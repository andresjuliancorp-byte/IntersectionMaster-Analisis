using System.Collections.Generic;
using UnityEngine;

namespace IntersectionMaster.Core
{
    /// <summary>
    /// Árboles vistos desde arriba hechos por código (dibujos propios, en gris para pintarlos de cualquier color):
    /// copa de muchas hojitas con borde irregular, luz arriba a la izquierda y sombra abajo a la derecha; palmera con hojas
    /// largas con nervio y foliolos; pino en estrella de agujas. Varias versiones de cada uno para que no se repitan.
    /// </summary>
    public static class TreeArt
    {
        const int N = 128;
        static readonly Dictionary<int, Sprite> cache = new Dictionary<int, Sprite>();

        public static Sprite Get(int kind, int version)
        {
            int key = kind * 10 + (version & 3);
            Sprite sp;
            if (cache.TryGetValue(key, out sp)) return sp;
            var px = new Color[N * N];
            var rnd = new System.Random(1234 + key * 977);
            switch (kind)
            {
                case 2: Palm(px, rnd); break;
                case 3: Pine(px, rnd); break;
                default: Leafy(px, rnd, kind == 1); break;
            }
            var t = new Texture2D(N, N, TextureFormat.RGBA32, false);
            t.SetPixels(px);
            t.filterMode = FilterMode.Bilinear;
            t.wrapMode = TextureWrapMode.Clamp;
            t.Apply();
            sp = Sprite.Create(t, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), N);
            cache[key] = sp;
            return sp;
        }

        static void Blob(Color[] px, float cx, float cy, float rx, float ry, float ang, float g, float a)
        {
            float c = Mathf.Cos(ang), s = Mathf.Sin(ang);
            int r = Mathf.CeilToInt(Mathf.Max(rx, ry)) + 1;
            for (int y = Mathf.Max(0, (int)cy - r); y < Mathf.Min(N, (int)cy + r + 1); y++)
                for (int x = Mathf.Max(0, (int)cx - r); x < Mathf.Min(N, (int)cx + r + 1); x++)
                {
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                    float u = (dx * c + dy * s) / rx, v = (-dx * s + dy * c) / ry;
                    float d = u * u + v * v;
                    if (d > 1f) continue;
                    float aa = a * Mathf.Clamp01((1f - d) * 3f);
                    var o = px[y * N + x];
                    float na = aa + o.a * (1f - aa);
                    float ng = na > 0f ? (g * aa + o.r * o.a * (1f - aa)) / na : g;
                    px[y * N + x] = new Color(ng, ng, ng, na);
                }
        }

        static void Leafy(Color[] px, System.Random rnd, bool autumn)
        {
            float R = N * 0.44f;
            // Borde irregular: radio que cambia con el ángulo.
            var bumps = new float[24];
            for (int i = 0; i < bumps.Length; i++) bumps[i] = 0.82f + (float)rnd.NextDouble() * 0.18f;
            for (int i = 0; i < 1500; i++)
            {
                float ang = (float)(rnd.NextDouble() * Mathf.PI * 2f);
                float rr = Mathf.Sqrt((float)rnd.NextDouble());
                int bi = Mathf.FloorToInt(ang / (Mathf.PI * 2f) * bumps.Length) % bumps.Length;
                float lim = R * bumps[bi];
                float x = N * 0.5f + Mathf.Cos(ang) * rr * lim, y = N * 0.5f + Mathf.Sin(ang) * rr * lim;
                // Luz desde arriba a la izquierda: más claro ahí, más oscuro abajo a la derecha y en el borde.
                float lit = 0.5f + 0.35f * (((x - N * 0.5f) * -0.6f + (y - N * 0.5f) * 0.8f) / R) - 0.15f * rr;
                float g = Mathf.Clamp01(lit + ((float)rnd.NextDouble() - 0.5f) * 0.35f + (autumn ? 0.05f : 0f));
                float sz = 2.2f + (float)rnd.NextDouble() * 3.2f;
                Blob(px, x, y, sz, sz * 0.6f, (float)(rnd.NextDouble() * Mathf.PI), 0.35f + g * 0.65f, 0.95f);
            }
        }

        static void Palm(Color[] px, System.Random rnd)
        {
            int leaves = 8 + rnd.Next(2);
            float off = (float)rnd.NextDouble() * 6.28f;
            for (int l = 0; l < leaves; l++)
            {
                float ang = off + l * Mathf.PI * 2f / leaves + ((float)rnd.NextDouble() - 0.5f) * 0.25f;
                float len = N * (0.44f + (float)rnd.NextDouble() * 0.05f);
                Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)), nrm = new Vector2(-dir.y, dir.x);
                float bendS = ((float)rnd.NextDouble() - 0.5f) * 0.4f;
                for (int k = 0; k < 40; k++)
                {
                    float t = k / 39f;
                    Vector2 c = new Vector2(N * 0.5f, N * 0.5f) + dir * (t * len) + nrm * (bendS * t * t * len);
                    float w = N * 0.085f * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * Mathf.Min(1f, t * 1.05f + 0.05f))), 0.8f);
                    float shade = 0.55f + 0.3f * Vector2.Dot(dir, new Vector2(-0.6f, 0.8f));
                    // Foliolos a los lados (rayitas) y el nervio central más oscuro.
                    for (int side = -1; side <= 1; side += 2)
                        Blob(px, c.x + nrm.x * side * w * 0.55f, c.y + nrm.y * side * w * 0.55f, w * 0.8f + 1f, 1.5f, ang + side * 0.9f, Mathf.Clamp01(shade + ((float)rnd.NextDouble() - 0.5f) * 0.2f), 0.95f);
                    Blob(px, c.x, c.y, 1.2f, 1.2f, 0f, shade * 0.6f, 0.9f);
                }
            }
            Blob(px, N * 0.5f, N * 0.5f, N * 0.05f, N * 0.05f, 0f, 0.35f, 1f);
        }

        static void Pine(Color[] px, System.Random rnd)
        {
            int points = 14 + rnd.Next(3);
            for (int layer = 0; layer < 3; layer++)
            {
                float R = N * (0.46f - layer * 0.12f);
                float baseG = 0.45f + layer * 0.18f;
                for (int i = 0; i < points; i++)
                {
                    float ang = (i + layer * 0.5f) * Mathf.PI * 2f / points;
                    for (int k = 0; k < 14; k++)
                    {
                        float t = k / 13f;
                        float x = N * 0.5f + Mathf.Cos(ang) * t * R, y = N * 0.5f + Mathf.Sin(ang) * t * R;
                        float w = (1f - t) * N * 0.12f + 2.5f;
                        float g = Mathf.Clamp01(baseG + 0.2f * (Mathf.Cos(ang) * -0.6f + Mathf.Sin(ang) * 0.8f) + ((float)rnd.NextDouble() - 0.5f) * 0.15f);
                        Blob(px, x, y, w, w * 0.8f, ang, g, 0.95f);
                    }
                }
            }
        }
    }
}
