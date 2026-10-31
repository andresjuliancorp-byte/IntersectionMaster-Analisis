using System.Collections.Generic;
using UnityEngine;
using IntersectionMaster.Core;
using IntersectionMaster.Data;

namespace IntersectionMaster.Traffic
{
    /// <summary>
    /// Red del modelo por carriles (como Intersection Controller 2): cada RoadData es un carril de un solo sentido.
    /// En un nodo, los carriles que llegan se unen a los que salen (del mismo tipo). No hay cruces "de calle":
    /// donde dos carriles se pisan se crea una zona de conflicto y los vehículos se ceden el paso allí.
    /// Las entradas y salidas son automáticas: un carril que no tiene ninguno antes genera vehículos y uno que no
    /// sigue a ninguno los retira.
    /// </summary>
    public partial class RoadNetworkManager
    {
        /// <summary>true si la red se construyó con el modelo por carriles.</summary>
        public bool LaneMode { get; private set; }

        /// <summary>Por nodo: el acceso único que agrupa los carriles que llegan y los que salen.</summary>
        public readonly Dictionary<int, Approach> hubs = new Dictionary<int, Approach>();

        /// <summary>Carriles (segmentos) por id de calle.</summary>
        public readonly Dictionary<int, RoadSeg> laneByRoad = new Dictionary<int, RoadSeg>();

        void BuildLaneMode(MapData map)
        {
            hubs.Clear();
            laneByRoad.Clear();
            foreach (var nd in map.nodes)
            {
                var n = new NetNode { id = nd.id, data = nd, pos = nd.Pos, weight = Mathf.Max(0f, nd.weight), ctrl = nd.ctrl, green = nd.green, yellow = nd.yellow };
                nodes.Add(n);
                nodeById[n.id] = n;
                var hub = new Approach { node = n, atB = true, halfWidth = LaneWidth * 0.5f };
                n.approaches.Add(hub);
                hubs[n.id] = hub;
            }

            foreach (var rd in map.roads)
            {
                NetNode na, nb;
                if (!nodeById.TryGetValue(rd.a, out na) || !nodeById.TryGetValue(rd.b, out nb)) continue;
                var nr = new NetRoad { data = rd, center = map.CenterOf(rd), limited = rd.limited };
                nr.length = nr.center.Length;
                float w = rd.Width;
                nr.halfWidth = w * 0.5f;
                var ha = hubs[na.id];
                var hb = hubs[nb.id];
                nr.appA = ha; nr.appB = hb;
                if (ha.road == null) ha.road = nr;
                if (hb.road == null) hb.road = nr;
                roads.Add(nr);
                roadById[rd.id] = nr;

                var seg = NewSeg(SegKind.Lane, nr.center);
                seg.laneMode = true;
                seg.laneType = rd.laneType;
                seg.roadId = rd.id;
                seg.width = w;
                seg.fromNode = na; seg.toNode = nb;
                seg.fromApp = ha; seg.toApp = hb;
                seg.laneIndex = 0;
                seg.speedCap = SpeedCapOf(rd);
                seg.stopS = seg.Length;
                seg.allow = rd.laneType == RoadData.LaneVehicle ? rd.GetLaneAllow(true, 0) : 0;
                seg.tram = rd.laneType == RoadData.LaneVehicle && rd.tram;
                seg.layer = rd.layer;
                seg.ramp = rd.ramp;
                seg.noChange = rd.noLaneChange;
                seg.avoid = rd.avoid;
                seg.crossRank = rd.laneType == RoadData.LaneSidewalk ? 4 : (rd.laneType == RoadData.LaneRail ? 5 : (rd.priority ? 3 : 2));
                nr.lanesAB = new[] { seg };
                nr.lanesBA = new RoadSeg[0];
                hb.inLanes.Add(seg);
                ha.outLanes.Add(seg);
                laneByRoad[rd.id] = seg;

                // Las aceras se caminan en los dos sentidos, sin importar en cuál se dibujaron.
                bool hasOpp = false;
                if (rd.laneType == RoadData.LaneSidewalk)
                    foreach (var o in map.roads)
                    {
                        // Ya hay otra acera pegada que va al revés (como en los mapas que dibujaron dos): no se duplica.
                        if (o == rd || o.laneType != RoadData.LaneSidewalk) continue;
                        if (Vector2.Distance(map.PosOf(o.a), map.PosOf(rd.b)) < 0.8f && Vector2.Distance(map.PosOf(o.b), map.PosOf(rd.a)) < 0.8f) { hasOpp = true; break; }
                    }
                if (rd.laneType == RoadData.LaneSidewalk && !hasOpp)
                {
                    var tw = NewSeg(SegKind.Lane, nr.center.Reversed());
                    tw.laneMode = true; tw.twin = true;
                    tw.laneType = rd.laneType; tw.roadId = rd.id; tw.width = w;
                    tw.fromNode = nb; tw.toNode = na; tw.fromApp = hb; tw.toApp = ha;
                    tw.laneIndex = 0; tw.speedCap = seg.speedCap; tw.stopS = tw.Length; tw.allow = 0;
                    tw.layer = rd.layer + rd.ramp; tw.ramp = -rd.ramp; tw.noChange = rd.noLaneChange; tw.crossRank = 4;
                    ha.inLanes.Add(tw);
                    hb.outLanes.Add(tw);
                }
            }

            ApplySigns(map);
            ApplyLaneRules(map);

            // Uniones en cada nodo: cada carril que llega sigue por los que salen del mismo tipo (sin dar media vuelta).
            foreach (var n in nodes)
            {
                var hub = hubs[n.id];
                foreach (var inL in hub.inLanes)
                {
                    Vector2 tin = inL.path.TangentAt(inL.Length);
                    foreach (var outL in hub.outLanes)
                    {
                        if (outL == inL || outL.laneType != inL.laneType) continue;
                        Vector2 tout = outL.path.TangentAt(0f);
                        float turn = Vector2.Angle(tin, tout);
                        if (turn > 135f) continue;
                        Link(inL, outL);
                        float signed = Vector2.SignedAngle(tin, tout);
                        inL.moves |= Mathf.Abs(signed) <= 35f ? 2 : (signed > 0f ? 1 : 4);
                    }
                }
            }

            // Línea de parada: al final del carril si allí hay semáforo, stop o ceda.
            foreach (var seg in segs)
            {
                var hub = seg.toApp;
                if (hub != null && (hub.hasLight || seg.endRank < 2)) seg.stopS = Mathf.Max(0.05f, seg.Length - 0.04f);
            }

            ComputeLaneConflicts();
            PullBackStopLines();
            BuildLaneSets();
            BuildPoints(map);

            foreach (var n in nodes)
            {
                var hub = hubs[n.id];
                // Entradas y salidas de vehículos: solo cuentan los carriles que no son acera (una acera se camina en
                // los dos sentidos: el sentido en que se dibujó no la convierte en entrada ni en salida).
                int nIn = 0, nOut = 0;
                foreach (var l in hub.inLanes) if (!l.twin && l.laneType != RoadData.LaneSidewalk) nIn++;
                foreach (var l in hub.outLanes) if (!l.twin && l.laneType != RoadData.LaneSidewalk) nOut++;
                n.entry = nIn == 0 && nOut > 0 && n.weight > 0.0001f;
                n.exit = nOut == 0 && nIn > 0;
                // Extremo de una acera (llega una sola acera y nada más): los peatones entran y salen por él.
                var swRoads = new HashSet<int>();
                int other = 0;
                foreach (var l in hub.inLanes) { if (l.laneType == RoadData.LaneSidewalk) swRoads.Add(l.roadId); else other++; }
                foreach (var l in hub.outLanes) { if (l.laneType == RoadData.LaneSidewalk) swRoads.Add(l.roadId); else other++; }
                n.walkEnd = swRoads.Count == 1 && other == 0;
                if (n.walkEnd && n.weight > 0.0001f) { n.entry = true; n.exit = true; }
                if (n.entry) entries.Add(n);
                if (n.exit) exits.Add(n);
            }
        }

        static float SpeedCapOf(RoadData rd)
        {
            if (rd.speedKmh > 0) return Mathf.Clamp(rd.speedKmh / 50f, 0.3f, 1.8f);
            return rd.limited ? 0.55f : 1f;
        }

        /// <summary>Reglas de cada carril en su nodo final (stop, ceda, prioridad) y prioridad de los carriles que salen de allí.</summary>
        void ApplyLaneRules(MapData map)
        {
            foreach (var sg in map.signs)
            {
                RoadSeg lane;
                if (!laneByRoad.TryGetValue(sg.road, out lane) || lane.toNode == null || lane.toNode.id != sg.node) continue;
                int rank;
                switch (sg.type)
                {
                    case SignType.Stop: if (sg.style == 2) continue; rank = 0; break;   // controlado por el usuario: va como semáforo
                    case SignType.Yield: rank = 1; break;
                    case SignType.Priority: rank = 3; break;
                    default: continue;
                }
                lane.endRank = rank;
            }
            // Los carriles que salen de un nodo con stop/ceda (y que no son peatonales ni tren) cruzan con esa prioridad.
            foreach (var n in nodes)
            {
                var hub = hubs[n.id];
                // Solo si TODOS los carriles que llegan tienen la regla (nodo de «línea de ceda» antes de un cruce).
                // En una confluencia con regla en un solo carril (entrada a glorieta) la regla vale para la confluencia.
                int min = 2;
                bool any = false, all = hub.inLanes.Count > 0;
                foreach (var il in hub.inLanes)
                {
                    if (il.endRank == 2) { all = false; continue; }
                    any = true;
                    if (il.endRank < min || min == 2) min = il.endRank;
                }
                if (!any || !all) continue;
                foreach (var ol in hub.outLanes)
                    if ((ol.laneType == RoadData.LaneVehicle || ol.laneType == RoadData.LaneBike) && ol.crossRank != 3) ol.crossRank = min;
            }
        }

        // ---------------- Zonas de conflicto ----------------

        struct Box { public float x0, y0, x1, y1; }

        static Box BoxOf(RoadSeg s)
        {
            var b = new Box { x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue };
            foreach (var p in s.path.pts)
            {
                if (p.x < b.x0) b.x0 = p.x;
                if (p.y < b.y0) b.y0 = p.y;
                if (p.x > b.x1) b.x1 = p.x;
                if (p.y > b.y1) b.y1 = p.y;
            }
            float m = s.width;
            b.x0 -= m; b.y0 -= m; b.x1 += m; b.y1 += m;
            return b;
        }

        static bool SharedNodeNear(RoadSeg a, RoadSeg b, Vector2 p)
        {
            NetNode[] na = { a.fromNode, a.toNode };
            NetNode[] nb = { b.fromNode, b.toNode };
            foreach (var x in na)
                foreach (var y in nb)
                    if (x != null && x == y && Vector2.Distance(x.pos, p) < Mathf.Max(a.width, b.width) * 1.6f) return true;
            return false;
        }

        void ComputeLaneConflicts()
        {
            var lanes = segs;
            var boxes = new Box[lanes.Count];
            for (int i = 0; i < lanes.Count; i++) boxes[i] = BoxOf(lanes[i]);
            var found = new List<Vector2>();
            for (int i = 0; i < lanes.Count; i++)
            {
                var a = lanes[i];
                var ba = boxes[i];
                for (int j = i + 1; j < lanes.Count; j++)
                {
                    var b = lanes[j];
                    var bb = boxes[j];
                    if (bb.x0 > ba.x1 || bb.x1 < ba.x0 || bb.y0 > ba.y1 || bb.y1 < ba.y0) continue;
                    // Puentes: solo se cruzan si comparten capa en algún tramo (las rampas tienen dos alturas).
                    int aLo = Mathf.Min(a.layer, a.layer + a.ramp), aHi = Mathf.Max(a.layer, a.layer + a.ramp);
                    int bLo = Mathf.Min(b.layer, b.layer + b.ramp), bHi = Mathf.Max(b.layer, b.layer + b.ramp);
                    if (aHi < bLo || bHi < aLo) continue;
                    if (a.laneType == RoadData.LaneSidewalk && b.laneType == RoadData.LaneSidewalk) continue;   // los peatones se esquivan
                    if (a.laneType == RoadData.LaneShoulder || b.laneType == RoadData.LaneShoulder) continue;   // por la berma no circula nadie
                    if (a.outs.Contains(b) || b.outs.Contains(a)) continue;
                    found.Clear();
                    var pa = a.path; var pb = b.path;
                    for (int u = 0; u < pa.pts.Length - 1; u++)
                    {
                        for (int v = 0; v < pb.pts.Length - 1; v++)
                        {
                            float t, w;
                            if (!Geo.SegSeg(pa.pts[u], pa.pts[u + 1], pb.pts[v], pb.pts[v + 1], out t, out w)) continue;
                            float sA = pa.cum[u] + t * (pa.cum[u + 1] - pa.cum[u]);
                            float sB = pb.cum[v] + w * (pb.cum[v + 1] - pb.cum[v]);
                            Vector2 p = pa.PointAt(sA);
                            if (SharedNodeNear(a, b, p) || a.LayerAt(sA) != b.LayerAt(sB)) continue;
                            found.Add(new Vector2(sA, sB));
                        }
                    }
                    if (found.Count == 0)
                    {
                        // Sin cruce exacto: ¿pasan tan cerca que los vehículos se rozarían? Entonces también es una zona de cesión.
                        float best = float.MaxValue, bsA = 0f, bsB = 0f;
                        for (int u = 0; u < pa.pts.Length - 1; u++)
                        {
                            for (int v = 0; v < pb.pts.Length - 1; v++)
                            {
                                float t, w;
                                float d = SegDistance(pa.pts[u], pa.pts[u + 1], pb.pts[v], pb.pts[v + 1], out t, out w);
                                if (d < best)
                                {
                                    best = d;
                                    bsA = pa.cum[u] + t * (pa.cum[u + 1] - pa.cum[u]);
                                    bsB = pb.cum[v] + w * (pb.cum[v + 1] - pb.cum[v]);
                                }
                            }
                        }
                        float near = (a.width + b.width) * 0.5f * 0.92f;
                        // Dos carriles de vehículos que se acercan en ángulo (p. ej. un giro que se junta con el carril
                        // recto antes de la confluencia): los coches se tocan con las esquinas antes que con los costados,
                        // así que hace falta más margen. Los carriles paralelos (vecinos de la misma calle) no cuentan.
                        // Solo si poco después confluyen en el mismo carril (si no, se crearían esperas de más dentro de los cruces).
                        if (best < 0.45f && a.laneType != RoadData.LaneSidewalk && b.laneType != RoadData.LaneSidewalk
                            && Vector2.Angle(pa.TangentAt(bsA), pb.TangentAt(bsB)) > 15f && MergeSoon(a, b))
                            near = Mathf.Max(near, 0.44f);
                        if (best < near && a.LayerAt(bsA) == b.LayerAt(bsB) && !SharedNodeNear(a, b, pa.PointAt(bsA)) && !SharedNodeNear(a, b, pb.PointAt(bsB)))
                            found.Add(new Vector2(bsA, bsB));
                    }
                    // Zonas de cada punto; las que se tocan en los dos carriles se funden en una sola (si no, dos coches
                    // podrían quedarse cada uno dentro de la zona del otro esperándose para siempre).
                    var zones = new List<Vector4>();   // (a0, a1, b0, b1)
                    float thr = (a.width + b.width) * 0.5f * 0.95f;
                    foreach (var f in found)
                    {
                        // Zona medida: el tramo de cada carril que queda más cerca del otro que el ancho de los dos
                        // (en cruces muy cerrados o casi de frente es largo), más un margen por las esquinas de los coches.
                        Vector2 ta = pa.TangentAt(f.x), tb = pb.TangentAt(f.y);
                        float sin = Mathf.Max(Mathf.Abs(Geo.Cross(ta, tb)), 0.3f);
                        float minA = (b.width * 0.5f + 0.12f) / sin + 0.08f, minB = (a.width * 0.5f + 0.12f) / sin + 0.08f;
                        float a0 = Extent(pa, pb, f.x, -1f, thr), a1 = Extent(pa, pb, f.x, 1f, thr);
                        float b0 = Extent(pb, pa, f.y, -1f, thr), b1 = Extent(pb, pa, f.y, 1f, thr);
                        const float corner = 0.15f;
                        zones.Add(new Vector4(Mathf.Min(f.x - Mathf.Min(minA, 1.3f), a0 - corner), Mathf.Max(f.x + Mathf.Min(minA, 1.3f), a1 + corner),
                                              Mathf.Min(f.y - Mathf.Min(minB, 1.3f), b0 - corner), Mathf.Max(f.y + Mathf.Min(minB, 1.3f), b1 + corner)));
                    }
                    for (bool merged = true; merged;)
                    {
                        merged = false;
                        for (int p1 = 0; p1 < zones.Count && !merged; p1++)
                            for (int p2 = p1 + 1; p2 < zones.Count && !merged; p2++)
                            {
                                var z1 = zones[p1]; var z2 = zones[p2];
                                bool touchA = z1.x <= z2.y + 0.3f && z2.x <= z1.y + 0.3f;
                                bool touchB = z1.z <= z2.w + 0.3f && z2.z <= z1.w + 0.3f;
                                if (!touchA && !touchB) continue;
                                zones[p1] = new Vector4(Mathf.Min(z1.x, z2.x), Mathf.Max(z1.y, z2.y), Mathf.Min(z1.z, z2.z), Mathf.Max(z1.w, z2.w));
                                zones.RemoveAt(p2);
                                merged = true;
                            }
                    }
                    foreach (var z in zones)
                    {
                        float myS = (z.x + z.y) * 0.5f, clearA = (z.y - z.x) * 0.5f;
                        float oS = (z.z + z.w) * 0.5f, clearB = (z.w - z.z) * 0.5f;
                        a.conflicts.Add(new Conflict { other = b, myS = myS, otherS = oS, clear = clearA, otherClear = clearB });
                        b.conflicts.Add(new Conflict { other = a, myS = oS, otherS = myS, clear = clearB, otherClear = clearA });
                    }
                }
            }
        }

        /// <summary>
        /// Un vehículo parado en una línea de detención no debe quedar dentro de una zona de cruce (ni de este carril ni
        /// de los que siguen): si la zona llega hasta ahí, la línea se adelanta hacia atrás para dejarla libre.
        /// </summary>
        void PullBackStopLines()
        {
            foreach (var seg in segs)
            {
                if (seg.stopS >= seg.Length) continue;
                float limit = seg.stopS;
                foreach (var c in seg.conflicts)
                {
                    float z0 = c.myS - c.clear;
                    if (z0 < limit && c.myS + c.clear > limit - 0.6f) limit = Mathf.Min(limit, z0 - 0.03f);
                }
                foreach (var o in seg.outs)
                    foreach (var c in o.conflicts)
                    {
                        float z0 = c.myS - c.clear;
                        if (z0 < 0f) limit = Mathf.Min(limit, seg.Length + z0 - 0.03f);
                        // Peatón: si la zona del cruce empieza casi al comenzar el tramo siguiente, espera un poco antes
                        // (con margen), no pegado a la zona.
                        else if (seg.laneType == RoadData.LaneSidewalk && o.laneType == RoadData.LaneSidewalk && z0 < 0.15f && c.other != null && c.other.laneType != RoadData.LaneSidewalk)
                            limit = Mathf.Min(limit, seg.Length + z0 - 0.15f);
                    }
                // Acera cuyo cruce empieza ya en su inicio (acera corta antes de la calle): no se puede esperar en este
                // tramo sin quedar dentro de la zona; se espera en el tramo anterior (línea en 0 = ya pasada).
                if (seg.laneType == RoadData.LaneSidewalk && limit < 0.02f) { seg.stopS = 0f; continue; }
                seg.stopS = Mathf.Clamp(limit, Mathf.Min(0.3f, seg.Length), seg.Length);
            }
        }

        /// <summary>¿Los dos carriles acaban en el mismo carril en uno o dos pasos (una confluencia)?</summary>
        static bool MergeSoon(RoadSeg a, RoadSeg b)
        {
            var near = new HashSet<RoadSeg>();
            foreach (var o in a.outs) { near.Add(o); foreach (var o2 in o.outs) near.Add(o2); }
            foreach (var o in b.outs)
            {
                if (near.Contains(o)) return true;
                foreach (var o2 in o.outs) if (near.Contains(o2)) return true;
            }
            return false;
        }

        /// <summary>Hasta dónde (en sentido dir desde s) el carril p sigue más cerca del carril q que thr. Máximo 3 unidades.</summary>
        static float Extent(Polyline p, Polyline q, float s, float dir, float thr)
        {
            float cur = s;
            for (int i = 0; i < 60; i++)
            {
                float nx = cur + dir * 0.05f;
                if (nx < -0.0001f || nx > p.Length + 0.0001f) break;
                float d;
                q.Project(p.PointAt(nx), out d);
                if (d > thr) break;
                cur = nx;
            }
            return cur;
        }

        /// <summary>Distancia mínima entre los segmentos p1-p2 y q1-q2; t y u son las fracciones de los puntos más cercanos.</summary>
        static float SegDistance(Vector2 p1, Vector2 p2, Vector2 q1, Vector2 q2, out float t, out float u)
        {
            float tt, uu;
            if (Geo.SegSeg(p1, p2, q1, q2, out tt, out uu)) { t = tt; u = uu; return 0f; }
            float best = float.MaxValue; t = u = 0f;
            float k;
            float d = Geo.DistToSegment(p1, q1, q2, out k); if (d < best) { best = d; t = 0f; u = k; }
            d = Geo.DistToSegment(p2, q1, q2, out k); if (d < best) { best = d; t = 1f; u = k; }
            d = Geo.DistToSegment(q1, p1, p2, out k); if (d < best) { best = d; t = k; u = 0f; }
            d = Geo.DistToSegment(q2, p1, p2, out k); if (d < best) { best = d; t = k; u = 1f; }
            return best;
        }

        // ---------------- Carriles paralelos (para cambiar de carril) ----------------

        void BuildLaneSets()
        {
            var lanes = new List<RoadSeg>();
            foreach (var s in segs) if (s.laneType == RoadData.LaneVehicle && s.Length > 2.2f) lanes.Add(s);
            int n = lanes.Count;
            var parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;
            System.Func<int, int> find = null;
            find = x => { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; };
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                    if (Parallel(lanes[i], lanes[j])) parent[find(i)] = find(j);

            var groups = new Dictionary<int, List<RoadSeg>>();
            for (int i = 0; i < n; i++)
            {
                int r = find(i);
                List<RoadSeg> g;
                if (!groups.TryGetValue(r, out g)) { g = new List<RoadSeg>(); groups[r] = g; }
                g.Add(lanes[i]);
            }
            foreach (var g in groups.Values)
            {
                if (g.Count < 2) continue;
                var refLane = g[0];
                Vector2 mid = refLane.path.PointAt(refLane.Length * 0.5f);
                Vector2 right = Geo.Right(refLane.path.TangentAt(refLane.Length * 0.5f));
                var keyed = new List<KeyValuePair<float, RoadSeg>>();
                foreach (var l in g)
                {
                    float d;
                    float sp = l.path.Project(mid, out d);
                    keyed.Add(new KeyValuePair<float, RoadSeg>(Vector2.Dot(l.path.PointAt(sp) - mid, right), l));
                }
                keyed.Sort((x, y) => x.Key.CompareTo(y.Key));
                var arr = new RoadSeg[keyed.Count];
                for (int i = 0; i < arr.Length; i++) { arr[i] = keyed[i].Value; arr[i].laneIndex = i; arr[i].laneSet = arr; }
            }
        }

        static bool Parallel(RoadSeg a, RoadSeg b)
        {
            if (a.layer != b.layer) return false;
            float lw = Mathf.Max(a.width, b.width);
            int hits = 0;
            for (int k = 1; k <= 3; k++)
            {
                float s = a.Length * k / 4f;
                Vector2 p = a.path.PointAt(s);
                float d;
                float sb = b.path.Project(p, out d);
                if (sb <= 0.05f || sb >= b.Length - 0.05f) continue;
                if (d < lw * 0.7f || d > lw * 1.35f) continue;
                if (Vector2.Dot(a.path.TangentAt(s), b.path.TangentAt(sb)) < 0.94f) continue;
                hits++;
            }
            return hits >= 2;
        }

        // ---------------- Consultas ----------------

        /// <summary>Acceso (agrupación de carriles) de un nodo en el modelo por carriles.</summary>
        public Approach HubOf(int nodeId)
        {
            Approach a;
            hubs.TryGetValue(nodeId, out a);
            return a;
        }
    }
}
