using System.Collections.Generic;
using UnityEngine;

namespace IntersectionMaster.Core
{
    /// <summary>Polilínea con longitudes acumuladas: posición, tangente y proyección por longitud de arco.</summary>
    public class Polyline
    {
        public readonly Vector2[] pts;
        public readonly float[] cum;
        public readonly float Length;

        public Polyline(IList<Vector2> points)
        {
            int n = points == null ? 0 : points.Count;
            pts = new Vector2[Mathf.Max(2, n)];
            for (int i = 0; i < n; i++) pts[i] = points[i];
            if (n == 0) { pts[0] = Vector2.zero; pts[1] = new Vector2(0.001f, 0f); }
            else if (n == 1) pts[1] = pts[0] + new Vector2(0.001f, 0f);
            cum = new float[pts.Length];
            for (int i = 1; i < pts.Length; i++) cum[i] = cum[i - 1] + Vector2.Distance(pts[i - 1], pts[i]);
            Length = cum[cum.Length - 1];
        }

        public Vector2 Start { get { return pts[0]; } }
        public Vector2 End { get { return pts[pts.Length - 1]; } }

        int SegIndex(float s)
        {
            if (s <= 0f) return 0;
            for (int i = 0; i < pts.Length - 1; i++)
            {
                if (s <= cum[i + 1]) return i;
            }
            return pts.Length - 2;
        }

        public Vector2 PointAt(float s)
        {
            s = Mathf.Clamp(s, 0f, Length);
            int i = SegIndex(s);
            float segLen = cum[i + 1] - cum[i];
            float t = segLen > 1e-6f ? (s - cum[i]) / segLen : 0f;
            return Vector2.Lerp(pts[i], pts[i + 1], t);
        }

        public Vector2 TangentAt(float s)
        {
            s = Mathf.Clamp(s, 0f, Length);
            int i = SegIndex(s);
            Vector2 d = pts[i + 1] - pts[i];
            if (d.sqrMagnitude < 1e-10f)
            {
                // Tramo degenerado: busca el siguiente tramo válido o usa la dirección general.
                for (int k = i; k < pts.Length - 1; k++)
                {
                    Vector2 e = pts[k + 1] - pts[k];
                    if (e.sqrMagnitude > 1e-10f) return e.normalized;
                }
                for (int k = i - 1; k >= 0; k--)
                {
                    Vector2 e = pts[k + 1] - pts[k];
                    if (e.sqrMagnitude > 1e-10f) return e.normalized;
                }
                return Vector2.right;
            }
            return d.normalized;
        }

        /// <summary>Tramo de la polilínea entre las longitudes de arco s0 y s1 (conserva los vértices intermedios).</summary>
        public Polyline Sub(float s0, float s1)
        {
            s0 = Mathf.Clamp(s0, 0f, Length);
            s1 = Mathf.Clamp(s1, 0f, Length);
            var list = new List<Vector2>();
            if (s1 - s0 < 0.02f)
            {
                Vector2 p = PointAt(s0);
                Vector2 d = TangentAt(s0) * 0.02f;
                list.Add(p);
                list.Add(p + d);
                return new Polyline(list);
            }
            list.Add(PointAt(s0));
            for (int i = 1; i < pts.Length - 1; i++)
            {
                if (cum[i] > s0 + 0.01f && cum[i] < s1 - 0.01f) list.Add(pts[i]);
            }
            list.Add(PointAt(s1));
            return new Polyline(list);
        }

        /// <summary>Vértices desplazados lateralmente u (positivo = a la derecha de la marcha), con unión en inglete.</summary>
        public Vector2[] OffsetPts(float u)
        {
            int n = pts.Length;
            var res = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                Vector2 dPrev = i > 0 ? pts[i] - pts[i - 1] : pts[1] - pts[0];
                Vector2 dNext = i < n - 1 ? pts[i + 1] - pts[i] : pts[n - 1] - pts[n - 2];
                if (dPrev.sqrMagnitude < 1e-10f) dPrev = dNext;
                if (dNext.sqrMagnitude < 1e-10f) dNext = dPrev;
                if (dPrev.sqrMagnitude < 1e-10f) { dPrev = Vector2.right; dNext = Vector2.right; }
                Vector2 nPrev = Geo.Right(dPrev.normalized);
                Vector2 nNext = Geo.Right(dNext.normalized);
                Vector2 nm = nPrev + nNext;
                if (nm.sqrMagnitude < 1e-6f) nm = nNext; else nm.Normalize();
                float k = 1f / Mathf.Max(0.6f, Vector2.Dot(nm, nNext));
                res[i] = pts[i] + nm * (u * k);
            }
            return res;
        }

        public Polyline Reversed()
        {
            var r = new Vector2[pts.Length];
            for (int i = 0; i < pts.Length; i++) r[i] = pts[pts.Length - 1 - i];
            return new Polyline(r);
        }

        /// <summary>Longitud de arco del punto más cercano a p; dist = distancia a la polilínea.</summary>
        public float Project(Vector2 p, out float dist)
        {
            float best = float.MaxValue;
            float bestS = 0f;
            for (int i = 0; i < pts.Length - 1; i++)
            {
                float t;
                float d = Geo.DistToSegment(p, pts[i], pts[i + 1], out t);
                if (d < best)
                {
                    best = d;
                    bestS = cum[i] + t * (cum[i + 1] - cum[i]);
                }
            }
            dist = best;
            return bestS;
        }
    }

    public static class Geo
    {
        public static Vector2 Quad(Vector2 a, Vector2 c, Vector2 b, float t)
        {
            float u = 1f - t;
            return u * u * a + 2f * u * t * c + t * t * b;
        }

        public static Vector2 Cubic(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float u = 1f - t;
            return u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;
        }

        /// <summary>Normal hacia la derecha de la dirección de marcha.</summary>
        public static Vector2 Right(Vector2 dir)
        {
            return new Vector2(dir.y, -dir.x);
        }

        public static Vector2 Left(Vector2 dir)
        {
            return new Vector2(-dir.y, dir.x);
        }

        public static float Angle(Vector2 v)
        {
            return Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
        }

        public static float Angle360(Vector2 v)
        {
            float a = Angle(v);
            if (a < 0f) a += 360f;
            return a;
        }

        public static Vector2 FromAngle(float deg)
        {
            float r = deg * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(r), Mathf.Sin(r));
        }

        public static float Cross(Vector2 a, Vector2 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        public static float Snap(float v, float step)
        {
            return Mathf.Round(v / step) * step;
        }

        public static float DistToSegment(Vector2 p, Vector2 a, Vector2 b, out float t)
        {
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            t = len2 > 1e-10f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2) : 0f;
            return Vector2.Distance(p, a + ab * t);
        }

        /// <summary>Intersección de los segmentos ab y cd; t y u son las fracciones sobre cada uno.</summary>
        public static bool SegSeg(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out float t, out float u)
        {
            t = u = 0f;
            Vector2 r = b - a;
            Vector2 s = d - c;
            float denom = Cross(r, s);
            if (Mathf.Abs(denom) < 1e-9f) return false;
            Vector2 ca = c - a;
            t = Cross(ca, s) / denom;
            u = Cross(ca, r) / denom;
            return t >= 0f && t <= 1f && u >= 0f && u <= 1f;
        }

        /// <summary>Primer cruce de dos polilíneas (el primero a lo largo de p). sP y sQ son las longitudes de arco.</summary>
        public static bool PolylineCross(Polyline p, Polyline q, out float sP, out float sQ)
        {
            sP = sQ = 0f;
            float bestS = float.MaxValue;
            bool found = false;
            for (int i = 0; i < p.pts.Length - 1; i++)
            {
                for (int j = 0; j < q.pts.Length - 1; j++)
                {
                    float t, u;
                    if (!SegSeg(p.pts[i], p.pts[i + 1], q.pts[j], q.pts[j + 1], out t, out u)) continue;
                    float sp = p.cum[i] + t * (p.cum[i + 1] - p.cum[i]);
                    if (sp < bestS)
                    {
                        bestS = sp;
                        sP = sp;
                        sQ = q.cum[j] + u * (q.cum[j + 1] - q.cum[j]);
                        found = true;
                    }
                }
            }
            return found;
        }

        public static float CircularDiff(float a, float b)
        {
            float d = Mathf.Abs(a - b) % 360f;
            return d > 180f ? 360f - d : d;
        }
    }
}
