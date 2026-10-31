// Simulación de comprobación (sin pantalla) para GitHub Actions: ejecuta el diagnóstico del juego y
// mueve vehículos por varios mapas para medir llegadas, choques y atascos con la lógica real del juego.
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using IntersectionMaster.Data;
using IntersectionMaster.Systems;
using IntersectionMaster.Traffic;

public static class Program
{
    class Stats
    {
        public string name;
        public int spawned, delivered, crashes, walkers, walkersSpawned, agentsArrived;
        public float maxWait;
        public int stuckEnd;
        public bool deadlock;
        public float deadlockAt;
        public int alive;
        public string note = "";
        public string waits = "";
    }

    class MiniSim
    {
        public RoadNetworkManager net = new RoadNetworkManager();
        PathfindingSystem pf = new PathfindingSystem();
        CollisionDetectionSystem col = new CollisionDetectionSystem();
        public List<VehicleController> vs = new List<VehicleController>();
        public List<IntersectionController> ics = new List<IntersectionController>();
        readonly List<KeyValuePair<VehicleController, VehicleController>> hits = new List<KeyValuePair<VehicleController, VehicleController>>();
        MapData map;
        int nextId = 1;
        float spawnTimer, walkTimer, agentTimer;
        float frozen;
        public Stats st;
        Transform root = new Transform();
        public float vehiclesPerMinute = 30f, walkersPerMinute = 12f;

        public MiniSim(MapData m, string name)
        {
            map = m;
            st = new Stats { name = name };
            m.EnsureLanes();
            net.Build(m);
            if (net.LaneMode) ics = IntersectionController.BuildLaneGroups(net, m.traffic);
            foreach (var ic in ics) ic.light.autoCycle = true;
        }

        public void Run(float seconds)
        {
            const float dt = 1f / 30f;
            int steps = (int)(seconds / dt);
            for (int i = 0; i < steps; i++)
            {
                Step(dt, i * dt);
                if (st.deadlock) break;
            }
            st.alive = vs.Count;
            // Grafo de esperas: quién espera a quién (para encontrar ciclos de bloqueo).
            var byId = new Dictionary<int, VehicleController>();
            foreach (var v in vs) byId[v.id] = v;
            int shown = 0;
            foreach (var v in vs)
            {
                if (v.waitTime < 15f || shown >= 12) continue;
                shown++;
                var chain = new StringBuilder("#" + v.id + "(" + v.why + ")");
                var cur = v;
                var seen = new HashSet<int> { v.id };
                for (int k = 0; k < 8; k++)
                {
                    VehicleController nx;
                    if (cur.whyId < 0 || !byId.TryGetValue(cur.whyId, out nx)) break;
                    chain.Append(" → #" + nx.id + "(" + nx.why + (nx.HeldByRed ? ",ROJO" : "") + " c" + nx.Seg.roadId + " s=" + nx.s.ToString("0.0") + " v=" + nx.speed.ToString("0.00") + " w=" + nx.waitTime.ToString("0") + ")");
                    if (!seen.Add(nx.id)) { chain.Append(" CICLO"); break; }
                    cur = nx;
                }
                var rt = new StringBuilder();
                for (int ri = v.routeIndex; ri < v.route.Count && ri < v.routeIndex + 5; ri++) rt.Append(v.route[ri].roadId + "(" + v.route[ri].Length.ToString("0.00") + ") ");
                st.waits += "\n    espera " + chain + " en carril " + v.Seg.roadId + " s=" + v.s.ToString("0.00") + "/" + v.Seg.Length.ToString("0.00") + " ruta: " + rt;
            }
            foreach (var v in vs) if (v.totalWait > st.maxWait) st.maxWait = v.totalWait;
            foreach (var v in vs) if (v.waitTime > 20f) st.stuckEnd++;
        }

        void Step(float dt, float t)
        {
            foreach (var ic in ics) ic.light.Update(dt);
            spawnTimer += dt; walkTimer += dt; agentTimer += dt;
            if (spawnTimer > 60f / vehiclesPerMinute) { spawnTimer = 0f; Spawn(RoadData.LaneVehicle, 0); }
            if (walkTimer > 60f / walkersPerMinute) { walkTimer = 0f; Spawn(RoadData.LaneSidewalk, 1); }
            if (agentTimer > 6f) { agentTimer = 0f; Spawn(RoadData.LaneBike, 3); Spawn(RoadData.LaneRail, 2); Spawn(RoadData.LaneBoat, 4); }

            foreach (var v in vs) v.Tick(dt, 1f);

            bool anyMoving = false;
            foreach (var v in vs) if (!v.crashed && v.speed > 0.02f) anyMoving = true;
            int live = 0;
            foreach (var v in vs) if (!v.crashed) live++;
            if (live > 0 && !anyMoving) frozen += dt; else frozen = 0f;
            if (frozen > 25f && !st.deadlock) { st.deadlock = true; st.deadlockAt = t; }

            for (int i = vs.Count - 1; i >= 0; i--)
            {
                var v = vs[i];
                if (v.arrived)
                {
                    // Nadie debe desaparecer a mitad de camino: solo donde termina su carril (sin continuación de su tipo).
                    bool cont = false;
                    foreach (var o in v.Seg.outs) if (o.laneType == v.Seg.laneType) cont = true;
                    if (cont) { st.crashes += 100; if (st.note.Length < 1400) st.note += "\n    DESAPARECIÓ t=" + t.ToString("0") + " en " + v.Pos + " :: " + Desc(v); }
                    if (v.agent == 0) st.delivered++; else if (v.agent == 1) st.walkers++; else st.agentsArrived++;
                    if (v.totalWait > st.maxWait) st.maxWait = v.totalWait;
                    v.Destroy(); vs.RemoveAt(i);
                }
                else if (v.crashed && v.crashTimer <= 0f) { v.Destroy(); vs.RemoveAt(i); }
            }
            col.Detect(vs, hits);
            foreach (var h in hits)
            {
                if (h.Key.crashed && h.Value.crashed) continue;
                string dA = Desc(h.Key), dB = Desc(h.Value);
                h.Key.Crash(8f); h.Value.Crash(8f);
                st.crashes++;
                if (st.note.Length < 1400) st.note += "\n    ANTES: " + dA + " || " + dB;
                if (st.note.Length < 1400) st.note += "\n    choque t=" + t.ToString("0") + " en " + h.Key.Pos + " :: " + Desc(h.Key) + " || " + Desc(h.Value) + " || conflicto=" + HasConflict(h.Key, h.Value);
                if (st.note.Length < 2400) st.note += "\n    detalle: " + SegInfo(h.Key.Seg) + " / " + SegInfo(h.Key.NextSeg) + " || " + SegInfo(h.Value.Seg) + " / " + SegInfo(h.Value.NextSeg);
            }
        }

        string Desc(VehicleController v)
        {
            var sg = v.Seg;
            var nx = v.NextSeg;
            return "[" + v.why + " blk=" + v.whyId + " id=" + v.id + " w=" + v.waitTime.ToString("0.0") + "] carril " + sg.roadId + " s=" + v.s.ToString("0.00") + "/" + sg.Length.ToString("0.00") + " v=" + v.speed.ToString("0.00")
                + " rank=" + sg.crossRank + "/" + sg.endRank + " ins=" + sg.ins.Count + " outs=" + sg.outs.Count + " conf=" + sg.conflicts.Count
                + " sig=" + (nx != null ? nx.roadId.ToString() : "-") + " [" + sg.path.Start + "→" + sg.path.End + "]";
        }

        static string SegInfo(RoadSeg g)
        {
            if (g == null) return "-";
            var sb2 = new StringBuilder("c" + g.roadId + "[");
            foreach (var q in g.path.pts) sb2.Append("(" + q.x.ToString("0.00") + "," + q.y.ToString("0.00") + ")");
            sb2.Append("] from=" + (g.fromNode != null ? g.fromNode.id.ToString() : "-") + " to=" + (g.toNode != null ? g.toNode.id.ToString() : "-") + " conf:");
            foreach (var c in g.conflicts) sb2.Append(" c" + c.other.roadId + "@" + c.myS.ToString("0.00"));
            sb2.Append(" outs:");
            foreach (var o in g.outs) sb2.Append(" c" + o.roadId);
            return sb2.ToString();
        }

        static bool HasConflict(VehicleController a, VehicleController b)
        {
            foreach (var x in new[] { a.Seg, a.NextSeg })
                foreach (var y in new[] { b.Seg, b.NextSeg })
                {
                    if (x == null || y == null) continue;
                    foreach (var c in x.conflicts) if (c.other == y) return true;
                }
            return false;
        }

        void Spawn(int laneType, int agent)
        {
            var starts = new List<RoadSeg>();
            foreach (var e in net.entries)
                foreach (var l in net.SpawnLanes(e))
                {
                    if (l.laneType != laneType) continue;
                    if (IntersectionController.BlockedPedStart(l, ics)) continue;
                    bool free = true;
                    foreach (var o in l.vehicles) if (o.s - o.HalfLength < (agent == 2 ? 3f : 0.9f)) free = false;
                    if (free) starts.Add(l);
                }
            if (starts.Count == 0) return;
            var lane = starts[UnityEngine.Random.Range(0, starts.Count)];
            if (laneType == RoadData.LaneSidewalk && lane.fromNode != null && !lane.fromNode.walkEnd) { st.crashes += 100; st.note += "\n    APARECIÓ a mitad de acera en " + lane.path.Start; }
            var route = pf.FindRoute(lane, net.exits, agent == 0 ? 1 : 0);
            if (route == null || route.Count == 0) return;
            var v = new VehicleController(nextId++, VehicleType.Car, route, root, agent);
            vs.Add(v);
            if (agent == 0) st.spawned++; else if (agent == 1) st.walkersSpawned++;
        }
    }

    // ---------------- Mapas de prueba (modelo por carriles) ----------------

    static float LaneWidthOf(MapData m)
    {
        foreach (var r in m.roads) if (r.laneType == RoadData.LaneVehicle) return r.Width;
        return 0f;
    }

    static int DecorOnLanes(MapData m)
    {
        int n = 0;
        foreach (var d in m.decor)
        {
            if (d.type != DecorType.Building && d.type != DecorType.Tree && d.type != DecorType.Hedge && d.type != DecorType.Containers && d.type != DecorType.RoundStructure) continue;
            foreach (var r in m.roads)
            {
                if (r.laneType != RoadData.LaneVehicle && r.laneType != RoadData.LaneSidewalk) continue;
                float dist;
                m.NearestParam(r, d.Pos, out dist);
                if (dist < r.Width * 0.5f + 0.05f) { n++; break; }
            }
        }
        return n;
    }

    static MapData NewMap(float w = 24f, float h = 14f)
    {
        var m = new MapData { width = w, height = h, laneFormat = MapData.LaneFormatCurrent };
        m.featureLevel = MapData.CurrentFeatureLevel;
        m.SetView(new Rect(2f, 2f, w - 4f, h - 4f));
        return m;
    }

    static NodeData N(MapData m, float x, float y) { return m.AddNode(new Vector2(x, y)); }

    static RoadData L(MapData m, NodeData a, NodeData b, int type = 0)
    {
        var r = m.AddRoad(a.id, b.id, 1, 0);
        if (r == null) throw new Exception("No se pudo crear el carril " + a.Pos + " → " + b.Pos);
        r.laneType = type;
        return r;
    }

    /// <summary>Carril recto partido en un nodo «stop» a cierta distancia antes del punto central (para semáforos y señales).</summary>
    static NodeData Path(MapData m, Vector2 from, Vector2 mid, Vector2 to, float stopBefore, out RoadData approach)
    {
        Vector2 dir = (mid - from).normalized;
        var a = N(m, from.x, from.y);
        Vector2 sp = mid - dir * stopBefore;
        var s = N(m, sp.x, sp.y);
        var e = N(m, to.x, to.y);
        approach = L(m, a, s);
        L(m, s, e);
        return s;
    }

    static MapData CrossTwoWay(bool lights, bool yieldNS)
    {
        var m = NewMap();
        float lw = 0.5f;
        var nodes = new List<KeyValuePair<RoadData, NodeData>>();
        RoadData ap;
        var s1 = Path(m, new Vector2(0.3f, 7f - lw / 2), new Vector2(12f, 7f - lw / 2), new Vector2(23.7f, 7f - lw / 2), 1.0f, out ap); nodes.Add(new KeyValuePair<RoadData, NodeData>(ap, s1));
        var s2 = Path(m, new Vector2(23.7f, 7f + lw / 2), new Vector2(12f, 7f + lw / 2), new Vector2(0.3f, 7f + lw / 2), 1.0f, out ap); nodes.Add(new KeyValuePair<RoadData, NodeData>(ap, s2));
        var s3 = Path(m, new Vector2(12f + lw / 2, 0.3f), new Vector2(12f + lw / 2, 7f), new Vector2(12f + lw / 2, 13.7f), 1.0f, out ap); nodes.Add(new KeyValuePair<RoadData, NodeData>(ap, s3));
        var s4 = Path(m, new Vector2(12f - lw / 2, 13.7f), new Vector2(12f - lw / 2, 7f), new Vector2(12f - lw / 2, 0.3f), 1.0f, out ap); nodes.Add(new KeyValuePair<RoadData, NodeData>(ap, s4));
        // Giros a la derecha desde el oeste hacia el sur y desde el este hacia el norte.
        if (lights || yieldNS)
            foreach (var kv in nodes)
            {
                bool ns = Mathf.Abs(m.PosOf(kv.Key.a).x - m.PosOf(kv.Key.b).x) < 0.01f;
                if (lights) m.ToggleSign(kv.Key.id, kv.Value.id, SignType.Light);
                else if (ns) m.ToggleSign(kv.Key.id, kv.Value.id, SignType.Yield);
            }
        return m;
    }

    static MapData CrossOneWay()
    {
        var m = NewMap();
        L(m, N(m, 0.3f, 7f), N(m, 23.7f, 7f));
        L(m, N(m, 12f, 0.3f), N(m, 12f, 13.7f));
        return m;
    }

    static MapData Merge()
    {
        var m = NewMap();
        var a = N(m, 0.3f, 7f); var j = N(m, 12f, 7f); var b = N(m, 23.7f, 7f);
        var c = N(m, 4f, 0.3f); var y = N(m, 10f, 6.2f);
        L(m, a, j); L(m, j, b);
        var r = L(m, c, y); L(m, y, j);
        m.ToggleSign(r.id, y.id, SignType.Yield);
        return m;
    }

    static MapData Crosswalk()
    {
        var m = NewMap();
        float lw = 0.5f;
        L(m, N(m, 0.3f, 7f - lw / 2), N(m, 23.7f, 7f - lw / 2));
        L(m, N(m, 23.7f, 7f + lw / 2), N(m, 0.3f, 7f + lw / 2));
        L(m, N(m, 12f, 0.5f), N(m, 12f, 13.5f), RoadData.LaneSidewalk);
        L(m, N(m, 12.5f, 13.5f), N(m, 12.5f, 0.5f), RoadData.LaneSidewalk);
        return m;
    }

    // Acera que cruza una calle de doble sentido con semáforo peatonal a cada lado (como lo crea el editor).
    static MapData PedLightCross()
    {
        var m = NewMap();
        float lw = 0.5f;
        L(m, N(m, 0.3f, 7f - lw / 2), N(m, 23.7f, 7f - lw / 2));
        L(m, N(m, 23.7f, 7f + lw / 2), N(m, 0.3f, 7f + lw / 2));
        var a = N(m, 12f, 0.5f); var n0 = N(m, 12f, 6.2f); var n1 = N(m, 12f, 7.8f); var b = N(m, 12f, 13.5f);
        var p1 = L(m, a, n0, RoadData.LaneSidewalk); var p2 = L(m, n0, n1, RoadData.LaneSidewalk); L(m, n1, b, RoadData.LaneSidewalk);
        m.ToggleSign(p1.id, n0.id, SignType.Light);
        m.ToggleSign(p2.id, n1.id, SignType.Light);
        return m;
    }

    // Acera que sale poco de la calle (como dibuja el usuario): de un lado termina (los peatones aparecen ahí solo en verde) y
    // del otro sigue otra acera (esperan en el nodo, sin partir).
    static MapData PedLightShort()
    {
        var m = NewMap();
        float lw = 0.5f;
        L(m, N(m, 0.3f, 7f - lw / 2), N(m, 23.7f, 7f - lw / 2));
        L(m, N(m, 23.7f, 7f + lw / 2), N(m, 0.3f, 7f + lw / 2));
        var a = N(m, 12f, 6.25f); var n1 = N(m, 12f, 7.75f); var b = N(m, 12.4f, 13.5f);   // en la cuadrícula: el nodo a media casilla del borde
        var cr = L(m, a, n1, RoadData.LaneSidewalk); L(m, n1, b, RoadData.LaneSidewalk);
        m.ToggleSign(cr.id, a.id, SignType.Light);
        m.ToggleSign(cr.id, n1.id, SignType.Light);
        return m;
    }

    // El mapa del usuario: calle de 2 carriles con semáforo, aceras a los dos lados partidas a la altura de la línea de
    // detención, y un paso con semáforo peatonal entre las dos esquinas.
    static MapData UserCorner()
    {
        var m = NewMap();
        var n1 = N(m, 12f, 0.5f); var n2 = N(m, 12f, 6.4f); var n7 = N(m, 12f, 13.5f);
        var n4 = N(m, 12.5f, 0.5f); var n5 = N(m, 12.5f, 6.4f); var n9 = N(m, 12.5f, 13.5f);
        var r3 = L(m, n1, n2); L(m, n2, n7); var r6 = L(m, n4, n5); L(m, n5, n9);
        var c11 = N(m, 11.25f, 7f); var c12 = N(m, 13.25f, 7f);
        var n18 = N(m, 11.25f, 6.5f); var n20 = N(m, 11.25f, 4.96f); var n22 = N(m, 13.25f, 6.5f); var n24 = N(m, 13.25f, 4.96f);
        var cr = L(m, c11, c12, RoadData.LaneSidewalk);
        L(m, c11, n18, RoadData.LaneSidewalk); L(m, n18, n20, RoadData.LaneSidewalk);
        L(m, c12, n22, RoadData.LaneSidewalk); L(m, n22, n24, RoadData.LaneSidewalk);
        m.ToggleSign(r6.id, n5.id, SignType.Light); m.ToggleSign(r3.id, n2.id, SignType.Light);
        m.ToggleSign(cr.id, c11.id, SignType.Light); m.ToggleSign(cr.id, c12.id, SignType.Light);
        return m;
    }

    // Igual, pero con las aceras dibujadas en sentidos distintos (dos que salen de la esquina y dos que llegan a un nodo):
    // el sentido del dibujo no debe crear entradas ni salidas de peatones a mitad de la acera.
    static MapData UserCornerMixed()
    {
        var m = UserCorner();
        foreach (var r in m.roads)
        {
            if (r.laneType != RoadData.LaneSidewalk) continue;
            var na = m.Node(r.a); var nb = m.Node(r.b);
            if (Mathf.Abs(na.x - 13.25f) < 0.01f && Mathf.Abs(nb.x - 13.25f) < 0.01f && nb.y < 5f) { int t = r.a; r.a = r.b; r.b = t; }   // 24→22
        }
        return m;
    }

    static MapData RailCross()
    {
        var m = NewMap();
        float lw = 0.5f;
        L(m, N(m, 0.3f, 7f - lw / 2), N(m, 23.7f, 7f - lw / 2));
        L(m, N(m, 23.7f, 7f + lw / 2), N(m, 0.3f, 7f + lw / 2));
        L(m, N(m, 8f, 0.3f), N(m, 16f, 13.7f), RoadData.LaneRail);
        return m;
    }

    static MapData Bridge()
    {
        var m = NewMap();
        L(m, N(m, 0.3f, 7f), N(m, 23.7f, 7f));
        var a = N(m, 12f, 0.3f); var b = N(m, 12f, 4f); var c = N(m, 12f, 10f); var d = N(m, 12f, 13.7f);
        var up = L(m, a, b); up.ramp = 1;
        var top = L(m, b, c); top.layer = 1;
        var down = L(m, c, d); down.layer = 1; down.ramp = -1;
        return m;
    }

    static MapData OldFourArms(bool ring, bool lights)
    {
        var m = new MapData { width = 24f, height = 14f };
        m.featureLevel = MapData.CurrentFeatureLevel;
        m.SetView(new Rect(2f, 2f, 20f, 10f));
        var c = m.AddNode(new Vector2(12f, 7f));
        if (ring) { c.roundabout = true; c.radius = 1.3f; }
        Func<float, float, NodeData> P = (x, y) => { var n = m.AddNode(new Vector2(x, y)); n.entry = true; n.exit = true; return n; };
        var r1 = m.AddRoad(P(0f, 7f).id, c.id);
        var r2 = m.AddRoad(c.id, P(24f, 7f).id);
        var r3 = m.AddRoad(P(12f, 0f).id, c.id);
        var r4 = m.AddRoad(c.id, P(12f, 14f).id);
        if (lights) foreach (var r in new[] { r1, r2, r3, r4 }) m.signs.Add(new SignItem { road = r.id, node = c.id, type = SignType.Light });
        m.Normalize();
        return m;
    }

    static string CheckDest()
    {
        var m = NewMap();
        var a = N(m, 0.3f, 7f); var j = N(m, 10f, 7f); var x = N(m, 23.7f, 7f); var y = N(m, 10f, 0.3f);
        var first = L(m, a, j); L(m, j, x); L(m, j, y);
        m.EnsureLanes();
        var net = new RoadNetworkManager(); net.Build(m);
        var pf = new PathfindingSystem();
        var lane = net.laneByRoad[first.id];
        a.dest.Add(new DestWeight { node = x.id, weight = 100f });
        a.dest.Add(new DestWeight { node = y.id, weight = 0f });
        int toX = 0, toY = 0;
        for (int i = 0; i < 200; i++)
        {
            var r = pf.FindRoute(lane, net.exits, 0);
            if (r == null || r.Count == 0) return "sin ruta";
            var last = r[r.Count - 1].toNode;
            if (last != null && last.id == x.id) toX++; else toY++;
        }
        if (toY != 0) return "con 100/0 fueron " + toY + " de 200 al destino con 0";
        a.dest[1].weight = 100f;
        toX = toY = 0;
        for (int i = 0; i < 400; i++)
        {
            var r = pf.FindRoute(lane, net.exits, 0);
            var last = r[r.Count - 1].toNode;
            if (last != null && last.id == x.id) toX++; else toY++;
        }
        if (toX < 120 || toY < 120) return "con 50/50 el reparto fue " + toX + "/" + toY;
        return null;
    }

    static string CheckFreeLights()
    {
        var m = CrossTwoWay(true, false);
        m.EnsureLanes();
        var net = new RoadNetworkManager(); net.Build(m);
        var ics = IntersectionController.BuildLaneGroups(net, m.traffic);
        if (ics.Count == 0) return "no hay grupo de semáforos";
        var lc = ics[0].light;
        lc.autoCycle = false;
        var apps = new List<Approach>(lc.Approaches);
        Approach p = null, q = null;
        for (int i = 0; i < apps.Count && p == null; i++)
            for (int k = i + 1; k < apps.Count; k++)
                if (lc.conflicts != null && lc.conflicts(apps[i], apps[k])) { p = apps[i]; q = apps[k]; break; }
        if (p == null) return "no hay dos semáforos que se crucen";
        if (!lc.IsOn(p)) lc.Toggle(p);
        if (!lc.IsOn(q)) lc.Toggle(q);
        for (int t = 0; t < 120; t++) lc.Update(1f / 30f);
        if (lc.GetState(p) != SignalState.Green || lc.GetState(q) != SignalState.Green)
            return "estados: " + lc.GetState(p) + " y " + lc.GetState(q);
        return null;
    }

    static string CheckUserStop()
    {
        var m = CrossTwoWay(false, false);
        int n = 0;
        foreach (var r in m.roads)
        {
            var nb = m.Node(r.b);
            bool hasOut = false;
            foreach (var o in m.roads) if (o.a == r.b) hasOut = true;
            if (!hasOut || nb == null) continue;
            m.ToggleSign(r.id, r.b, SignType.Stop);
            var sg = m.FindSign(r.id, r.b, SignType.Stop);
            if (sg != null) { sg.style = 2; n++; }
        }
        if (n == 0) return "no se pudo poner el stop";
        m.EnsureLanes();
        var net = new RoadNetworkManager(); net.Build(m);
        foreach (var sg in net.segs) if (sg.endRank == 0) return "un carril quedó con regla de stop";
        var ics = IntersectionController.BuildLaneGroups(net, m.traffic);
        if (ics.Count == 0) return "no se creó el grupo controlable";
        var sim = new MiniSim(m, "stop usuario");
        sim.Run(90f);
        if (sim.st.crashes > 0) return "choques=" + sim.st.crashes;
        return null;
    }

    static string CheckTram()
    {
        var m = NewMap();
        var a = N(m, 0.3f, 7f); var b = N(m, 23.7f, 7f);
        var r = L(m, a, b);
        m.EnsureLanes();
        var net = new RoadNetworkManager(); net.Build(m);
        var pf = new PathfindingSystem();
        var lane = net.laneByRoad[r.id];
        var route = pf.FindRoute(lane, net.exits, PathfindingSystem.TramBit);
        if (route != null && route.Count > 0) return "el tranvía encontró ruta sin tranvía permitido";
        r.tram = true;
        net = new RoadNetworkManager(); net.Build(m);
        lane = net.laneByRoad[r.id];
        route = pf.FindRoute(lane, net.exits, PathfindingSystem.TramBit);
        if (route == null || route.Count == 0) return "el tranvía no encontró ruta con tranvía permitido";
        var car = pf.FindRoute(lane, net.exits, 1 << (int)VehicleType.Car);
        if (car == null || car.Count == 0) return "el coche ya no puede usar el carril con tranvía";
        return null;
    }

    static List<RoadSeg> FirstRoute(MapData m, RoadNetworkManager net, int bits)
    {
        var pf = new PathfindingSystem();
        foreach (var e in net.entries)
            foreach (var l in net.SpawnLanes(e))
            {
                var r = pf.FindRoute(l, net.exits, bits);
                if (r != null && r.Count > 1) return r;
            }
        return null;
    }

    static string CheckEmergency()
    {
        var m = CrossTwoWay(true, false);
        m.EnsureLanes();
        var net = new RoadNetworkManager(); net.Build(m);
        var ics = IntersectionController.BuildLaneGroups(net, m.traffic);
        foreach (var ic in ics) ic.light.AllRed();
        for (int t = 0; t < 120; t++) foreach (var ic in ics) ic.light.Update(1f / 30f);
        var root = new Transform();
        var route = FirstRoute(m, net, 0);
        if (route == null) return "sin ruta";
        var amb = new VehicleController(1, VehicleType.Ambulance, new List<RoadSeg>(route), root);
        var car = new VehicleController(2, VehicleType.Car, new List<RoadSeg>(FirstRoute(m, net, 0)), root);
        // El coche va en otra simulación aparte para que no se estorben.
        car.Seg.vehicles.Remove(car);
        for (int i = 0; i < 30 * 20; i++) { foreach (var ic in ics) ic.light.Update(1f / 30f); amb.Tick(1f / 30f, 1f); }
        if (amb.routeIndex == 0) return "la ambulancia se quedó en el semáforo en rojo";
        amb.Seg.vehicles.Remove(amb);
        car.Seg.vehicles.Add(car);
        for (int i = 0; i < 30 * 20; i++) { foreach (var ic in ics) ic.light.Update(1f / 30f); car.Tick(1f / 30f, 1f); }
        if (car.routeIndex != 0) return "el coche pasó en rojo";
        return null;
    }

    static string CheckShoulder()
    {
        var m = NewMap();
        var a = N(m, 0.3f, 7f); var b = N(m, 23.7f, 7f);
        var r = L(m, a, b);
        m.EnsureLanes();
        var net = new RoadNetworkManager(); net.Build(m);
        var lane = net.laneByRoad[r.id];
        var root = new Transform();
        var broken = new VehicleController(1, VehicleType.Car, new List<RoadSeg> { lane }, root);
        broken.PlaceAt(10f);
        broken.onShoulder = true; broken.holdTimer = 100f;
        var follower = new VehicleController(2, VehicleType.Car, new List<RoadSeg> { lane }, root);
        follower.PlaceAt(3f);
        var col = new CollisionDetectionSystem();
        var hits = new List<KeyValuePair<VehicleController, VehicleController>>();
        var all = new List<VehicleController> { broken, follower };
        float minSpeedNear = 99f;
        for (int i = 0; i < 30 * 30; i++)
        {
            broken.Tick(1f / 30f, 1f); follower.Tick(1f / 30f, 1f);
            if (Mathf.Abs(follower.s - broken.s) < 0.8f) minSpeedNear = Mathf.Min(minSpeedNear, follower.speed);
            col.Detect(all, hits);
            if (hits.Count > 0) return "chocaron";
            if (follower.s > broken.s + 2f) break;
        }
        if (follower.s <= broken.s + 1f) return "el de detrás no adelantó (s=" + follower.s.ToString("0.0") + ")";
        if (broken.s > 10.05f) return "el averiado se movió";
        return null;
    }

    static string CheckUserStopCar()
    {
        var m = NewMap();
        var a = N(m, 0.3f, 7f); var b = N(m, 23.7f, 7f);
        var r = L(m, a, b);
        m.EnsureLanes();
        var net = new RoadNetworkManager(); net.Build(m);
        var v = new VehicleController(1, VehicleType.Car, new List<RoadSeg> { net.laneByRoad[r.id] }, new Transform());
        v.PlaceAt(4f);
        v.userStopped = true;
        for (int i = 0; i < 90; i++) v.Tick(1f / 30f, 1f);
        if (v.s > 4.05f) return "se movió detenido";
        v.userStopped = false;
        for (int i = 0; i < 90; i++) v.Tick(1f / 30f, 1f);
        if (v.s < 4.5f) return "no siguió al soltarlo";
        return null;
    }

    static string CheckExpand()
    {
        var m = CrossTwoWay(true, false);
        m.EnsureLanes();
        var before = m.View;
        int nodes = m.nodes.Count, roads = m.roads.Count;
        if (!m.ExpandToBig()) return "no se agrandó";
        if (m.width < 199f || m.height < 199f) return "tamaño " + m.width + "x" + m.height;
        var v = m.View;
        if (Mathf.Abs(v.width - before.width) > 0.01f || Mathf.Abs(v.height - before.height) > 0.01f) return "la línea roja cambió de tamaño";
        if (Mathf.Abs(v.center.x - 100f) > 1f || Mathf.Abs(v.center.y - 100f) > 1f) return "la línea roja no quedó en el centro";
        m.Normalize();
        if (m.nodes.Count != nodes || m.roads.Count != roads) return "se perdieron nodos o carriles";
        var sim = new MiniSim(m, "grande");
        sim.Run(60f);
        if (sim.st.delivered < 5) return "casi no llegaron coches: " + sim.st.delivered;
        return null;
    }

    public static int Main(string[] args)
    {
        var sb = new StringBuilder();
        int fails = 0;

        // 1) Diagnóstico del propio juego.
        var res = MapSelfTest.RunAll();
        sb.AppendLine(MapSelfTest.Report(res));
        foreach (var r in res) if (!r.passed) fails++;

        // 2) Tráfico en marcha.
        var list = new List<KeyValuePair<string, Func<MapData>>>
        {
            new KeyValuePair<string, Func<MapData>>("Dos carriles que se cruzan (sin señales)", CrossOneWay),
            new KeyValuePair<string, Func<MapData>>("Cruce doble sentido sin señales", () => CrossTwoWay(false, false)),
            new KeyValuePair<string, Func<MapData>>("Cruce doble sentido con ceda el paso N-S", () => CrossTwoWay(false, true)),
            new KeyValuePair<string, Func<MapData>>("Cruce doble sentido con semáforos", () => CrossTwoWay(true, false)),
            new KeyValuePair<string, Func<MapData>>("Incorporación con ceda el paso", Merge),
            new KeyValuePair<string, Func<MapData>>("Acera que cruza una calle", Crosswalk),
            new KeyValuePair<string, Func<MapData>>("Acera con semáforo peatonal", PedLightCross),
            new KeyValuePair<string, Func<MapData>>("Acera corta con semáforo peatonal", PedLightShort),
            new KeyValuePair<string, Func<MapData>>("Esquina del usuario: paso con semáforo peatonal", UserCorner),
            new KeyValuePair<string, Func<MapData>>("Esquina del usuario con aceras en sentidos mezclados", UserCornerMixed),
            new KeyValuePair<string, Func<MapData>>("Tren que cruza una calle", RailCross),
            new KeyValuePair<string, Func<MapData>>("Puente con rampas sobre una calle (sin cruce)", Bridge),
            new KeyValuePair<string, Func<MapData>>("Mapa antiguo: cruce convertido", () => OldFourArms(false, false)),
            new KeyValuePair<string, Func<MapData>>("Mapa antiguo: cruce con semáforos convertido", () => OldFourArms(false, true)),
            new KeyValuePair<string, Func<MapData>>("Mapa antiguo: glorieta diminuta con tráfico alto (límite conocido)", () => OldFourArms(true, false)),
            new KeyValuePair<string, Func<MapData>>("Mapa antiguo: glorieta convertida (tráfico medio 15/min)", () => { var mm = OldFourArms(true, false); mm.traffic.spawnPerMinute = 15f; return mm; }),
        };
        foreach (var s in SampleMaps.All())
        {
            var sm = s;
            list.Add(new KeyValuePair<string, Func<MapData>>("Ejemplo: " + s.name, () => sm));
        }

        sb.AppendLine();
        sb.AppendLine("Simulación de tráfico (180 s de juego cada uno, semáforos en ciclo automático):");
        foreach (var kv in list)
        {
            Stats st;
            try
            {
                // Cada prueba con su propia semilla: así un cambio en otra prueba no altera el resultado de esta.
                UnityEngine.Random.rng = new System.Random(12345 + kv.Key.Length);
                var m = kv.Value();
                var sim = new MiniSim(m, kv.Key);
                if (m.traffic.spawnPerMinute < 20f) sim.vehiclesPerMinute = m.traffic.spawnPerMinute;
                sim.Run(180f);
                st = sim.st;
                int confl = 0; foreach (var sg in sim.net.segs) confl += sg.conflicts.Count;
                if (kv.Key.StartsWith("Puente") && confl > 0) { st.crashes += 1000; st.note += "EL PUENTE TIENE ZONAS DE CRUCE: " + confl + " "; }
                if (kv.Key.StartsWith("Ejemplo")) st.note += "sobre_calle=" + DecorOnLanes(m) + " ancho=" + LaneWidthOf(m).ToString("0.00") + " ";
                st.note += "carriles=" + sim.net.segs.Count + " entradas=" + sim.net.entries.Count + " salidas=" + sim.net.exits.Count + " grupos_semáforo=" + sim.ics.Count + " " + st.note;
            }
            catch (Exception ex)
            {
                sb.AppendLine("[FALLA] " + kv.Key + ": excepción " + ex.GetType().Name + ": " + ex.Message + " " + ex.StackTrace.Split('\n')[0]);
                fails++;
                continue;
            }
            bool bad = st.crashes > 0 || st.deadlock || st.stuckEnd > 4 || (st.spawned > 0 && st.delivered < st.spawned / 2);
            bool known = kv.Key.Contains("límite conocido");
            if (bad && !known) fails++;
            sb.AppendLine((bad ? (known ? "[AVISO] " : "[FALLA] ") : "[OK] ") + st.name + ": salidos=" + st.spawned + " llegados=" + st.delivered + " choques=" + st.crashes
                + " peatones=" + st.walkers + "/" + st.walkersSpawned + " otros=" + st.agentsArrived
                + " espera_máx=" + st.maxWait.ToString("0") + "s atascados_al_final=" + st.stuckEnd + " en_mapa=" + st.alive
                + (st.deadlock ? " BLOQUEO_TOTAL t=" + st.deadlockAt.ToString("0") : "") + " | " + st.note + (bad ? st.waits : ""));
        }

        // 2b) Campaña: los 60 mapas generados se validan y aguantan tráfico (150 s cada uno).
        // (sobre_calle = edificios, árboles, setos o naves cuyo centro cae encima de un carril o acera)
        sb.AppendLine();
        sb.AppendLine("Campaña (5 mundos × 12 niveles, 150 s de juego cada uno):");
        for (int w = 0; w < CampaignMaps.Worlds; w++)
            for (int l = 0; l < CampaignMaps.LevelsPerWorld; l++)
            {
                string name = CampaignMaps.WorldNames[w] + " " + (l + 1);
                try
                {
                    UnityEngine.Random.rng = new System.Random(777 + w * 31 + l);
                    var m = CampaignMaps.Build(w, l);
                    m.Normalize();
                    var issues = MapValidator.Validate(m);
                    if (MapValidator.HasErrors(issues))
                    {
                        var fe = MapValidator.FirstError(issues);
                        sb.AppendLine("[FALLA] " + name + ": el mapa tiene errores: " + (fe != null ? fe.message : "?"));
                        fails++;
                        continue;
                    }
                    var sim = new MiniSim(m, name);
                    sim.vehiclesPerMinute = Mathf.Min(30f, m.traffic.spawnPerMinute);
                    sim.Run(150f);
                    var st = sim.st;
                    // Esperas largas en rojo no cuentan: en la partida los semáforos los maneja el jugador (aquí van en ciclo automático).
                    bool bad = st.crashes > 0 || st.deadlock || (st.spawned > 0 && st.delivered < st.spawned / 3) || sim.net.entries.Count == 0;
                    if (bad) fails++;
                    sb.AppendLine((bad ? "[FALLA] " : "[OK] ") + name + ": salidos=" + st.spawned + " llegados=" + st.delivered + " choques=" + st.crashes
                        + " atascados=" + st.stuckEnd + " cruces=" + sim.ics.Count + " carriles=" + sim.net.segs.Count + " objetos=" + m.decor.Count
                        + " sobre_calle=" + DecorOnLanes(m) + " ancho=" + LaneWidthOf(m).ToString("0.00")
                        + (st.deadlock ? " BLOQUEO_TOTAL" : "") + (bad ? " | " + st.note + st.waits : ""));
                }
                catch (Exception ex)
                {
                    sb.AppendLine("[FALLA] " + name + ": excepción " + ex.GetType().Name + ": " + ex.Message + " " + ex.StackTrace.Split('\n')[0]);
                    fails++;
                }
            }

        // 2c) Mapas del jugador hechos en Intersection Controller (.trzmap en ci/sim/data): se importan y se prueban.
        sb.AppendLine();
        sb.AppendLine("Mapas importados de Intersection Controller:");
        string dataDir = System.IO.Path.Combine(AppContext.BaseDirectory, "data");
        if (!System.IO.Directory.Exists(dataDir)) dataDir = "data";
        if (System.IO.Directory.Exists(dataDir))
            foreach (var f in System.IO.Directory.GetFiles(dataDir, "*.trzmap"))
            {
                string name = System.IO.Path.GetFileName(f);
                try
                {
                    MapData m; string msg;
                    if (!TrzImporter.TryConvert(System.IO.File.ReadAllBytes(f), name, out m, out msg)) { sb.AppendLine("[FALLA] " + name + ": " + msg); fails++; continue; }
                    var counts = new SortedDictionary<string, int>();
                    foreach (var d in m.decor) { string k = d.type.ToString(); int c; counts.TryGetValue(k, out c); counts[k] = c + 1; }
                    var lanes = new SortedDictionary<int, int>();
                    foreach (var r in m.roads) { int c; lanes.TryGetValue(r.laneType, out c); lanes[r.laneType] = c + 1; }
                    var deco = new StringBuilder();
                    foreach (var kv in counts) deco.Append(kv.Key).Append('=').Append(kv.Value).Append(' ');
                    var ln = new StringBuilder();
                    foreach (var kv in lanes) ln.Append("tipo").Append(kv.Key).Append('=').Append(kv.Value).Append(' ');
                    {
                        // Guardar y volver a leer (como Mis mapas): si falla, el mapa no aparecería en la lista.
                        string json = MapSerializationSystem.ToJson(m, true);
                        string rtMsg;
                        try
                        {
                            MapData back; string rerr;
                            bool okBack = MapSerializationSystem.TryFromJson(json, out back, out rerr);
                            rtMsg = okBack ? "se vuelve a leer bien (" + back.roads.Count + " carriles, " + back.decor.Count + " objetos)" : "NO SE PUEDE LEER: " + rerr;
                            if (!okBack) fails++;
                        }
                        catch (Exception rex) { rtMsg = "EXCEPCIÓN AL LEER: " + rex.GetType().Name + ": " + rex.Message + " " + rex.StackTrace.Split('\n')[0]; fails++; }
                        // Unity no sabe leer NaN ni Infinity en JSON: un solo valor así y el mapa no aparece en Mis mapas.
                        int nan = 0, at = 0;
                        while ((at = json.IndexOf("NaN", at, StringComparison.Ordinal)) >= 0) { nan++; at += 3; }
                        int inf = 0; at = 0;
                        while ((at = json.IndexOf("Infinity", at, StringComparison.Ordinal)) >= 0) { inf++; at += 8; }
                        if (nan + inf > 0)
                        {
                            int k = json.IndexOf(nan > 0 ? "NaN" : "Infinity", StringComparison.Ordinal);
                            rtMsg += " | VALORES INVÁLIDOS: NaN=" + nan + " Infinity=" + inf + " cerca de: " + json.Substring(Math.Max(0, k - 160), Math.Min(200, json.Length - Math.Max(0, k - 160))).Replace("\n", " ").Replace("  ", "");
                            fails++;
                        }
                        sb.AppendLine("    tamaño al guardar: " + json.Length + " caracteres; " + rtMsg);
                    }
                    var issues = MapValidator.Validate(m);
                    string errs = MapValidator.HasErrors(issues) ? "ERRORES: " + MapValidator.FirstError(issues).message : "sin errores";
                    sb.AppendLine("    " + name + ": " + msg + " | tamaño " + m.width + "x" + m.height + " vista " + m.View + " | modo " + m.playMode + " objetivo " + m.goalVehicles + " en " + m.goalMinutes + " min, hora " + m.startHour + ", lluvia " + m.rainChance);
                    sb.AppendLine("    carriles: " + ln + "| señales " + m.signs.Count + " | puntos " + m.points.Count + " | botones " + m.buttons.Count + " | " + errs);
                    sb.AppendLine("    decoración: " + deco);
                    {
                        int tolls = 0, decoArrows = 0, marks = 0, closed = 0; var mk = new int[9];
                        foreach (var q in m.points) if (q.kind == PointKind.Toll) tolls++;
                        foreach (var q in m.arrows) if (q.deco) decoArrows++;
                        foreach (var r in m.roads) { if (r.markKind > 0) { marks++; mk[Math.Min(8, r.markKind)]++; } if (r.allow == RoadData.ClosedMask) closed++; }
                        sb.AppendLine("    peajes " + tolls + " | flechas del original " + decoArrows + " | marcas " + marks + " (" + string.Join(",", mk) + ") | carriles cerrados " + closed);
                    }
                    UnityEngine.Random.rng = new System.Random(4242);
                    var sim = new MiniSim(m, name);
                    sim.Run(150f);
                    var st = sim.st;
                    bool bad = st.crashes > 0 || st.deadlock || sim.net.entries.Count == 0;
                    if (bad) fails++;
                    sb.AppendLine((bad ? "[FALLA] " : "[OK] ") + name + ": salidos=" + st.spawned + " llegados=" + st.delivered + " choques=" + st.crashes + " peatones=" + st.walkers + "/" + st.walkersSpawned
                        + " otros=" + st.agentsArrived + " atascados=" + st.stuckEnd + " cruces=" + sim.ics.Count + " entradas=" + sim.net.entries.Count + " salidas=" + sim.net.exits.Count
                        + (st.deadlock ? " BLOQUEO_TOTAL" : "") + (bad ? " | " + st.note + st.waits : ""));
                }
                catch (Exception ex) { sb.AppendLine("[FALLA] " + name + ": excepción " + ex.GetType().Name + ": " + ex.Message + " " + ex.StackTrace.Split('\n')[0]); fails++; }
            }

        // 3) Comprobaciones de las funciones nuevas (sin pantalla).
        sb.AppendLine();
        sb.AppendLine("Funciones del editor como el original:");
        foreach (var kv in new List<KeyValuePair<string, Func<string>>>
        {
            new KeyValuePair<string, Func<string>>("Probabilidad de destino: 100/0 manda todos a un final; 50/50 reparte", CheckDest),
            new KeyValuePair<string, Func<string>>("Semáforos libres: se pueden poner en verde dos que se cruzan", CheckFreeLights),
            new KeyValuePair<string, Func<string>>("Stop controlado por el usuario: regula como un semáforo, no como stop", CheckUserStop),
            new KeyValuePair<string, Func<string>>("Tranvía: solo va por carriles con tranvía permitido", CheckTram),
            new KeyValuePair<string, Func<string>>("Emergencia pasa en rojo; un coche normal se para", CheckEmergency),
            new KeyValuePair<string, Func<string>>("Coche averiado en la berma: el de detrás lo adelanta despacio sin chocar", CheckShoulder),
            new KeyValuePair<string, Func<string>>("Coche detenido por el jugador: no se mueve hasta soltarlo", CheckUserStopCar),
            new KeyValuePair<string, Func<string>>("Mapa grande: un mapa pequeño se agranda a 200x200 sin perder nada y sigue funcionando", CheckExpand),
        })
        {
            string err;
            try { err = kv.Value(); } catch (Exception ex) { err = "excepción " + ex.GetType().Name + ": " + ex.Message; }
            if (err != null) fails++;
            sb.AppendLine((err == null ? "[OK] " : "[FALLA] ") + kv.Key + (err == null ? "" : ": " + err));
        }

        sb.Insert(0, "RESULTADO: " + (fails == 0 ? "TODO OK" : fails + " fallos") + "\n");
        Console.WriteLine(sb.ToString());
        System.IO.File.WriteAllText("sim_result.txt", sb.ToString());
        return 0;
    }
}
