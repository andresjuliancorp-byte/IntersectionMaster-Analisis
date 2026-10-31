using System.Collections.Generic;
using UnityEngine;
using IntersectionMaster.Data;

namespace IntersectionMaster.Traffic
{
    /// <summary>
    /// Rutas por el grafo de carriles (Dijkstra). Un recorrido desde un carril de entrada calcula las distancias a todos
    /// los carriles; después se elige una salida alcanzable al azar. Los costes llevan un pequeño ruido para que
    /// rutas casi iguales varíen entre vehículos.
    /// </summary>
    public class PathfindingSystem
    {
        readonly Dictionary<RoadSeg, float> dist = new Dictionary<RoadSeg, float>();
        readonly Dictionary<RoadSeg, RoadSeg> prev = new Dictionary<RoadSeg, RoadSeg>();
        readonly List<KeyValuePair<float, RoadSeg>> heap = new List<KeyValuePair<float, RoadSeg>>();
        readonly List<NetNode> reachable = new List<NetNode>();

        void Push(float k, RoadSeg s)
        {
            heap.Add(new KeyValuePair<float, RoadSeg>(k, s));
            int i = heap.Count - 1;
            while (i > 0)
            {
                int p = (i - 1) / 2;
                if (heap[p].Key <= heap[i].Key) break;
                var t = heap[p]; heap[p] = heap[i]; heap[i] = t;
                i = p;
            }
        }

        KeyValuePair<float, RoadSeg> Pop()
        {
            var top = heap[0];
            int last = heap.Count - 1;
            heap[0] = heap[last];
            heap.RemoveAt(last);
            int i = 0;
            int n = heap.Count;
            while (true)
            {
                int l = i * 2 + 1, r = l + 1, m = i;
                if (l < n && heap[l].Key < heap[m].Key) m = l;
                if (r < n && heap[r].Key < heap[m].Key) m = r;
                if (m == i) break;
                var t = heap[m]; heap[m] = heap[i]; heap[i] = t;
                i = m;
            }
            return top;
        }

        static float Cost(RoadSeg s)
        {
            float c = s.Length / Mathf.Max(0.2f, s.speedCap);
            if (s.kind == SegKind.Connector) c += 0.25f;
            if (s.avoid) c += 500f;   // «Evitar»: solo si no queda otro camino
            return c * Random.Range(0.9f, 1.2f);
        }

        int typeBit;   // 0 = cualquier tipo
        /// <summary>Bit del tranvía: solo va por carriles con tranvía permitido.</summary>
        public const int TramBit = 1 << (int)VehicleType.Tram;

        void Run(RoadSeg start)
        {
            dist.Clear();
            prev.Clear();
            heap.Clear();
            dist[start] = 0f;
            prev[start] = null;
            Push(0f, start);
            while (heap.Count > 0)
            {
                var top = Pop();
                RoadSeg cur = top.Value;
                float dcur;
                if (!dist.TryGetValue(cur, out dcur) || top.Key > dcur + 1e-4f) continue;
                for (int i = 0; i < cur.outs.Count; i++)
                {
                    var o = cur.outs[i];
                    if (typeBit == TramBit) { if (!o.tram) continue; }
                    else if (typeBit != 0 && o.allow != 0 && (o.allow & typeBit) == 0) continue;
                    float nd = dcur + Cost(o);
                    float old;
                    if (dist.TryGetValue(o, out old) && old <= nd) continue;
                    dist[o] = nd;
                    prev[o] = cur;
                    Push(nd, o);
                }
            }
        }

        /// <summary>Ruta desde el carril de entrada hasta una salida alcanzable al azar (distinta del nodo de entrada). Null si no hay.</summary>
        public List<RoadSeg> FindRoute(RoadSeg start, List<NetNode> exits, int vehicleTypeBit = 0)
        {
            var fromData = start != null && start.fromNode != null ? start.fromNode.data : null;
            return FindRoute(start, exits, vehicleTypeBit, fromData != null && fromData.dest != null && fromData.dest.Count > 0 ? fromData.dest : null);
        }

        /// <summary>Como FindRoute, pero con la probabilidad de destino del nodo de inicio (dest), si la tiene.</summary>
        public List<RoadSeg> FindRoute(RoadSeg start, List<NetNode> exits, int vehicleTypeBit, List<DestWeight> dest)
        {
            if (start == null) return null;
            typeBit = vehicleTypeBit;
            if (typeBit == TramBit ? !start.tram : (typeBit != 0 && start.allow != 0 && (start.allow & typeBit) == 0)) { typeBit = 0; return null; }
            Run(start);
            typeBit = 0;
            reachable.Clear();
            for (int i = 0; i < exits.Count; i++)
            {
                var e = exits[i];
                if (e == start.fromNode) continue;
                // Los peatones solo terminan donde termina una acera (no a mitad de camino en una salida de coches).
                if (start.laneType == RoadData.LaneSidewalk && !e.walkEnd) continue;
                if (BestLaneTo(e) != null) reachable.Add(e);
            }
            if (reachable.Count == 0) return null;
            float total = 0f;
            for (int i = 0; i < reachable.Count; i++) total += DestW(reachable[i], dest);
            NetNode pick = null;
            if (total > 0.001f)
            {
                float r = Random.value * total;
                for (int i = 0; i < reachable.Count; i++)
                {
                    float w = DestW(reachable[i], dest);
                    r -= w;
                    if (r <= 0f && w > 0f) { pick = reachable[i]; break; }
                }
                if (pick == null) pick = reachable[reachable.Count - 1];
            }
            else pick = reachable[Random.Range(0, reachable.Count)];
            return Reconstruct(BestLaneTo(pick));
        }

        /// <summary>Peso de un destino: el del nodo de inicio si lo fijó el jugador; si no, el de la Tabla de tráfico.</summary>
        static float DestW(NetNode exit, List<DestWeight> dest)
        {
            if (dest == null) return exit.weight;
            for (int i = 0; i < dest.Count; i++) if (dest[i].node == exit.id) return Mathf.Max(0f, dest[i].weight);
            return 50f;   // destino no tocado: el valor inicial
        }

        /// <summary>Ruta desde start hasta el nodo de salida dado; devuelve el coste o -1. La ruta incluye start.</summary>
        public float RouteTo(RoadSeg start, NetNode exit, int vehicleTypeBit, List<RoadSeg> outRoute)
        {
            if (start == null || exit == null) return -1f;
            typeBit = vehicleTypeBit;
            Run(start);
            typeBit = 0;
            RoadSeg goal = null;
            float bd = float.MaxValue;
            foreach (var a in exit.approaches)
                for (int i = 0; i < a.inLanes.Count; i++)
                {
                    float d;
                    if (dist.TryGetValue(a.inLanes[i], out d) && d < bd) { bd = d; goal = a.inLanes[i]; }
                }
            if (goal == null) return -1f;
            outRoute.Clear();
            outRoute.AddRange(Reconstruct(goal));
            return bd;
        }

        /// <summary>true si desde el carril de entrada se puede llegar al nodo de salida.</summary>
        public bool CanReach(RoadSeg start, NetNode exit)
        {
            Run(start);
            return BestLaneTo(exit) != null;
        }

        /// <summary>Tras Run: ¿se puede llegar a este nodo de salida? (usa las distancias del último recorrido)</summary>
        public bool LastRunReaches(NetNode exit)
        {
            return BestLaneTo(exit) != null;
        }

        public void Prepare(RoadSeg start)
        {
            Run(start);
        }

        /// <summary>
        /// Tras Run/Prepare: el extremo de carril más lejano alcanzado donde la ruta se corta (carril sin ninguna continuación
        /// y que no llega a una salida). Sirve para decirle al jugador dónde se interrumpe una ruta. False si no hay ninguno.
        /// </summary>
        public bool LastRunDeadEnd(out Vector2 pos)
        {
            pos = Vector2.zero;
            RoadSeg best = null;
            float bd = -1f;
            foreach (var kv in dist)
            {
                var s = kv.Key;
                if (s.kind != SegKind.Lane || s.toNode == null || s.toNode.exit) continue;
                if (s.outs.Count > 0) continue;
                if (kv.Value > bd) { bd = kv.Value; best = s; }
            }
            if (best == null) return false;
            pos = best.toNode.pos;
            return true;
        }

        RoadSeg BestLaneTo(NetNode exit)
        {
            RoadSeg best = null;
            float bd = float.MaxValue;
            foreach (var a in exit.approaches)
            {
                for (int i = 0; i < a.inLanes.Count; i++)
                {
                    float d;
                    if (dist.TryGetValue(a.inLanes[i], out d) && d < bd) { bd = d; best = a.inLanes[i]; }
                }
            }
            return best;
        }

        List<RoadSeg> Reconstruct(RoadSeg last)
        {
            var route = new List<RoadSeg>();
            for (var s = last; s != null; s = prev[s]) route.Add(s);
            route.Reverse();
            return route;
        }
    }
}
