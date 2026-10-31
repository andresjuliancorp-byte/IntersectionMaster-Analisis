using System.Collections.Generic;
using UnityEngine;
using IntersectionMaster.Core;
using IntersectionMaster.Data;

namespace IntersectionMaster.Traffic
{
    public enum SegKind { Lane, Connector, Ring }

    /// <summary>Un cruce entre dos conectores (o una confluencia): posición del cruce a lo largo de cada uno.</summary>
    public class Conflict
    {
        public RoadSeg other;
        public float myS;
        public float otherS;
        public bool merge;
        public float clear = 0.25f;       // modelo por carriles: medio largo de la zona de cruce sobre mi carril
        public float otherClear = 0.25f;  // ... y sobre el otro carril
    }

    /// <summary>Un tramo afectado por un paso de peatones: el frente del vehículo debe quedarse antes de limitFrontS.</summary>
    public class CwEntry
    {
        public Crosswalk cw;
        public float limitFrontS;
    }

    /// <summary>Un paso de peatones: franja sobre la calzada que cruzan los peatones.</summary>
    public class Crosswalk
    {
        public const float ZoneHalf = 0.14f;   // semilargo de la franja a lo largo de la calzada

        public int roadId;
        public Vector2 center;
        public Vector2 across;                 // vector unitario que cruza la calzada
        public Vector2 axis;                   // vector unitario a lo largo de la calzada
        public float halfWidth;                // medio ancho de la calzada
        public bool busy;                      // hay peatones sobre la calzada
        public Approach lightApp;              // acceso con semáforo junto al paso: los peatones cruzan cuando está en rojo
        public int crossing, waiting;
        public readonly List<CwEntry> entries = new List<CwEntry>();
    }

    /// <summary>
    /// Segmento recorrible por vehículos: un carril de calle (Lane), un conector dentro de un cruce (Connector)
    /// o un arco del anillo de una rotonda (Ring).
    /// </summary>
    /// <summary>Punto lógico sobre un carril: parada de autobús, peaje o detector (posición s a lo largo del carril).</summary>
    public class StopPoint
    {
        public float s;
        public PointKind kind;
        public float seconds;
        public float cooldown;     // detectores: tiempo hasta poder pedir verde otra vez
        public int srcId = -1;     // id del punto del mapa (para dibujar el detector encendido)
    }

    public class RoadSeg
    {
        public int id;
        public SegKind kind;
        public Polyline path;
        public NetNode fromNode, toNode;
        public Approach fromApp, toApp;       // carriles: aproximación en cada extremo
        public Approach inApp, outApp;        // conectores: aproximación de la que vienen / a la que van
        public RoadSeg inLane;                // conectores: carril de entrada
        public int laneIndex;
        public int move;                      // conectores: 0 recto, 1 izquierda, -1 derecha
        public int moves;                     // carriles: giros posibles desde su final (bits 1, 2, 4)
        public float speedCap = 1f;
        public bool avoid;                    // «Evitar»: muy caro para las rutas (solo si no hay otro camino)
        public int allow;                     // carriles: tipos de vehículo permitidos (0 = todos)
        public float stopS;                   // carriles: dónde se detiene el frente del vehículo ante el cruce
        public bool ringEntry;
        public RoadSeg mouth;                 // entrada a rotonda: arco del anillo frente a la boca de la calle
        public int priority;
        public int layer;                     // capa vertical: solo chocan vehículos de la misma capa
        public bool noChange;                 // carriles: prohibido cambiar de carril
        public readonly List<StopPoint> stops = new List<StopPoint>();
        public RoadSeg[] laneSet;             // carriles: todos los carriles del mismo sentido (por laneIndex)
        public readonly List<RoadSeg> outs = new List<RoadSeg>();
        public readonly List<RoadSeg> ins = new List<RoadSeg>();
        public readonly List<Conflict> conflicts = new List<Conflict>();
        public readonly List<VehicleController> vehicles = new List<VehicleController>();
        public readonly List<CwEntry> crosswalks = new List<CwEntry>();

        // ---- Modelo por carriles ----
        public bool laneMode;                 // red por carriles (cada RoadData es un carril)
        public int laneType;                  // RoadData.LaneVehicle, LaneSidewalk, ...
        public bool tram;                     // el tranvía puede usar este carril
        public int roadId;                    // id de la calle (carril) de origen
        public bool twin;                     // sentido contrario de una acera: los peatones caminan en los dos sentidos
        public int ramp;                      // rampa: la segunda mitad del carril está en layer + ramp

        /// <summary>Capa en la posición s (las rampas cambian de capa a mitad del carril).</summary>
        public int LayerAt(float s) { return layer + (ramp != 0 && s > Length * 0.5f ? ramp : 0); }
        public int crossRank = 2;             // prioridad al cruzar otro carril: 0 stop, 1 ceda, 2 normal, 3 prioridad, 4 peatones, 5 tren
        public int endRank = 2;               // regla en el nodo final de este carril (para confluencias): 0 stop, 1 ceda, 2 normal, 3 prioridad
        public float width = 0.36f;

        public float Length { get { return path.Length; } }
    }

    /// <summary>Extremo de una calle en un nodo.</summary>
    public class Approach
    {
        public NetNode node;
        public NetRoad road;
        public bool atB;                      // el extremo de la calle en este nodo es road.b
        public float angle;                   // rumbo (grados) que sale del nodo siguiendo la calle
        public float trim;                    // distancia del centro del nodo hasta el inicio de los carriles
        public float halfWidth;
        public bool hasCrosswalk;
        public bool hasRule;
        public bool hasLight;                 // tiene semáforo propio
        public int lightModel, lightSide, lightPhase;
        public bool lightHidden;
        public float lightSize = 1f;
        public SignType rule;
        public int banMask;                   // 1 = izquierda, 2 = recto, 4 = derecha
        public Vector2 edgeLeft, edgeRight;   // bordes de la calzada a la distancia trim (izquierda y derecha mirando hacia fuera del nodo)
        public Vector2 edgeDir;               // dirección saliente en ese punto
        public float ringIn, ringOut;         // rotondas: ángulos sobre el anillo
        public readonly List<RoadSeg> inLanes = new List<RoadSeg>();    // llegan al nodo
        public readonly List<RoadSeg> outLanes = new List<RoadSeg>();   // salen del nodo

        public Vector2 Outward { get { return Geo.FromAngle(angle); } }

        public bool CanTurn(int move)
        {
            int bit = move == 1 ? 1 : (move == 0 ? 2 : 4);
            return (banMask & bit) == 0;
        }
    }

    public class NetNode
    {
        public int id;
        public Vector2 pos;
        public bool entry, exit, roundabout;
        public bool walkEnd;   // extremo de acera: los peatones solo aparecen y desaparecen ahí
        public float radius;
        public float weight = 1f;
        public int ctrl;
        public float green, yellow;
        public float trim;
        public NodeData data;   // nodo del mapa (tráfico propio del nodo de inicio y destinos)
        public readonly List<Approach> approaches = new List<Approach>();
        public readonly List<RoadSeg> connectors = new List<RoadSeg>();
        public readonly List<RoadSeg> ringSegs = new List<RoadSeg>();
        public IntersectionController controller;

        public bool IsJunction { get { return roundabout || approaches.Count >= 3; } }
    }

    public class NetRoad
    {
        public RoadData data;
        public Polyline center;
        public float length;
        public float halfWidth;
        public float trimA, trimB;
        public bool limited;
        public Approach appA, appB;
        public RoadSeg[] lanesAB = new RoadSeg[0];
        public RoadSeg[] lanesBA = new RoadSeg[0];
    }

    /// <summary>
    /// Construye, a partir de un MapData, el grafo de carriles: carriles de cada calle, conectores dentro de cada cruce
    /// (uno por carril y giro permitido), anillos de rotonda, señales y pasos de peatones. Conducción por la derecha.
    /// </summary>
    public partial class RoadNetworkManager
    {
        public const float LaneWidth = 0.5f;   // como el original y el editor (antes 0,36)
        /// <summary>Cuánto más ancho es el carril que en los mapas antiguos (0,36): para agrandar glorietas y distancias.</summary>
        public const float WidthScale = LaneWidth / 0.36f;
        public const float RingHalfWidth = 0.24f;

        public readonly List<NetNode> nodes = new List<NetNode>();
        public readonly Dictionary<int, NetNode> nodeById = new Dictionary<int, NetNode>();
        public readonly List<NetRoad> roads = new List<NetRoad>();
        public readonly Dictionary<int, NetRoad> roadById = new Dictionary<int, NetRoad>();
        public readonly List<RoadSeg> segs = new List<RoadSeg>();
        public readonly List<NetNode> entries = new List<NetNode>();
        public readonly List<NetNode> exits = new List<NetNode>();
        public readonly List<Crosswalk> crosswalks = new List<Crosswalk>();
        public readonly List<RoadSeg> detectorLanes = new List<RoadSeg>();

        public void Clear()
        {
            nodes.Clear(); nodeById.Clear(); roads.Clear(); roadById.Clear(); segs.Clear(); LaneMode = false; hubs.Clear(); laneByRoad.Clear();
            entries.Clear(); exits.Clear(); crosswalks.Clear(); detectorLanes.Clear();
        }

        MapData srcMap;

        public void Build(MapData map)
        {
            Clear();
            srcMap = map;
            LaneMode = map.IsLaneMap;
            if (LaneMode) { BuildLaneMode(map); return; }

            foreach (var nd in map.nodes)
            {
                var n = new NetNode { id = nd.id, pos = nd.Pos, entry = nd.entry, exit = nd.exit, roundabout = nd.roundabout, radius = nd.radius * WidthScale, weight = Mathf.Max(0f, nd.weight), ctrl = nd.ctrl, green = nd.green, yellow = nd.yellow };
                nodes.Add(n);
                nodeById[n.id] = n;
            }

            foreach (var rd in map.roads)
            {
                NetNode na, nb;
                if (!nodeById.TryGetValue(rd.a, out na) || !nodeById.TryGetValue(rd.b, out nb)) continue;
                var nr = new NetRoad { data = rd, center = map.CenterOf(rd), limited = rd.limited };
                nr.length = nr.center.Length;
                nr.halfWidth = (rd.lanesAB + rd.lanesBA) * LaneWidth * 0.5f + rd.MedianWidth * 0.5f;
                nr.appA = new Approach
                {
                    node = na, road = nr, atB = false, halfWidth = nr.halfWidth,
                    angle = Geo.Angle360(nr.center.TangentAt(0f))
                };
                nr.appB = new Approach
                {
                    node = nb, road = nr, atB = true, halfWidth = nr.halfWidth,
                    angle = Geo.Angle360(-nr.center.TangentAt(nr.length))
                };
                na.approaches.Add(nr.appA);
                nb.approaches.Add(nr.appB);
                roads.Add(nr);
                roadById[rd.id] = nr;
            }

            foreach (var n in nodes) n.approaches.Sort((x, y) => x.angle.CompareTo(y.angle));

            ApplySigns(map);

            foreach (var n in nodes) n.trim = ComputeNodeTrim(n);
            foreach (var r in roads)
            {
                float ta = r.appA.node.trim, tb = r.appB.node.trim;
                float avail = Mathf.Max(0.3f, r.length - 0.4f);
                if (ta + tb > avail)
                {
                    float k = avail / (ta + tb);
                    ta *= k; tb *= k;
                }
                r.trimA = ta; r.trimB = tb;
                r.appA.trim = ta; r.appB.trim = tb;
                SetEdges(r);
                BuildLanes(r);
            }

            foreach (var n in nodes)
            {
                if (n.roundabout) BuildRing(n);
                else BuildConnectors(n);
            }

            foreach (var n in nodes)
            {
                int ly = 0;
                foreach (var ap in n.approaches) if (ap.road != null && ap.road.data.layer > ly) ly = ap.road.data.layer;
                foreach (var c in n.connectors) c.layer = ly;
                foreach (var c in n.ringSegs) c.layer = ly;
            }

            BuildCrosswalks(map);
            ApplyStopLines(map);
            BuildPoints(map);

            foreach (var n in nodes)
            {
                if (n.entry && !entries.Contains(n)) entries.Add(n);
                if (n.exit && !exits.Contains(n)) exits.Add(n);
            }
        }

        // ---------------- Líneas de detención dibujadas a mano ----------------

        /// <summary>Una "Línea de detención" que cruza un carril cerca de su cruce fija dónde se paran los vehículos.</summary>
        void ApplyStopLines(MapData map)
        {
            foreach (var d in map.decor)
            {
                if (d.type != DecorType.StopLine) continue;
                Vector2 a = d.Pos, b = d.End;
                foreach (var nr in roads)
                {
                    for (int dir = 0; dir < 2; dir++)
                    {
                        var lanes = dir == 0 ? nr.lanesAB : nr.lanesBA;
                        foreach (var lane in lanes)
                        {
                            if (lane.toNode == null || !lane.toNode.IsJunction) continue;
                            float s1, s2;
                            var line = new Polyline(new[] { a, b });
                            if (!Geo.PolylineCross(lane.path, line, out s1, out s2)) continue;
                            if (lane.Length - s1 > 2.5f) continue;     // demasiado lejos del cruce: es solo pintura
                            lane.stopS = Mathf.Clamp(s1 - 0.04f, 0.2f, lane.Length);
                        }
                    }
                }
            }
        }

        // ---------------- Puntos lógicos (parada, peaje, detector) ----------------

        void BuildPoints(MapData map)
        {
            foreach (var pt in map.points)
            {
                NetRoad nr;
                var rd = map.Road(pt.road);
                if (rd == null || !roadById.TryGetValue(pt.road, out nr)) continue;
                Vector2 pos = map.PointOnRoad(rd, pt.t);
                for (int dirIdx = 0; dirIdx < 2; dirIdx++)
                {
                    if (pt.dir != 2 && pt.dir != dirIdx) continue;
                    var lanes = dirIdx == 0 ? nr.lanesAB : nr.lanesBA;
                    if (lanes.Length == 0) continue;
                    if (pt.kind == PointKind.BusStop)
                    {
                        // El autobús para en el carril más pegado a la acera (el de más a la derecha).
                        RoadSeg best = null; float bestSide = float.MaxValue, bestS = 0f;
                        foreach (var lane in lanes)
                        {
                            float d;
                            float s = lane.path.Project(pos, out d);
                            float side = Geo.Cross(lane.path.TangentAt(s), lane.path.PointAt(s) - pos);
                            if (side < bestSide) { bestSide = side; best = lane; bestS = s; }
                        }
                        AddStop(best, bestS, pt, false);
                    }
                    else
                    {
                        foreach (var lane in lanes)
                        {
                            float d;
                            AddStop(lane, lane.path.Project(pos, out d), pt, pt.kind == PointKind.Detector);
                        }
                    }
                }
            }
        }

        void AddStop(RoadSeg lane, float s, PointItem pt, bool detector)
        {
            if (lane == null) return;
            // Los detectores suelen ir justo antes del semáforo, al final del carril: antes se descartaban en silencio
            // (margen de 0,8) y nunca detectaban. Ahora solo se ajustan dentro del carril.
            if (detector) s = Mathf.Clamp(s, 0.05f, Mathf.Max(0.05f, lane.Length - 0.05f));
            else if (s < 0.8f || s > lane.Length - 0.8f) return;
            lane.stops.Add(new StopPoint { s = s, kind = pt.kind, seconds = pt.seconds, srcId = pt.id });
            if (detector && !detectorLanes.Contains(lane)) detectorLanes.Add(lane);
        }

        // ---------------- Señales ----------------

        void ApplySigns(MapData map)
        {
            foreach (var s in map.signs)
            {
                NetRoad nr;
                if (!roadById.TryGetValue(s.road, out nr)) continue;
                if (s.type == SignType.SpeedLimit) { nr.limited = true; continue; }
                Approach a = s.node == nr.data.a ? nr.appA : (s.node == nr.data.b ? nr.appB : null);
                if (a == null) continue;
                switch (s.type)
                {
                    case SignType.Stop:
                        if (s.style == 2)
                        {
                            // Stop controlado por el usuario: se comporta como un semáforo sin cabeza que el jugador abre y cierra.
                            a.hasLight = true; a.lightHidden = true; a.lightPhase = s.phase; a.lightSize = 1f;
                            break;
                        }
                        a.hasRule = true;
                        a.rule = s.type;
                        break;
                    case SignType.Yield:
                    case SignType.Priority:
                        a.hasRule = true;
                        a.rule = s.type;
                        break;
                    case SignType.Light: a.hasLight = true; a.lightModel = s.model; a.lightSide = s.side; a.lightPhase = s.phase; a.lightSize = s.size <= 0.01f ? 1f : s.size; a.lightHidden = s.hidden; break;
                    case SignType.NoLeft: a.banMask |= 1; break;
                    case SignType.NoStraight: a.banMask |= 2; break;
                    case SignType.NoRight: a.banMask |= 4; break;
                }
            }
        }

        // ---------------- Geometría de calles ----------------

        static float ComputeNodeTrim(NetNode n)
        {
            int m = n.approaches.Count;
            if (n.roundabout) return n.radius + 0.5f;
            if (m <= 1) return 0f;
            if (m == 2)
            {
                var a = n.approaches[0];
                var b = n.approaches[1];
                float turn = Mathf.Abs(Mathf.DeltaAngle(a.angle + 180f, b.angle));
                return 0.3f + Mathf.Clamp(turn / 90f, 0f, 1.5f) * Mathf.Max(a.halfWidth, b.halfWidth);
            }
            float t = 0.9f;
            for (int i = 0; i < m; i++)
            {
                var a = n.approaches[i];
                t = Mathf.Max(t, a.halfWidth + 0.35f);
                for (int j = 0; j < m; j++)
                {
                    if (i == j) continue;
                    var b = n.approaches[j];
                    float gamma = Mathf.Abs(Mathf.DeltaAngle(a.angle, b.angle));
                    if (gamma > 155f) continue;
                    t = Mathf.Max(t, b.halfWidth / Mathf.Sin(Mathf.Max(gamma, 25f) * Mathf.Deg2Rad) + 0.3f);
                }
            }
            return Mathf.Min(t, 3.2f);
        }

        static void SetEdges(NetRoad r)
        {
            SetEdge(r.appA, r.center.PointAt(r.trimA), r.center.TangentAt(r.trimA));
            SetEdge(r.appB, r.center.PointAt(r.length - r.trimB), -r.center.TangentAt(r.length - r.trimB));
        }

        static void SetEdge(Approach a, Vector2 p, Vector2 outward)
        {
            a.edgeDir = outward;
            a.edgeLeft = p + Geo.Left(outward) * a.halfWidth;
            a.edgeRight = p + Geo.Right(outward) * a.halfWidth;
        }

        RoadSeg NewSeg(SegKind kind, Polyline path)
        {
            var s = new RoadSeg { id = segs.Count, kind = kind, path = path };
            segs.Add(s);
            return s;
        }

        void BuildLanes(NetRoad nr)
        {
            var d = nr.data;
            Polyline sub = nr.center.Sub(nr.trimA, nr.length - nr.trimB);
            int n1 = d.lanesAB, n2 = d.lanesBA;
            float u0 = (n2 - n1) * 0.5f * LaneWidth;
            float cap = nr.limited ? 0.55f : 1f;
            nr.lanesAB = new RoadSeg[n1];
            nr.lanesBA = new RoadSeg[n2];

            for (int i = 0; i < n1; i++)
            {
                var path = new Polyline(sub.OffsetPts(u0 + d.MedianWidth * 0.5f + (i + 0.5f) * LaneWidth));
                var seg = NewSeg(SegKind.Lane, path);
                seg.fromNode = nr.appA.node; seg.toNode = nr.appB.node;
                seg.fromApp = nr.appA; seg.toApp = nr.appB;
                seg.roadId = d.id;
                seg.laneIndex = i; seg.speedCap = cap; seg.stopS = path.Length; seg.allow = d.GetLaneAllow(true, i); seg.layer = d.layer; seg.noChange = d.noLaneChange; seg.laneSet = nr.lanesAB;
                nr.lanesAB[i] = seg;
            }
            for (int j = 0; j < n2; j++)
            {
                var pts = sub.OffsetPts(u0 - d.MedianWidth * 0.5f - (j + 0.5f) * LaneWidth);
                System.Array.Reverse(pts);
                var path = new Polyline(pts);
                var seg = NewSeg(SegKind.Lane, path);
                seg.fromNode = nr.appB.node; seg.toNode = nr.appA.node;
                seg.fromApp = nr.appB; seg.toApp = nr.appA;
                seg.roadId = d.id;
                seg.laneIndex = j; seg.speedCap = cap; seg.stopS = path.Length; seg.allow = d.GetLaneAllow(false, j); seg.layer = d.layer; seg.noChange = d.noLaneChange; seg.laneSet = nr.lanesBA;
                nr.lanesBA[j] = seg;
            }

            nr.appA.inLanes.AddRange(nr.lanesBA);
            nr.appA.outLanes.AddRange(nr.lanesAB);
            nr.appB.inLanes.AddRange(nr.lanesAB);
            nr.appB.outLanes.AddRange(nr.lanesBA);
        }

        // ---------------- Cruces ----------------

        /// <summary>0 = recto, 1 = izquierda, -1 = derecha, 2 = cambio de sentido.</summary>
        static int Classify(Approach a, Approach b)
        {
            float delta = Mathf.DeltaAngle(a.angle + 180f, b.angle);
            float ad = Mathf.Abs(delta);
            if (ad <= 35f) return 0;
            if (ad > 150f) return 2;
            return delta > 0f ? 1 : -1;
        }

        static int MoveBit(int move)
        {
            return move == 1 ? 1 : (move == 0 ? 2 : 4);
        }

        static Polyline BezierPath(Vector2 p0, Vector2 t0, Vector2 p3, Vector2 t1)
        {
            float d = Vector2.Distance(p0, p3);
            if (d < 0.04f) return new Polyline(new[] { p0, p0 + t0.normalized * 0.05f });
            float h = d * 0.4f;
            Vector2 p1 = p0 + t0.normalized * h;
            Vector2 p2 = p3 - t1.normalized * h;
            int steps = Mathf.Clamp(Mathf.CeilToInt(d / 0.12f), 6, 24);
            var pts = new Vector2[steps + 1];
            for (int i = 0; i <= steps; i++) pts[i] = Geo.Cubic(p0, p1, p2, p3, i / (float)steps);
            return new Polyline(pts);
        }

        RoadSeg MakeConnector(NetNode n, RoadSeg inLane, RoadSeg outLane, int move)
        {
            var path = BezierPath(inLane.path.End, inLane.path.TangentAt(inLane.Length), outLane.path.Start, outLane.path.TangentAt(0f));
            var c = NewSeg(SegKind.Connector, path);
            c.fromNode = n; c.toNode = n;
            c.inApp = inLane.toApp; c.outApp = outLane.fromApp;
            c.inLane = inLane;
            c.move = move;
            c.speedCap = move == 0 ? 1f : (move == 1 ? 0.6f : 0.5f);
            inLane.outs.Add(c); c.ins.Add(inLane);
            c.outs.Add(outLane); outLane.ins.Add(c);
            n.connectors.Add(c);
            inLane.moves |= MoveBit(move);
            return c;
        }

        static int AutoMask(int lane, int count, int avail)
        {
            int m;
            if (count <= 1) m = 7;
            else if (count == 2) m = lane == 0 ? 3 : 6;
            else m = lane == 0 ? 1 : (lane == count - 1 ? 6 : 2);
            m &= avail;
            return m != 0 ? m : avail;
        }

        static int TargetLane(int i, int n, int k, int move)
        {
            if (move == -1) return Mathf.Clamp(k - 1 - Mathf.Min(n - 1 - i, k - 1), 0, k - 1);
            return Mathf.Min(i, k - 1);
        }

        void BuildConnectors(NetNode n)
        {
            var targets = new List<Approach>();
            var moves = new List<int>();
            foreach (var a in n.approaches)
            {
                if (a.inLanes.Count == 0) continue;
                targets.Clear(); moves.Clear();
                int avail = 0;
                foreach (var b in n.approaches)
                {
                    if (b == a || b.outLanes.Count == 0) continue;
                    int mv = Classify(a, b);
                    if (mv == 2 || !a.CanTurn(mv)) continue;
                    targets.Add(b); moves.Add(mv);
                    avail |= MoveBit(mv);
                }
                if (targets.Count == 0) continue;

                for (int i = 0; i < a.inLanes.Count; i++)
                {
                    int user = srcMap != null ? srcMap.LaneArrowMoves(a.road.data.id, a.atB, i) : 0;
                    if (user == 0) user = a.road.data.GetMask(a.atB, i) & 7;
                    int mask = user != 0 ? user : AutoMask(i, a.inLanes.Count, avail);
                    for (int t = 0; t < targets.Count; t++)
                    {
                        if ((mask & MoveBit(moves[t])) == 0) continue;
                        var b = targets[t];
                        int j = TargetLane(i, a.inLanes.Count, b.outLanes.Count, moves[t]);
                        MakeConnector(n, a.inLanes[i], b.outLanes[j], moves[t]);
                    }
                }
                // Nodo de paso (2 calles) con más carriles a la salida: el carril exterior se abre en los nuevos (carril que empieza).
                if (n.approaches.Count == 2 && targets.Count == 1 && moves[0] == 0)
                {
                    var b2 = targets[0];
                    int nIn = a.inLanes.Count;
                    for (int j = nIn; j < b2.outLanes.Count; j++) MakeConnector(n, a.inLanes[nIn - 1], b2.outLanes[j], 0);
                }
            }
            ComputeConflicts(n);
        }

        void ComputeConflicts(NetNode n)
        {
            var list = n.connectors;
            for (int i = 0; i < list.Count; i++)
            {
                for (int j = i + 1; j < list.Count; j++)
                {
                    var c1 = list[i];
                    var c2 = list[j];
                    if (c1.inApp == c2.inApp || c1.inLane == c2.inLane) continue;
                    if (c1.outs[0] == c2.outs[0])
                    {
                        c1.conflicts.Add(new Conflict { other = c2, myS = c1.Length, otherS = c2.Length, merge = true });
                        c2.conflicts.Add(new Conflict { other = c1, myS = c2.Length, otherS = c1.Length, merge = true });
                        continue;
                    }
                    float s1, s2;
                    if (Geo.PolylineCross(c1.path, c2.path, out s1, out s2))
                    {
                        c1.conflicts.Add(new Conflict { other = c2, myS = s1, otherS = s2 });
                        c2.conflicts.Add(new Conflict { other = c1, myS = s2, otherS = s1 });
                    }
                }
            }
        }

        // ---------------- Rotondas ----------------

        static float Ccw(float deg)
        {
            float d = deg % 360f;
            if (d < 0f) d += 360f;
            return d;
        }

        RoadSeg ArcSeg(NetNode n, float startDeg, float sweepDeg)
        {
            int steps = Mathf.Max(2, Mathf.CeilToInt(sweepDeg / 6f));
            var pts = new Vector2[steps + 1];
            for (int i = 0; i <= steps; i++)
                pts[i] = n.pos + Geo.FromAngle(startDeg + sweepDeg * i / steps) * n.radius;
            var s = NewSeg(SegKind.Ring, new Polyline(pts));
            s.fromNode = n; s.toNode = n;
            s.speedCap = 0.55f;
            s.stopS = s.Length;
            n.ringSegs.Add(s);
            return s;
        }

        static Vector2 AvgEnd(List<RoadSeg> lanes)
        {
            Vector2 sum = Vector2.zero;
            for (int i = 0; i < lanes.Count; i++) sum += lanes[i].path.End;
            return sum / lanes.Count;
        }

        static Vector2 AvgStart(List<RoadSeg> lanes)
        {
            Vector2 sum = Vector2.zero;
            for (int i = 0; i < lanes.Count; i++) sum += lanes[i].path.Start;
            return sum / lanes.Count;
        }

        void Link(RoadSeg from, RoadSeg to)
        {
            if (!from.outs.Contains(to)) from.outs.Add(to);
            if (!to.ins.Contains(from)) to.ins.Add(from);
        }

        RoadSeg RingConnector(NetNode n, RoadSeg from, RoadSeg to, Approach inApp, Approach outApp, RoadSeg inLane, int move)
        {
            var path = BezierPath(from.path.End, from.path.TangentAt(from.Length), to.path.Start, to.path.TangentAt(0f));
            var c = NewSeg(SegKind.Connector, path);
            c.fromNode = n; c.toNode = n;
            c.inApp = inApp; c.outApp = outApp; c.inLane = inLane;
            c.move = move;
            c.speedCap = 0.6f;
            Link(from, c);
            Link(c, to);
            n.connectors.Add(c);
            return c;
        }

        void BuildRing(NetNode n)
        {
            var apps = n.approaches;
            int m = apps.Count;
            if (m == 0) return;

            for (int k = 0; k < m; k++)
            {
                var a = apps[k];
                a.ringIn = a.inLanes.Count > 0 ? Geo.Angle360(AvgEnd(a.inLanes) - n.pos) : Ccw(a.angle + 14f);
                a.ringOut = a.outLanes.Count > 0 ? Geo.Angle360(AvgStart(a.outLanes) - n.pos) : Ccw(a.angle - 14f);
            }

            var ring = new RoadSeg[m];
            var mouth = new RoadSeg[m];
            for (int k = 0; k < m; k++)
            {
                float from = apps[k].ringIn;
                float to = apps[(k + 1) % m].ringOut;
                float sweep = Ccw(to - from);
                if (m > 1 && sweep > 300f) sweep = 6f;
                if (sweep < 4f) sweep = 4f;
                ring[k] = ArcSeg(n, from, sweep);

                float mSweep = Ccw(apps[k].ringIn - apps[k].ringOut);
                if (mSweep > 120f) mSweep = 8f;
                if (mSweep < 1.5f) mSweep = 1.5f;
                mouth[k] = ArcSeg(n, apps[k].ringOut, mSweep);
            }

            for (int k = 0; k < m; k++)
            {
                var prev = ring[(k - 1 + m) % m];
                Link(prev, mouth[k]);
                Link(mouth[k], ring[k]);
            }

            for (int k = 0; k < m; k++)
            {
                var a = apps[k];
                var prev = ring[(k - 1 + m) % m];
                for (int j = 0; j < a.outLanes.Count; j++)
                {
                    var c = RingConnector(n, prev, a.outLanes[j], null, a, null, 0);
                    c.priority = 40;
                }
                for (int i = 0; i < a.inLanes.Count; i++)
                {
                    var il = a.inLanes[i];
                    var c = RingConnector(n, il, ring[k], a, null, il, -1);
                    c.ringEntry = true;
                    c.mouth = mouth[k];
                    c.priority = 12;
                    il.moves = 2;
                }
            }
        }

        // ---------------- Pasos de peatones ----------------

        void BuildCrosswalks(MapData map)
        {
            // Automáticos (opcionales): en cada brazo de un cruce con espacio suficiente.
            foreach (var n in nodes)
            {
                if (!map.autoCrosswalks) break;
                if (!n.IsJunction || n.roundabout) continue;
                foreach (var a in n.approaches)
                {
                    var r = a.road;
                    if (r.length - r.trimA - r.trimB < 1.9f) continue;
                    float sAlong = a.atB ? r.length - a.trim - 0.45f : a.trim + 0.45f;
                    var cw = NewCrosswalk(r, sAlong);
                    a.hasCrosswalk = true;
                    if (a.hasLight) cw.lightApp = a;
                    foreach (var il in a.inLanes)
                    {
                        float proj = ProjectOn(il, cw.center);
                        il.stopS = Mathf.Max(0.2f, proj - Crosswalk.ZoneHalf - 0.12f);
                        AddEntry(cw, il, il.stopS);
                    }
                    foreach (var ol in a.outLanes)
                        AddEntry(cw, ol, ProjectOn(ol, cw.center) - Crosswalk.ZoneHalf - 0.04f);
                }
            }

            // Manuales: a mitad de calle.
            foreach (var item in map.crosswalks)
            {
                NetRoad r;
                if (!roadById.TryGetValue(item.road, out r)) continue;
                float sAlong = Mathf.Clamp(item.t * r.length, r.trimA + 0.4f, r.length - r.trimB - 0.4f);
                if (r.length - r.trimA - r.trimB < 0.9f) continue;
                var cw = NewCrosswalk(r, sAlong);
                foreach (var l in r.lanesAB) AddEntry(cw, l, ProjectOn(l, cw.center) - Crosswalk.ZoneHalf - 0.04f);
                foreach (var l in r.lanesBA) AddEntry(cw, l, ProjectOn(l, cw.center) - Crosswalk.ZoneHalf - 0.04f);
                // Paso colocado a mano junto a un cruce: la línea de parada se adelanta y, si hay semáforo, lo regula.
                foreach (var app in new[] { r.appA, r.appB })
                {
                    if (!app.node.IsJunction || app.node.roundabout) continue;
                    float dEnd = app.atB ? r.length - r.trimB - sAlong : sAlong - r.trimA;
                    if (dEnd > 1.6f) continue;
                    app.hasCrosswalk = true;
                    if (app.hasLight) cw.lightApp = app;
                    foreach (var il in app.inLanes)
                    {
                        float proj = ProjectOn(il, cw.center);
                        il.stopS = Mathf.Min(il.stopS, Mathf.Max(0.2f, proj - Crosswalk.ZoneHalf - 0.12f));
                    }
                }
            }
        }

        Crosswalk NewCrosswalk(NetRoad r, float sAlong)
        {
            Vector2 axis = r.center.TangentAt(sAlong);
            var cw = new Crosswalk
            {
                roadId = r.data.id,
                center = r.center.PointAt(sAlong),
                axis = axis,
                across = Geo.Right(axis),
                halfWidth = r.halfWidth
            };
            crosswalks.Add(cw);
            return cw;
        }

        static float ProjectOn(RoadSeg lane, Vector2 p)
        {
            float d;
            return lane.path.Project(p, out d);
        }

        static void AddEntry(Crosswalk cw, RoadSeg lane, float limit)
        {
            var entry = new CwEntry { cw = cw, limitFrontS = limit };
            cw.entries.Add(entry);
            lane.crosswalks.Add(entry);
        }

        // ---------------- Consultas ----------------

        public NetNode GetNode(int id)
        {
            NetNode n;
            nodeById.TryGetValue(id, out n);
            return n;
        }

        /// <summary>Carriles por los que un vehículo puede entrar al mapa desde el nodo.</summary>
        public List<RoadSeg> SpawnLanes(NetNode n)
        {
            var list = new List<RoadSeg>();
            foreach (var a in n.approaches)
                foreach (var l in a.outLanes)
                    if (l.laneType != RoadData.LaneSidewalk || n.walkEnd || !LaneMode) list.Add(l);   // peatones: solo desde el extremo de una acera
            return list;
        }
    }
}
