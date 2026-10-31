using System.Collections.Generic;
using UnityEngine;
using IntersectionMaster.Core;
using IntersectionMaster.Data;

namespace IntersectionMaster.Traffic
{
    /// <summary>
    /// Peatones animados. Aparecen junto a los pasos de peatones (los de cada cruce y los puestos a mano), esperan en
    /// la acera a que no venga ningún coche y cruzan. Mientras alguien está en la calzada, los vehículos se detienen
    /// antes del paso (ver VehicleController).
    /// </summary>
    public class PedestrianSystem
    {
        const int MaxPeds = 14;
        const int MaxPerCrosswalk = 3;
        const float WalkSpeed = 0.6f;    // unidades por segundo
        const float CurbMargin = 0.22f;

        class Pedestrian
        {
            public Crosswalk cw;
            public Vector2 a, b;
            public float t;
            public bool crossing;
            public float wait;
            public float phase;
            public Transform tr;
            public SpriteRenderer body;
            public int stage;                       // 0 llega por la acera, 1 espera/cruza, 2 se aleja
            public List<Vector2> inPath, outPath;   // aceras desde/hacia fuera del área visible
            public float walked;
        }

        static readonly string[] Shirts = { "#E5534B", "#4C8DF6", "#F6C945", "#3FB950", "#8C6BD9", "#E08A2B", "#F2F2F2", "#2E9AA3" };

        public readonly List<Crosswalk> Crosswalks = new List<Crosswalk>();
        public int Crossed { get; private set; }

        readonly List<Pedestrian> peds = new List<Pedestrian>();
        Transform root;
        float spawnAcc;
        Rect view;
        Dictionary<int, NetRoad> roadById;
        const float ApproachSpeed = 0.75f;

        public int Count { get { return peds.Count; } }

        public void Clear()
        {
            Crosswalks.Clear();
            for (int i = 0; i < peds.Count; i++) if (peds[i].tr != null) Object.Destroy(peds[i].tr.gameObject);
            peds.Clear();
            Crossed = 0;
            spawnAcc = 0f;
            root = null;
        }

        public void Build(MapData map, RoadNetworkManager net, Transform parent)
        {
            Clear();
            root = parent;
            view = map.View;
            roadById = net.roadById;
            for (int i = 0; i < net.crosswalks.Count; i++)
            {
                Crosswalks.Add(net.crosswalks[i]);
            }
        }

        public void Step(float dt, List<VehicleController> vehicles, float perMinute)
        {
            if (Crosswalks.Count == 0 || root == null) return;

            if (perMinute > 0f && peds.Count < MaxPeds)
            {
                spawnAcc += perMinute / 60f * dt;
                while (spawnAcc >= 1f)
                {
                    spawnAcc -= 1f;
                    Spawn();
                }
            }
            else spawnAcc = Mathf.Min(spawnAcc, 1f);

            for (int k = 0; k < Crosswalks.Count; k++) { Crosswalks[k].crossing = 0; Crosswalks[k].waiting = 0; }

            for (int i = peds.Count - 1; i >= 0; i--)
            {
                var p = peds[i];
                p.phase += dt * 11f;
                if (p.stage == 0)
                {
                    p.walked += dt * ApproachSpeed;
                    if (p.walked >= PathLength(p.inPath)) { p.stage = 1; p.walked = 0f; }
                }
                else if (p.stage == 1)
                {
                    if (!p.crossing)
                    {
                        p.wait += dt;
                        p.cw.waiting++;
                        if (CanStep(p, vehicles)) p.crossing = true;
                    }
                    else
                    {
                        p.t += dt * WalkSpeed / Mathf.Max(0.1f, (p.b - p.a).magnitude);
                        p.cw.crossing++;
                        if (p.t >= 1f)
                        {
                            Crossed++;
                            p.stage = 2;
                            p.walked = 0f;
                            if (PathLength(p.outPath) < 0.05f) { Remove(i); continue; }
                        }
                    }
                }
                else
                {
                    p.walked += dt * ApproachSpeed;
                    if (p.walked >= PathLength(p.outPath)) { Remove(i); continue; }
                }
                Place(p);
            }

            for (int k = 0; k < Crosswalks.Count; k++) Crosswalks[k].busy = Crosswalks[k].crossing > 0;
        }

        void Spawn()
        {
            // Elige un paso con sitio libre.
            for (int tries = 0; tries < 6; tries++)
            {
                var cw = Crosswalks[Random.Range(0, Crosswalks.Count)];
                int here = 0;
                for (int i = 0; i < peds.Count; i++) if (peds[i].cw == cw) here++;
                if (here >= MaxPerCrosswalk) continue;

                float side = Random.value < 0.5f ? 1f : -1f;
                var p = new Pedestrian
                {
                    cw = cw,
                    a = cw.center - cw.across * side * (cw.halfWidth + CurbMargin) + cw.axis * Random.Range(-0.08f, 0.08f),
                    phase = Random.value * 6f
                };
                p.b = p.a + cw.across * side * ((cw.halfWidth + CurbMargin) * 2f);
                p.inPath = Sidewalk(cw, p.a, Random.value < 0.5f ? 1f : -1f);
                p.inPath.Reverse();
                p.outPath = Sidewalk(cw, p.b, Random.value < 0.5f ? 1f : -1f);
                p.stage = 0;

                var go = new GameObject("Pedestrian");
                go.transform.SetParent(root, false);
                p.tr = go.transform;
                p.body = SpriteFactory.Make("Body", p.tr, SpriteFactory.Circle, SpriteFactory.Hex(Shirts[Random.Range(0, Shirts.Length)]),
                    Vector2.zero, new Vector2(0.11f, 0.19f), 19);
                SpriteFactory.Make("Head", p.tr, SpriteFactory.Circle, SpriteFactory.Hex("#E8C39E"), Vector2.zero, Vector2.one * 0.095f, 20);
                float ang = Mathf.Atan2(cw.across.y * side, cw.across.x * side) * Mathf.Rad2Deg;
                p.tr.rotation = Quaternion.Euler(0f, 0f, ang);
                Place(p);
                peds.Add(p);
                return;
            }
        }

        void Remove(int i)
        {
            if (peds[i].tr != null) Object.Destroy(peds[i].tr.gameObject);
            peds.RemoveAt(i);
        }

        static float PathLength(List<Vector2> pts)
        {
            float len = 0f;
            for (int i = 1; i < pts.Count; i++) len += Vector2.Distance(pts[i - 1], pts[i]);
            return len;
        }

        static Vector2 AlongPath(List<Vector2> pts, float d, out Vector2 dir)
        {
            dir = Vector2.right;
            if (pts.Count == 0) return Vector2.zero;
            for (int i = 1; i < pts.Count; i++)
            {
                float seg = Vector2.Distance(pts[i - 1], pts[i]);
                if (seg > 0.0001f) dir = (pts[i] - pts[i - 1]) / seg;
                if (d <= seg || i == pts.Count - 1)
                    return Vector2.Lerp(pts[i - 1], pts[i], seg > 0.0001f ? Mathf.Clamp01(d / seg) : 1f);
                d -= seg;
            }
            return pts[0];
        }

        /// <summary>Acera junto a la calle del paso, desde «start» hasta salir del área visible (o el final de la calle).</summary>
        List<Vector2> Sidewalk(Crosswalk cw, Vector2 start, float dirSign)
        {
            var pts = new List<Vector2> { start };
            NetRoad nr;
            if (roadById == null || !roadById.TryGetValue(cw.roadId, out nr)) return pts;
            Polyline c = nr.center;
            float dist;
            float s0 = c.Project(cw.center, out dist);
            float side = Vector2.Dot(Geo.Right(c.TangentAt(s0)), start - c.PointAt(s0)) >= 0f ? 1f : -1f;
            float off = cw.halfWidth + CurbMargin;
            for (float d = 0.5f; d <= 18f; d += 0.5f)
            {
                float sp = s0 + dirSign * d;
                if (sp < 0f || sp > c.Length) break;
                Vector2 q = c.PointAt(sp) + Geo.Right(c.TangentAt(sp)) * (side * off);
                pts.Add(q);
                if (!view.Contains(q)) break;
            }
            return pts;
        }

        void Place(Pedestrian p)
        {
            if (p.tr == null) return;
            Vector2 pos, dir;
            bool walking = true;
            if (p.stage == 0) pos = AlongPath(p.inPath, p.walked, out dir);
            else if (p.stage == 2) pos = AlongPath(p.outPath, p.walked, out dir);
            else
            {
                pos = Vector2.Lerp(p.a, p.b, Mathf.Clamp01(p.t));
                dir = p.b - p.a;
                walking = p.crossing;
            }
            p.tr.position = new Vector3(pos.x, pos.y, 0f);
            if (dir.sqrMagnitude > 0.0001f) p.tr.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
            // Animación al caminar: balanceo de los hombros; parado, respiración leve.
            float sway = walking ? Mathf.Sin(p.phase) * 0.22f : Mathf.Sin(p.phase * 0.25f) * 0.05f;
            p.body.transform.localScale = new Vector3(0.11f * (1f + sway), 0.19f * (1f - sway * 0.5f), 1f);
        }

        // El peatón sale a la calzada si ningún vehículo está sobre el paso ni llega demasiado rápido para frenar.
        bool CanStep(Pedestrian p, List<VehicleController> vehicles)
        {
            var cw = p.cw;
            // Semáforo peatonal: si el paso está junto a un acceso con semáforo, solo se cruza cuando los coches de ese acceso tienen rojo.
            if (cw.lightApp != null)
            {
                var ic = cw.lightApp.node.controller;
                if (ic != null && ic.IsLit && ic.GetState(cw.lightApp) != SignalState.Red) return false;
            }
            float reach = cw.halfWidth + 0.5f;
            for (int i = 0; i < vehicles.Count; i++)
            {
                var v = vehicles[i];
                Vector2 d = cw.center - v.Pos;
                float along = -Vector2.Dot(d, cw.axis);          // posición del coche a lo largo de la calzada
                float lateral = Mathf.Abs(Vector2.Dot(d, cw.across));
                if (lateral > reach) continue;
                if (Mathf.Abs(along) < Crosswalk.ZoneHalf + v.HalfLength + 0.05f)
                {
                    if (p.wait > 14f && v.speed < 0.05f && !v.crashed) continue;   // coche parado sobre el paso: se rodea
                    return false;
                }
                float dist = d.magnitude;
                bool approaching = Vector2.Dot(d, v.Dir) > 0f;
                if (approaching && v.speed > 0.4f && dist < 0.9f) return false;
                if (approaching && v.speed > 0.2f && dist < 1.4f && p.wait < 8f) return false;
            }
            return true;
        }
    }
}
