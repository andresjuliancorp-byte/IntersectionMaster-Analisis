using System.Collections.Generic;
using UnityEngine;
using IntersectionMaster.Core;
using IntersectionMaster.Data;

namespace IntersectionMaster.Traffic
{
    /// <summary>
    /// Convierte un mapa del modelo antiguo (calles con varios carriles y cruces en los nodos) al modelo por carriles
    /// (como Intersection Controller 2): cada carril y cada giro dentro de un cruce pasa a ser un carril de un solo
    /// sentido entre dos nodos. Se conservan las señales (en el nodo donde termina cada carril), los puntos lógicos,
    /// las flechas, los botones de semáforos y la decoración. Los pasos de peatones pasan a ser aceras que cruzan.
    /// </summary>
    public static class LaneConverter
    {
        class NodeIndex
        {
            readonly MapData map;
            readonly Dictionary<long, List<NodeData>> cells = new Dictionary<long, List<NodeData>>();
            const float Cell = 0.25f, Tol = 0.03f;

            public NodeIndex(MapData map) { this.map = map; }

            static long Key(int cx, int cy) { return ((long)cx << 32) ^ (uint)cy; }

            public NodeData At(Vector2 p)
            {
                int cx = Mathf.FloorToInt(p.x / Cell), cy = Mathf.FloorToInt(p.y / Cell);
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        List<NodeData> list;
                        if (!cells.TryGetValue(Key(cx + dx, cy + dy), out list)) continue;
                        for (int i = 0; i < list.Count; i++) if (Vector2.Distance(list[i].Pos, p) <= Tol) return list[i];
                    }
                p = map.Clamp(p);
                var n = new NodeData { id = map.NewObjectId(), x = p.x, y = p.y, weight = 0f };
                map.nodes.Add(n);
                List<NodeData> cell;
                if (!cells.TryGetValue(Key(cx, cy), out cell)) { cell = new List<NodeData>(); cells[Key(cx, cy)] = cell; }
                cell.Add(n);
                return n;
            }
        }

        public static void Convert(MapData m)
        {
            if (m == null || m.IsLaneMap) return;

            // 1) Red del modelo antiguo: carriles, conectores de giro y arcos de rotonda.
            var net = new RoadNetworkManager();
            net.Build(m);

            var oldNodes = m.nodes;
            var oldRoads = m.roads;
            var oldSigns = m.signs;
            var oldPoints = m.points;
            var oldArrows = m.arrows;
            var oldButtons = m.buttons;
            var oldById = new Dictionary<int, NodeData>();
            foreach (var n in oldNodes) oldById[n.id] = n;

            m.nodes = new List<NodeData>();
            m.roads = new List<RoadData>();
            m.signs = new List<SignItem>();
            m.points = new List<PointItem>();
            m.arrows = new List<LaneArrowItem>();
            m.crosswalks = new List<CrosswalkItem>();
            m.buttons = new List<GroupButton>();
            m.laneFormat = MapData.LaneFormatCurrent;

            var index = new NodeIndex(m);
            var firstPart = new Dictionary<RoadSeg, RoadData>();   // primer tramo nuevo de cada segmento
            var lastPart = new Dictionary<RoadSeg, RoadData>();    // último tramo (el que llega al nodo final)

            foreach (var seg in net.segs)
            {
                if (seg.path == null || seg.Length < 0.02f) continue;
                var src = SourceRoad(seg);
                var parts = SplitForQuadratic(seg.path);
                RoadData first = null, last = null;
                foreach (var part in parts)
                {
                    var na = index.At(part.Start);
                    var nb = index.At(part.End);
                    if (na == nb) continue;
                    var rd = new RoadData
                    {
                        id = m.NewObjectId(), a = na.id, b = nb.id, lanesAB = 1, lanesBA = 0,
                        layer = seg.layer, noLaneChange = seg.noChange, laneWidth = RoadNetworkManager.LaneWidth
                    };
                    SetBend(rd, part);
                    if (src != null)
                    {
                        rd.hidden = src.hidden; rd.color = src.color; rd.limited = src.limited;
                        rd.laneType = src.pedestrian ? RoadData.LaneSidewalk : RoadData.LaneVehicle;
                    }
                    if (seg.allow != 0 && seg.allow != RoadData.PedestrianMask) rd.allow = seg.allow;
                    if (seg.kind == SegKind.Lane && src != null) StyleLane(rd, seg, src);
                    // Glorietas: el anillo y las salidas tienen prioridad sobre quien entra.
                    if (seg.kind == SegKind.Ring || (seg.kind == SegKind.Connector && seg.inApp == null && seg.fromNode != null && seg.fromNode.roundabout)) rd.priority = true;
                    m.roads.Add(rd);
                    if (first == null) first = rd;
                    last = rd;
                }
                if (first != null) { firstPart[seg] = first; lastPart[seg] = last; }
            }

            // 2) Pesos de entrada y salida: solo donde el mapa antiguo tenía entradas y salidas.
            foreach (var seg in net.segs)
            {
                if (seg.kind != SegKind.Lane) continue;
                RoadData rd;
                if (seg.fromNode != null && seg.ins.Count == 0 && firstPart.TryGetValue(seg, out rd))
                {
                    NodeData on;
                    var nn = m.Node(rd.a);
                    if (nn != null && oldById.TryGetValue(seg.fromNode.id, out on) && on.entry) nn.weight = Mathf.Max(nn.weight, on.weight <= 0f ? 0f : on.weight);
                }
                if (seg.toNode != null && seg.outs.Count == 0 && lastPart.TryGetValue(seg, out rd))
                {
                    NodeData on;
                    var nn = m.Node(rd.b);
                    if (nn != null && oldById.TryGetValue(seg.toNode.id, out on) && on.exit) nn.weight = Mathf.Max(nn.weight, Mathf.Max(0.01f, on.weight));
                }
            }

            // 3) Señales: en el nodo donde termina cada carril que llegaba al acceso.
            foreach (var sg in oldSigns)
            {
                if (sg.type == SignType.SpeedLimit || sg.type == SignType.NoLeft || sg.type == SignType.NoRight || sg.type == SignType.NoStraight) continue;
                NetRoad nr;
                if (!net.roadById.TryGetValue(sg.road, out nr)) continue;
                var app = sg.node == nr.data.a ? nr.appA : (sg.node == nr.data.b ? nr.appB : null);
                if (app == null) continue;
                NodeData on;
                oldById.TryGetValue(sg.node, out on);
                foreach (var il in app.inLanes)
                {
                    RoadData rd;
                    if (!lastPart.TryGetValue(il, out rd)) continue;
                    m.signs.Add(new SignItem { road = rd.id, node = rd.b, type = sg.type, model = sg.model, size = sg.size, side = sg.side, phase = sg.phase, style = sg.style });
                    var end = m.Node(rd.b);
                    if (end != null && on != null) { end.ctrl = on.ctrl; end.green = on.green; end.yellow = on.yellow; }
                }
            }
            // Entradas a rotonda: ceda el paso al llegar al anillo.
            foreach (var seg in net.segs)
            {
                if (!seg.ringEntry) continue;
                RoadData rd;
                if (lastPart.TryGetValue(seg, out rd)) m.signs.Add(new SignItem { road = rd.id, node = rd.b, type = SignType.Yield });
            }

            // 4) Puntos lógicos (parada, peaje, detector): sobre el carril más cercano del sentido indicado.
            foreach (var pt in oldPoints)
            {
                NetRoad nr;
                var rdOld = FindOld(oldRoads, pt.road);
                if (rdOld == null || !net.roadById.TryGetValue(pt.road, out nr)) continue;
                Vector2 pos = PointOnOld(m, oldById, rdOld, pt.t);
                for (int dir = 0; dir < 2; dir++)
                {
                    if (pt.dir != 2 && pt.dir != dir) continue;
                    var lanes = dir == 0 ? nr.lanesAB : nr.lanesBA;
                    if (lanes.Length == 0) continue;
                    if (pt.kind == PointKind.BusStop) AddPoint(m, firstPart, lanes[lanes.Length - 1], pos, pt);
                    else foreach (var l in lanes) AddPoint(m, firstPart, l, pos, pt);
                }
            }

            // 5) Flechas pintadas.
            foreach (var ar in oldArrows)
            {
                NetRoad nr;
                if (!net.roadById.TryGetValue(ar.road, out nr)) continue;
                var lanes = ar.forward ? nr.lanesAB : nr.lanesBA;
                if (ar.lane < 0 || ar.lane >= lanes.Length) continue;
                RoadData rd;
                if (!firstPart.TryGetValue(lanes[ar.lane], out rd)) continue;
                var na = m.AddArrow(rd.id, true, 0, ar.t, ar.moves);
                if (na != null) na.size = ar.size;
            }

            // 6) Botones de varios semáforos.
            foreach (var bt in oldButtons)
            {
                var nb = new GroupButton { id = bt.id, x = bt.x, y = bt.y };
                foreach (var l in bt.links)
                {
                    NetRoad nr;
                    if (!net.roadById.TryGetValue(l.road, out nr)) continue;
                    var app = l.node == nr.data.a ? nr.appA : (l.node == nr.data.b ? nr.appB : null);
                    if (app == null) continue;
                    foreach (var il in app.inLanes)
                    {
                        RoadData rd;
                        if (lastPart.TryGetValue(il, out rd)) nb.links.Add(new ButtonLink { road = rd.id, node = rd.b });
                    }
                }
                m.buttons.Add(nb);
            }

            // 7) Pasos de peatones: una acera en cada sentido que cruza la calzada.
            foreach (var cw in net.crosswalks)
            {
                float half = cw.halfWidth + 0.3f;
                for (int k = -1; k <= 1; k += 2)
                {
                    Vector2 shift = cw.axis * (0.08f * k);
                    Vector2 p0 = cw.center + shift - cw.across * half * k;
                    Vector2 p1 = cw.center + shift + cw.across * half * k;
                    var na = index.At(p0);
                    var nb = index.At(p1);
                    if (na == nb) continue;
                    m.roads.Add(new RoadData { id = m.NewObjectId(), a = na.id, b = nb.id, lanesAB = 1, lanesBA = 0, laneType = RoadData.LaneSidewalk, laneWidth = 0.3f });
                }
            }

            foreach (var n in m.nodes) n.roundabout = false;
            m.autoArrows = false;
            m.autoCrosswalks = false;
            m.autoStopLines = false;
        }

        static RoadData SourceRoad(RoadSeg seg)
        {
            if (seg.kind == SegKind.Lane && seg.fromApp != null && seg.fromApp.road != null) return seg.fromApp.road.data;
            if (seg.inLane != null && seg.inLane.fromApp != null && seg.inLane.fromApp.road != null) return seg.inLane.fromApp.road.data;
            return null;
        }

        static RoadData FindOld(List<RoadData> roads, int id)
        {
            for (int i = 0; i < roads.Count; i++) if (roads[i].id == id) return roads[i];
            return null;
        }

        static Vector2 PointOnOld(MapData m, Dictionary<int, NodeData> nodes, RoadData r, float t)
        {
            NodeData na, nb;
            if (!nodes.TryGetValue(r.a, out na) || !nodes.TryGetValue(r.b, out nb)) return Vector2.zero;
            Vector2 a = na.Pos, b = nb.Pos;
            Vector2 c = (a + b) * 0.5f + new Vector2(r.bendX, r.bendY);
            return r.Curved ? Geo.Quad(a, c, b, t) : Vector2.Lerp(a, b, t);
        }

        static void AddPoint(MapData m, Dictionary<RoadSeg, RoadData> firstPart, RoadSeg lane, Vector2 pos, PointItem pt)
        {
            RoadData rd;
            if (!firstPart.TryGetValue(lane, out rd)) return;
            float d;
            float s = lane.path.Project(pos, out d);
            float t = Mathf.Clamp(s / Mathf.Max(0.01f, lane.Length), 0.05f, 0.95f);
            m.points.Add(new PointItem { id = m.NewObjectId(), road = rd.id, t = t, kind = pt.kind, seconds = pt.seconds, dir = 2 });
        }

        /// <summary>Líneas pintadas de cada carril según su posición en la calle original.</summary>
        static void StyleLane(RoadData rd, RoadSeg seg, RoadData src)
        {
            if (src.noMarks) return;
            int count = seg.laneSet != null ? seg.laneSet.Length : 1;
            bool forward = seg.fromApp != null && !seg.fromApp.atB;
            int opposite = forward ? src.lanesBA : src.lanesAB;
            rd.lineR = seg.laneIndex == count - 1 ? 1 : 0;
            if (seg.laneIndex > 0) rd.lineL = 3;
            else if (opposite == 0) rd.lineL = 1;
            else if (forward) rd.lineL = (src.lanesAB == 1 && src.lanesBA == 1 && src.MedianWidth <= 0.01f) ? 4 : 5;
            if (src.rails && seg.laneIndex == count - 1) rd.rails = true;
            if (src.cables && seg.laneIndex == count - 1 && forward) rd.cables = true;
        }

        /// <summary>
        /// Un carril nuevo es una bézier cuadrática. Si el tramo gira mucho (arcos de rotonda, curvas cerradas)
        /// se parte en varios para que la forma se parezca a la original.
        /// </summary>
        static List<Polyline> SplitForQuadratic(Polyline p)
        {
            var result = new List<Polyline>();
            float turn = Mathf.Abs(Vector2.SignedAngle(p.TangentAt(0f), p.TangentAt(p.Length)));
            float total = 0f;
            for (int i = 1; i < p.pts.Length - 1; i++)
            {
                Vector2 d0 = p.pts[i] - p.pts[i - 1], d1 = p.pts[i + 1] - p.pts[i];
                if (d0.sqrMagnitude > 1e-8f && d1.sqrMagnitude > 1e-8f) total += Mathf.Abs(Vector2.SignedAngle(d0, d1));
            }
            turn = Mathf.Max(turn, total);
            int pieces = Mathf.Clamp(Mathf.CeilToInt(turn / 80f), 1, 6);
            if (p.Length < 0.5f) pieces = 1;
            for (int k = 0; k < pieces; k++) result.Add(p.Sub(p.Length * k / pieces, p.Length * (k + 1) / pieces));
            return result;
        }

        /// <summary>Punto de control de la cuadrática que pasa por el punto medio del tramo.</summary>
        static void SetBend(RoadData rd, Polyline part)
        {
            if (part.pts.Length <= 2) return;
            Vector2 a = part.Start, b = part.End;
            Vector2 mid = part.PointAt(part.Length * 0.5f);
            Vector2 bend = 2f * (mid - (a + b) * 0.5f);
            if (bend.sqrMagnitude < 0.0025f) return;
            rd.bendX = bend.x;
            rd.bendY = bend.y;
        }
    }
}
