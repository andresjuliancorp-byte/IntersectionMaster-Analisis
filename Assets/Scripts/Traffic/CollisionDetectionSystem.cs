using System.Collections.Generic;
using UnityEngine;

namespace IntersectionMaster.Traffic
{
    /// <summary>
    /// Detección de colisiones con cuadrícula espacial para no comparar todos con todos. Cada vehículo se aproxima con dos
    /// círculos (delantero y trasero). Se ignoran los vehículos del mismo segmento y los de segmentos emparentados
    /// (consecutivos, que divergen o que confluyen): su separación la gestiona el seguimiento del líder.
    /// </summary>
    public class CollisionDetectionSystem
    {
        const float Cell = 0.6f;

        readonly Dictionary<long, List<VehicleController>> grid = new Dictionary<long, List<VehicleController>>();
        readonly Stack<List<VehicleController>> pool = new Stack<List<VehicleController>>();

        static long Key(int x, int y)
        {
            return ((long)x << 32) ^ (uint)y;
        }

        static bool Related(RoadSeg a, RoadSeg b)
        {
            if (a == b) return true;
            if (a.outs.Contains(b) || b.outs.Contains(a)) return true;
            for (int i = 0; i < a.outs.Count; i++) if (b.outs.Contains(a.outs[i])) return true;
            for (int i = 0; i < a.ins.Count; i++) if (b.ins.Contains(a.ins[i])) return true;
            return false;
        }

        static bool Overlap(VehicleController a, VehicleController b)
        {
            float off1 = a.Length * 0.25f, off2 = b.Length * 0.25f;
            float r = a.Radius + b.Radius;
            float r2 = r * r;
            for (int i = -1; i <= 1; i += 2)
            {
                Vector2 pa = a.Pos + a.Dir * (off1 * i);
                for (int j = -1; j <= 1; j += 2)
                {
                    Vector2 pb = b.Pos + b.Dir * (off2 * j);
                    if ((pa - pb).sqrMagnitude < r2) return true;
                }
            }
            return false;
        }

        public void Detect(List<VehicleController> vehicles, List<KeyValuePair<VehicleController, VehicleController>> hits)
        {
            hits.Clear();
            foreach (var l in grid.Values) { l.Clear(); pool.Push(l); }
            grid.Clear();

            for (int i = 0; i < vehicles.Count; i++)
            {
                var v = vehicles[i];
                var p = v.Pos;
                long k = Key(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.y / Cell));
                List<VehicleController> cell;
                if (!grid.TryGetValue(k, out cell))
                {
                    cell = pool.Count > 0 ? pool.Pop() : new List<VehicleController>(4);
                    grid[k] = cell;
                }
                cell.Add(v);
            }

            for (int i = 0; i < vehicles.Count; i++)
            {
                var v = vehicles[i];
                var p = v.Pos;
                int cx = Mathf.FloorToInt(p.x / Cell), cy = Mathf.FloorToInt(p.y / Cell);
                for (int dx = -2; dx <= 2; dx++)
                {
                    for (int dy = -2; dy <= 2; dy++)
                    {
                        List<VehicleController> cell;
                        if (!grid.TryGetValue(Key(cx + dx, cy + dy), out cell)) continue;
                        for (int j = 0; j < cell.Count; j++)
                        {
                            var o = cell[j];
                            if (o.id <= v.id) continue;
                            if (v.crashed && o.crashed) continue;
                            if (v.Layer != o.Layer) continue;   // puentes: capas distintas no chocan
                            if (v.agent == 1 && o.agent == 1) continue;  // los peatones se esquivan entre sí
                            if ((v.agent == 4) != (o.agent == 4)) continue;   // los barcos van por el agua
                            if (Related(v.Seg, o.Seg)) continue;
                            if (Overlap(v, o)) hits.Add(new KeyValuePair<VehicleController, VehicleController>(v, o));
                        }
                    }
                }
            }
        }
    }
}
