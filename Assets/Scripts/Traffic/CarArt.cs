using System.Collections.Generic;
using UnityEngine;
using IntersectionMaster.Data;

namespace IntersectionMaster.Traffic
{
    /// <summary>
    /// Dibujo de los vehículos vistos desde arriba, como el original: carrocería con capó, parabrisas en trapecio con sus
    /// parales, techo, ventanas laterales con pilares, luneta y espejos. Se genera por código una textura en grises por
    /// modelo y tamaño (el color del coche la tiñe al dibujarla, así sirven el color del coche, el tinte y el choque).
    /// Coordenadas: u a lo largo (0 = detrás, 1 = delante), v a lo ancho (0 = izquierda, 1 = derecha del coche).
    /// </summary>
    public static class CarArt
    {
        public enum Model { Sedan, Hatch, Suv, Van, Truck, Bus }

        /// <summary>Margen a cada lado (en anchos de coche) para que quepan los espejos.</summary>
        public const float Margin = 0.12f;
        const int PixelsPerUnit = 320;

        static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

        public static Model ModelFor(VehicleType t, string label)
        {
            switch (t)
            {
                case VehicleType.Van: case VehicleType.Ambulance: return Model.Van;
                case VehicleType.Truck: case VehicleType.FireTruck: return Model.Truck;
                case VehicleType.Bus: return Model.Bus;
                case VehicleType.Taxi: case VehicleType.Police: return Model.Sedan;
            }
            if (label == "Compacto") return Model.Hatch;
            if (label == "Auto grande") return Model.Suv;
            return Model.Sedan;
        }

        struct Prim
        {
            public bool quad, outside;
            public float u0, u1, v0, v1;
            public Vector2 a, b, c, d;
            public Color32 col;
        }

        static Prim R(float u0, float u1, float v0, float v1, Color32 c, bool outside = false)
        { return new Prim { u0 = u0, u1 = u1, v0 = v0, v1 = v1, col = c, outside = outside }; }
        static Prim Q(float ua, float va, float ub, float vb, float uc, float vc, float ud, float vd, Color32 c)
        { return new Prim { quad = true, a = new Vector2(ua, va), b = new Vector2(ub, vb), c = new Vector2(uc, vc), d = new Vector2(ud, vd), col = c }; }

        static readonly Color32 Glass = new Color32(18, 20, 26, 255);
        static Color32 G(int g) { return new Color32((byte)g, (byte)g, (byte)g, 255); }

        /// <summary>Piezas del modelo, en orden de dibujo (la última que contiene el punto manda).</summary>
        static List<Prim> Parts(Model m)
        {
            var p = new List<Prim>();
            Color32 hood = G(236), roof = G(252), trunk = G(232), mirror = G(205), rail = G(150);
            switch (m)
            {
                case Model.Sedan:
                    p.Add(R(0.72f, 0.97f, 0.1f, 0.9f, hood));
                    p.Add(Q(0.72f, 0.07f, 0.72f, 0.93f, 0.58f, 0.83f, 0.58f, 0.17f, Glass));     // parabrisas
                    p.Add(R(0.33f, 0.58f, 0.15f, 0.85f, roof));
                    p.Add(Q(0.33f, 0.17f, 0.33f, 0.83f, 0.22f, 0.9f, 0.22f, 0.1f, Glass));       // luneta
                    p.Add(R(0.36f, 0.57f, 0.03f, 0.12f, Glass)); p.Add(R(0.36f, 0.57f, 0.88f, 0.97f, Glass));
                    p.Add(R(0.455f, 0.475f, 0f, 1f, G(225)));                                    // pilar central
                    p.Add(R(0.03f, 0.21f, 0.1f, 0.9f, trunk));
                    break;
                case Model.Hatch:
                    p.Add(R(0.74f, 0.97f, 0.1f, 0.9f, hood));
                    p.Add(Q(0.74f, 0.07f, 0.74f, 0.93f, 0.6f, 0.83f, 0.6f, 0.17f, Glass));
                    p.Add(R(0.14f, 0.6f, 0.15f, 0.85f, roof));
                    p.Add(R(0.16f, 0.59f, 0.03f, 0.12f, Glass)); p.Add(R(0.16f, 0.59f, 0.88f, 0.97f, Glass));
                    p.Add(R(0.4f, 0.42f, 0f, 1f, G(225)));
                    p.Add(R(0.04f, 0.13f, 0.12f, 0.88f, Glass));                                 // luneta pequeña
                    break;
                case Model.Suv:
                    p.Add(R(0.74f, 0.97f, 0.1f, 0.9f, hood));
                    p.Add(Q(0.74f, 0.06f, 0.74f, 0.94f, 0.62f, 0.86f, 0.62f, 0.14f, Glass));
                    p.Add(R(0.12f, 0.62f, 0.12f, 0.88f, roof));
                    p.Add(R(0.14f, 0.61f, 0.03f, 0.1f, Glass)); p.Add(R(0.14f, 0.61f, 0.9f, 0.97f, Glass));
                    p.Add(R(0.42f, 0.44f, 0f, 1f, G(225))); p.Add(R(0.27f, 0.29f, 0f, 1f, G(225)));
                    p.Add(R(0.18f, 0.58f, 0.2f, 0.24f, rail)); p.Add(R(0.18f, 0.58f, 0.76f, 0.8f, rail));   // barras del techo
                    p.Add(R(0.42f, 0.54f, 0.32f, 0.68f, Glass));                                 // techo solar
                    p.Add(R(0.03f, 0.11f, 0.12f, 0.88f, Glass));
                    break;
                case Model.Van:
                    p.Add(R(0.86f, 0.97f, 0.1f, 0.9f, hood));
                    p.Add(Q(0.86f, 0.06f, 0.86f, 0.94f, 0.76f, 0.86f, 0.76f, 0.14f, Glass));
                    p.Add(R(0.03f, 0.76f, 0.1f, 0.9f, roof));
                    p.Add(R(0.64f, 0.75f, 0.03f, 0.1f, Glass)); p.Add(R(0.64f, 0.75f, 0.9f, 0.97f, Glass));
                    p.Add(R(0.3f, 0.32f, 0.12f, 0.88f, G(225))); p.Add(R(0.55f, 0.57f, 0.12f, 0.88f, G(225)));   // nervios del techo
                    break;
                case Model.Truck:
                    p.Add(R(0.92f, 0.98f, 0.1f, 0.9f, hood));
                    p.Add(Q(0.92f, 0.06f, 0.92f, 0.94f, 0.85f, 0.86f, 0.85f, 0.14f, Glass));
                    p.Add(R(0.72f, 0.85f, 0.1f, 0.9f, roof));
                    p.Add(R(0.69f, 0.72f, 0.05f, 0.95f, G(60)));                                  // separación cabina-caja
                    break;
                case Model.Bus:
                    p.Add(Q(0.985f, 0.05f, 0.985f, 0.95f, 0.94f, 0.9f, 0.94f, 0.1f, Glass));
                    p.Add(R(0.03f, 0.94f, 0.12f, 0.88f, roof));
                    p.Add(R(0.06f, 0.92f, 0.03f, 0.12f, Glass)); p.Add(R(0.06f, 0.92f, 0.88f, 0.97f, Glass));
                    for (int i = 1; i < 8; i++) { float u = 0.06f + i * 0.86f / 8f; p.Add(R(u - 0.006f, u + 0.006f, 0f, 1f, G(225))); }
                    p.Add(R(0.4f, 0.58f, 0.3f, 0.7f, G(200)));                                    // aire acondicionado
                    p.Add(R(0.02f, 0.05f, 0.15f, 0.85f, Glass));
                    break;
            }
            // Espejos (sobresalen a los lados, a la altura del parabrisas).
            float mu = m == Model.Bus ? 0.94f : m == Model.Truck ? 0.88f : m == Model.Van ? 0.82f : 0.7f;
            p.Add(R(mu - 0.035f, mu, -0.1f, 0.02f, mirror, true));
            p.Add(R(mu - 0.035f, mu, 0.98f, 1.1f, mirror, true));
            return p;
        }

        static bool InQuad(Prim q, Vector2 p)
        {
            Vector2[] v = { q.a, q.b, q.c, q.d };
            float sgn = 0f;
            for (int i = 0; i < 4; i++)
            {
                Vector2 e = v[(i + 1) % 4] - v[i], w = p - v[i];
                float c = e.x * w.y - e.y * w.x;
                if (Mathf.Abs(c) < 1e-9f) continue;
                if (sgn == 0f) sgn = Mathf.Sign(c);
                else if (Mathf.Sign(c) != sgn) return false;
            }
            return true;
        }

        /// <summary>Sprite del modelo para un coche de largo L y ancho W. Mide 1 de ancho: se escala por (L, L).</summary>
        public static Sprite Get(Model m, float L, float W)
        {
            string key = m + ":" + Mathf.RoundToInt(L * 100f) + ":" + Mathf.RoundToInt(W * 100f);
            Sprite sp;
            if (cache.TryGetValue(key, out sp)) return sp;
            int tw = Mathf.Max(16, Mathf.RoundToInt(L * PixelsPerUnit));
            float fullW = W * (1f + 2f * Margin);
            int th = Mathf.Max(8, Mathf.RoundToInt(fullW * PixelsPerUnit));
            var parts = Parts(m);
            float bodyPx = W * PixelsPerUnit;
            float rFront = (m == Model.Bus || m == Model.Truck || m == Model.Van ? 0.1f : 0.2f) * W, rRear = 0.12f * W;
            var px = new Color32[tw * th];
            Color32 baseCol = G(226);
            for (int y = 0; y < th; y++)
            {
                for (int x = 0; x < tw; x++)
                {
                    float r = 0f, g = 0f, b = 0f, a = 0f;
                    for (int sy = 0; sy < 2; sy++)
                        for (int sx = 0; sx < 2; sx++)
                        {
                            float wu = (x + 0.25f + 0.5f * sx) / tw;              // 0..1 a lo largo
                            float wy = (y + 0.25f + 0.5f * sy) / th * fullW;      // 0..fullW
                            float wv = (wy - W * Margin) / W;                     // 0..1 en la carrocería
                            Color32 c = new Color32(0, 0, 0, 0);
                            bool inBody = wu >= 0f && wu <= 1f && wv >= 0f && wv <= 1f;
                            if (inBody)
                            {
                                // Esquinas redondeadas (más delante en los turismos).
                                float lu = wu * L, lv = wv * W, rad = wu > 0.5f ? rFront : rRear;
                                float cu = wu > 0.5f ? L - rad : rad, cv = wv > 0.5f ? W - rad : rad;
                                bool corner = (wu > 0.5f ? lu > cu : lu < cu) && (wv > 0.5f ? lv > cv : lv < cv);
                                if (corner && (lu - cu) * (lu - cu) + (lv - cv) * (lv - cv) > rad * rad) inBody = false;
                            }
                            if (inBody) c = baseCol;
                            var pt = new Vector2(wu, wv);
                            foreach (var pr in parts)
                            {
                                if (!inBody && !pr.outside) continue;
                                bool hit = pr.quad ? InQuad(pr, pt) : (wu >= pr.u0 && wu <= pr.u1 && wv >= pr.v0 && wv <= pr.v1);
                                if (hit) c = pr.col;
                            }
                            if (inBody)
                            {
                                // Borde un poco más oscuro: da volumen.
                                float dEdge = Mathf.Min(Mathf.Min(wu * tw, (1f - wu) * tw), Mathf.Min(wv * bodyPx, (1f - wv) * bodyPx));
                                if (dEdge < 1.6f) { c.r = (byte)(c.r * 0.7f); c.g = (byte)(c.g * 0.7f); c.b = (byte)(c.b * 0.7f); }
                            }
                            float ca = c.a / 255f;
                            r += c.r * ca; g += c.g * ca; b += c.b * ca; a += ca;
                        }
                    if (a > 0f) px[y * tw + x] = new Color32((byte)(r / a), (byte)(g / a), (byte)(b / a), (byte)(a / 4f * 255f));
                    else px[y * tw + x] = new Color32(0, 0, 0, 0);
                }
            }
            var t = new Texture2D(tw, th, TextureFormat.RGBA32, false);
            t.SetPixels32(px);
            t.filterMode = FilterMode.Bilinear;
            t.Apply();
            sp = Sprite.Create(t, new Rect(0, 0, tw, th), new Vector2(0.5f, 0.5f), tw);
            cache[key] = sp;
            return sp;
        }
    }
}
