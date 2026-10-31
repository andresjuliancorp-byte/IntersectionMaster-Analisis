using System.Collections.Generic;
using UnityEngine;
using IntersectionMaster.Data;

namespace IntersectionMaster.Traffic
{
    /// <summary>
    /// Semáforos del modelo por carriles. Cada nodo con semáforo regula los carriles que llegan a él. Los semáforos
    /// cercanos forman un grupo (un "cruce") con su propio ciclo: dos semáforos se estorban si los carriles que salen
    /// de ellos se cruzan o se juntan poco después; los que no se estorban comparten fase.
    /// </summary>
    public partial class IntersectionController
    {
        const float ReachDistance = 6f;
        const float GroupDistance = 7f;

        bool laneMode;
        readonly Dictionary<Approach, Dictionary<RoadSeg, float>> reach = new Dictionary<Approach, Dictionary<RoadSeg, float>>();
        readonly Dictionary<Approach, float> depth = new Dictionary<Approach, float>();

        /// <summary>Grupo de semáforos del modelo por carriles. node es un nodo virtual cuyos accesos son los nodos con semáforo.</summary>
        public IntersectionController(NetNode groupNode, TrafficSettings settings, bool lanes)
        {
            node = groupNode;
            laneMode = true;
            mode = ControlMode.Lights;
            node.controller = this;
            foreach (var a in node.approaches) { a.node.controller = this; reach[a] = Reach(a); depth[a] = 0.5f; }

            var list = node.approaches;
            for (int i = 0; i < list.Count; i++)
                for (int j = i + 1; j < list.Count; j++)
                {
                    float da, db;
                    if (!Clashes(list[i], list[j], out da, out db)) continue;
                    AddClash(list[i], list[j]);
                    AddClash(list[j], list[i]);
                    depth[list[i]] = Mathf.Max(depth[list[i]], da);
                    depth[list[j]] = Mathf.Max(depth[list[j]], db);
                }

            var phases = LanePhases(list);
            if (phases.Count == 0) phases = GreedyPhases(list);
            light.phases.AddRange(phases);
            light.autoCycle = node.ctrl == 0 ? settings.autoCycle : node.ctrl == 2;
            light.greenSeconds = node.green > 0.1f ? node.green : settings.greenSeconds;
            if (node.yellow > 0.1f) light.yellowSeconds = node.yellow;
            if (node.data != null && node.data.allRed > 0.05f) light.allRedSeconds = node.data.allRed;
            light.sensor = node.data != null && node.data.sensor;
            light.hasDemand = HasDemand;
            light.safeMode = settings.safeSignals;
            light.freeToggle = true;   // como el original: el jugador decide qué semáforos están en verde
            light.conflicts = (x, y) => { HashSet<Approach> set; return clash.TryGetValue(x, out set) && set.Contains(y); };
            light.isClear = IsClearFor;
            // Semáforos peatonales: los dos lados del mismo cruce van juntos (al tocar uno cambian los dos, y en el ciclo
            // automático también), salvo que el jugador les haya puesto una fase.
            var pedPair = PedPairs(list);
            light.groupOf = x => { if (x.lightPhase > 0) return x.lightPhase; int g; return pedPair.TryGetValue(x, out g) ? g : 0; };
            foreach (var a in list) light.Register(a);
            light.InitStart();
        }

        /// <summary>¿Hay alguien esperando o llegando a este semáforo? (para el «Sensor» del controlador)</summary>
        static bool HasDemand(Approach a)
        {
            foreach (var l in a.inLanes)
                foreach (var v in l.vehicles)
                    if (!v.arrived && l.stopS - v.s < 4f) return true;
            return false;
        }

        /// <summary>¿Es un semáforo peatonal? (todos los carriles que llegan son de acera).</summary>
        static bool IsPed(Approach a)
        {
            if (a.inLanes.Count == 0) return false;
            foreach (var l in a.inLanes) if (l.laneType != RoadData.LaneSidewalk) return false;
            return true;
        }

        /// <summary>Parejas de semáforos peatonales (los dos bordes de un cruce): número de grupo propio (1000 + …).</summary>
        static Dictionary<Approach, int> PedPairs(List<Approach> list)
        {
            var res = new Dictionary<Approach, int>();
            foreach (var a in list)
            {
                if (!IsPed(a) || res.ContainsKey(a)) continue;
                foreach (var o in a.outLanes)
                {
                    if (o.laneType != RoadData.LaneSidewalk || o.toApp == null) continue;
                    bool crosses = false;
                    foreach (var c in o.conflicts) if (c.other != null && c.other.laneType != RoadData.LaneSidewalk) { crosses = true; break; }
                    if (!crosses) continue;
                    var b = o.toApp;
                    if (b == a || !list.Contains(b) || !IsPed(b) || res.ContainsKey(b)) continue;
                    int g = 1000 + Mathf.Min(a.node.id, b.node.id) % 100000;
                    res[a] = g; res[b] = g;
                    break;
                }
            }
            return res;
        }

        /// <summary>Crea los grupos de semáforos de una red por carriles.</summary>
        public static List<IntersectionController> BuildLaneGroups(RoadNetworkManager net, TrafficSettings settings)
        {
            var result = new List<IntersectionController>();
            var lit = new List<Approach>();
            foreach (var n in net.nodes)
            {
                var hub = net.HubOf(n.id);
                if (hub != null && hub.hasLight && hub.inLanes.Count > 0) lit.Add(hub);
            }
            int count = lit.Count;
            var parent = new int[count];
            for (int i = 0; i < count; i++) parent[i] = i;
            System.Func<int, int> find = null;
            find = x => { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; };
            for (int i = 0; i < count; i++)
                for (int j = i + 1; j < count; j++)
                    if (Vector2.Distance(lit[i].node.pos, lit[j].node.pos) < GroupDistance) parent[find(i)] = find(j);

            var groups = new Dictionary<int, List<Approach>>();
            for (int i = 0; i < count; i++)
            {
                int r = find(i);
                List<Approach> g;
                if (!groups.TryGetValue(r, out g)) { g = new List<Approach>(); groups[r] = g; }
                g.Add(lit[i]);
            }
            int k = 0;
            foreach (var g in groups.Values)
            {
                Vector2 c = Vector2.zero;
                foreach (var a in g) c += a.node.pos;
                c /= g.Count;
                var first = g[0].node;
                var vn = new NetNode { id = -100000 - (k++), pos = c, ctrl = first.ctrl, green = first.green, yellow = first.yellow };
                vn.approaches.AddRange(g);
                result.Add(new IntersectionController(vn, settings, true));
            }
            return result;
        }

        /// <summary>Carriles alcanzables desde el semáforo (hasta cierta distancia) con la distancia a la que empiezan.</summary>
        static Dictionary<RoadSeg, float> Reach(Approach a)
        {
            var dist = new Dictionary<RoadSeg, float>();
            var queue = new List<RoadSeg>();
            foreach (var ol in a.outLanes) { dist[ol] = 0f; queue.Add(ol); }
            // También los carriles que llegan al semáforo pasan por el nodo: el primer tramo de salida empieza en 0.
            for (int qi = 0; qi < queue.Count; qi++)
            {
                var sg = queue[qi];
                float d0 = dist[sg];
                float d1 = d0 + sg.Length;
                if (d1 >= ReachDistance) continue;
                foreach (var nx in sg.outs)
                {
                    float old;
                    if (dist.TryGetValue(nx, out old) && old <= d1) continue;
                    if (nx.toApp != null && nx.fromApp != null && nx.fromApp.hasLight) continue;   // otro semáforo: ahí manda el suyo
                    dist[nx] = d1;
                    queue.Add(nx);
                }
            }
            return dist;
        }

        /// <summary>¿Se estorban dos semáforos? da/db: hasta qué distancia tras cada semáforo llega la zona compartida.</summary>
        bool Clashes(Approach a, Approach b, out float da, out float db)
        {
            da = db = 0f;
            var ra = reach[a];
            var rb = reach[b];
            bool any = false;
            foreach (var kv in ra)
            {
                float ob;
                if (rb.TryGetValue(kv.Key, out ob))
                {
                    any = true;
                    da = Mathf.Max(da, kv.Value + 0.5f);
                    db = Mathf.Max(db, ob + 0.5f);
                }
                foreach (var c in kv.Key.conflicts)
                {
                    float oo;
                    if (!rb.TryGetValue(c.other, out oo)) continue;
                    if (kv.Value + c.myS > ReachDistance || oo + c.otherS > ReachDistance) continue;
                    any = true;
                    da = Mathf.Max(da, kv.Value + c.myS + c.clear);
                    db = Mathf.Max(db, oo + c.otherS + c.otherClear);
                }
            }
            return any;
        }

        static List<SignalPhase> LanePhases(List<Approach> list)
        {
            var result = new List<SignalPhase>();
            bool user = false;
            foreach (var a in list) if (a.lightPhase > 0) user = true;
            if (user)
            {
                var byNum = new SortedDictionary<int, SignalPhase>();
                int solo = 100;
                foreach (var a in list)
                {
                    int key = a.lightPhase > 0 ? a.lightPhase : solo++;
                    SignalPhase ph;
                    if (!byNum.TryGetValue(key, out ph)) { ph = new SignalPhase { label = key < 100 ? "Fase " + key : "Semáforo" }; byNum[key] = ph; }
                    ph.approaches.Add(a);
                }
                foreach (var kv in byNum) result.Add(kv.Value);
                return result;
            }
            return result;
        }

        /// <summary>Fases automáticas: cada semáforo entra en la primera fase con la que no se estorba.</summary>
        List<SignalPhase> GreedyPhases(List<Approach> list)
        {
            var result = new List<SignalPhase>();
            var sorted = new List<Approach>(list);
            sorted.Sort((x, y) => InAngle(x).CompareTo(InAngle(y)));
            foreach (var a in sorted)
            {
                SignalPhase target = null;
                foreach (var ph in result)
                {
                    bool ok = true;
                    foreach (var m in ph.approaches) if (ClashOf(a, m)) { ok = false; break; }
                    if (ok) { target = ph; break; }
                }
                if (target == null) { target = new SignalPhase { label = "Fase " + (result.Count + 1) }; result.Add(target); }
                target.approaches.Add(a);
            }
            return result.Count >= 2 ? result : new List<SignalPhase>();
        }

        bool ClashOf(Approach a, Approach b)
        {
            HashSet<Approach> set;
            return clash.TryGetValue(a, out set) && set.Contains(b);
        }

        static float InAngle(Approach a)
        {
            if (a.inLanes.Count == 0) return 0f;
            var l = a.inLanes[0];
            float ang = Mathf.Atan2(l.path.TangentAt(l.Length).y, l.path.TangentAt(l.Length).x) * Mathf.Rad2Deg;
            ang = ang % 180f;
            if (ang < 0f) ang += 180f;
            return ang;
        }

        bool LaneClearFor(Approach a)
        {
            HashSet<Approach> set;
            if (!clash.TryGetValue(a, out set)) return true;
            foreach (var b in set)
            {
                Dictionary<RoadSeg, float> rb;
                if (!reach.TryGetValue(b, out rb)) continue;
                float lim = depth.ContainsKey(b) ? depth[b] : 1f;
                foreach (var kv in rb)
                {
                    if (kv.Value > lim) continue;
                    var vs = kv.Key.vehicles;
                    for (int i = 0; i < vs.Count; i++)
                    {
                        var w = vs[i];
                        if (w.crashed) continue;
                        if (kv.Value + w.s - w.HalfLength < lim) return false;
                    }
                }
            }
            return true;
        }
    }
}
