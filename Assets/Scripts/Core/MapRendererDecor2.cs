using System.Collections.Generic;
using UnityEngine;
using IntersectionMaster.Data;
using IntersectionMaster.Traffic;

namespace IntersectionMaster.Core
{
    /// <summary>
    /// Decoración de los mapas por carriles con las opciones del original: edificios en 3D falso (altura, sombras,
    /// tejados, paredes y colores), árboles de 4 tipos, vallas de 11 tipos, aparcamientos con coches, coches aparcados
    /// con modelo, luces y colores, materiales con barras y transparencia, y pegatinas.
    /// </summary>
    public partial class MapRenderer
    {
        public static readonly string[] WallColorNames = { "Arena", "Ladrillo", "Gris", "Blanco", "Azul", "Verde", "Ocre", "Oscuro" };
        static readonly string[] WallColorHex = { "#D8C3A5", "#B5654A", "#A9A9A6", "#EDEBE6", "#8FA6C2", "#9DB59A", "#D2A85C", "#5E5F66" };
        public static readonly string[] RoofColorNames = { "Gris", "Teja", "Oscuro", "Blanco", "Verde", "Azul", "Marrón", "Arena" };
        static readonly string[] RoofColorHex = { "#8E9196", "#B4553C", "#4A4C52", "#E4E4E0", "#5E8A5B", "#4F6E96", "#7A5638", "#CDB98F" };
        public static readonly string[] RoofNames = { "Dos aguas", "Plano", "Cuatro aguas", "Terraza" };
        public static readonly string[] WallNames = { "Lisas", "Ventanas", "Cristal" };
        public static readonly string[] TreeNames = { "Frondoso", "Otoño", "Palmera", "Pino" };
        public static readonly string[] FenceNames = { "Malla", "Guardarraíl", "Blanca", "Oscura", "Estacas", "Tablas", "Rústica", "Hormigón", "Metal", "Roja y blanca", "Amarilla y negra" };
        public static readonly Color[] FenceTints = { new Color(0.62f, 0.64f, 0.66f), new Color(0.75f, 0.77f, 0.8f), Color.white, new Color(0.25f, 0.26f, 0.3f),
            new Color(0.66f, 0.45f, 0.28f), new Color(0.5f, 0.33f, 0.2f), new Color(0.58f, 0.4f, 0.25f), new Color(0.82f, 0.82f, 0.78f), new Color(0.15f, 0.15f, 0.17f),
            new Color(0.85f, 0.2f, 0.2f), new Color(0.95f, 0.78f, 0.1f) };
        public static readonly string[] CarModelNames = { "Aleatorio", "Sedán", "Compacto", "Todoterreno", "Furgoneta", "Camión", "Autobús" };
        public static readonly string[] CarColorNames = { "Aleatorio", "Rojo", "Azul", "Blanco", "Negro", "Amarillo", "Verde", "Gris", "Morado", "Naranja", "Marino" };
        public static readonly string[] CarColorHex = { "#C9524B", "#4F7FCB", "#E8E8E8", "#3A3D46", "#D8A93B", "#4E9A66", "#9AA0A8", "#7A4BB5", "#E07A2F", "#1F2A44" };
        public static readonly string[] ParkingNames = { "En batería", "En diagonal", "En línea" };
        public static readonly string[] BarNames = { "Sin barras", "/", "//", "////" };
        /// <summary>Pegatinas como el original (después de las 6 antiguas: Flecha recta… LENTO).</summary>
        public static readonly string[] Sticker2Names = { "Césped", "Mancha", "Mancha pequeña", "Mancha suave", "Parche", "Grietas", "Flecha", "Discapacitados",
            "Rombo", "Paso a nivel", "Ceda", "Bicicleta", "Flecha larga", "Incorporación der.", "Incorporación izq.", "Desvío", "Doble sentido",
            "Curva", "Flecha doble", "Discapacitados azul", "Adoquines", "Gravilla", "Gravilla en línea", "Gravilla suelta",
            "Flecha recto", "Flecha izquierda", "Flecha derecha", "Recto e izquierda", "Recto y derecha", "Izquierda y derecha", "Recto y ambos lados" };
        // Movimientos (1 izquierda, 2 recto, 4 derecha) de las 7 flechas de decoración del original (Sticker2Names desde la 24).
        static readonly int[] ArrowStickerMoves = { 2, 1, 4, 3, 6, 5, 7 };

        static float Hash01(Vector2 p, int k)
        {
            float h = Mathf.Sin(p.x * 12.9898f + p.y * 78.233f + k * 37.719f) * 43758.5453f;
            return h - Mathf.Floor(h);
        }

        static readonly Vector2 SunDesign = new Vector2(-1f, -0.12f).normalized;

        /// <summary>Dibuja la decoración con las opciones del original. Devuelve false si el tipo no se trata aquí.</summary>
        bool DrawDecorIC2(DecorItem d, MapData map)
        {
            switch (d.type)
            {
                case DecorType.Building: DrawBuilding2(d); return true;
                case DecorType.Tree: DrawTree2(d); return true;
                case DecorType.Fence: DrawFence2(d, map); return true;
                case DecorType.ParkedCar: DrawParkedCar2(d, map); return true;
                case DecorType.Parking: DrawParking2(d); return true;
                case DecorType.Material: DrawMaterial2(d); return true;
                case DecorType.Sticker:
                    if (Mathf.Abs(d.variant) < 6) return false;   // las 6 antiguas se dibujan como antes
                    DrawSticker2(d); return true;
            }
            return false;
        }

        // ---------------- Edificio ----------------

        void DrawBuilding2(DecorItem d)
        {
            Transform parent = staticRoot;
            Vector2 p = d.Pos;
            float S = 1.25f * d.size;
            Vector2 f = Geo.FromAngle(d.rot), n = new Vector2(-f.y, f.x);
            // Rectangular (importado del original: semiancho x2 y semialto y2) o cuadrado.
            float hx = d.x2 > 0.05f ? d.x2 * d.size : S * 0.5f, hy = d.y2 > 0.05f ? d.y2 * d.size : S * 0.5f;
            float H = 0.1f * d.height * (d.lowHeight ? 0.55f : 1f);   // como el original: 10 pisos ≈ 1 unidad
            Vector2 up = new Vector2(0f, H);
            Vector2[] c = { p - f * hx - n * hy, p + f * hx - n * hy, p + f * hx + n * hy, p - f * hx + n * hy };
            S = Mathf.Min(hx, hy) * 2f;
            Color wall = RoadData.ValidHex(d.hexA) ? SpriteFactory.Hex(d.hexA) : SpriteFactory.Hex(WallColorHex[Mathf.Abs(d.color) % WallColorHex.Length]);
            Color roofC = RoadData.ValidHex(d.hexB) ? SpriteFactory.Hex(d.hexB) : SpriteFactory.Hex(RoofColorHex[Mathf.Abs(d.roofColor) % RoofColorHex.Length]);

            if (!d.noShadow)
            {
                // Sombra «barrida»: la planta desplazada hacia el lado de la sombra, más larga cuanto más alto.
                float len = 0.1f + H * 1.1f;
                for (int i = 1; i <= 3; i++)
                    RegShadow(SpriteFactory.Make("BShadow", parent, SpriteFactory.Square, new Color(0f, 0f, 0f, 0.13f), p + SunDesign * (len * i / 3f), Vector2.one * S, 3, d.rot), p);
            }

            var mb = new MeshBatch();
            // Paredes: solo las caras que miran hacia abajo en pantalla (las que se ven desde la cámara inclinada).
            for (int i = 0; i < 4; i++)
            {
                Vector2 a = c[i], b = c[(i + 1) % 4];
                Vector2 o = Geo.Right((b - a).normalized);
                if (o.y > -0.02f) continue;
                float lit = 0.78f + 0.14f * Mathf.Clamp(-o.x, -1f, 1f);
                Color wc = Color.Lerp(Color.black, wall, lit);
                if (d.wall == 2) wc = Color.Lerp(wc, new Color(0.45f, 0.6f, 0.75f), 0.55f);
                mb.Quad(a, b, b + up, a + up, wc);
                float edge = (b - a).magnitude;
                for (int k = 0; k < d.height; k++)
                {
                    float z0 = H * (k + 0.3f) / d.height, z1 = H * (k + 0.75f) / d.height;
                    if (d.wall == 1)
                    {
                        int m = Mathf.Max(1, Mathf.RoundToInt(edge / 0.24f));
                        for (int w = 0; w < m; w++)
                        {
                            float t0 = (w + 0.22f) / m, t1 = (w + 0.78f) / m;
                            Vector2 q0 = Vector2.Lerp(a, b, t0), q1 = Vector2.Lerp(a, b, t1);
                            mb.Quad(q0 + new Vector2(0f, z0), q1 + new Vector2(0f, z0), q1 + new Vector2(0f, z1), q0 + new Vector2(0f, z1), new Color(0.22f, 0.27f, 0.36f, 1f));
                            // De noche se encienden algunas ventanas (luz cálida), como en una ciudad de verdad.
                            if (Mathf.Abs(o.x) < 0.35f && Hash01(p + new Vector2(w, k), i) < 0.45f)
                            {
                                Vector2 wc0 = (q0 + q1) * 0.5f + new Vector2(0f, (z0 + z1) * 0.5f);
                                RegNightGlow(SpriteFactory.Make("WinLit", parent, SpriteFactory.Square, new Color(1f, 0.84f, 0.5f, 0f), wc0, new Vector2((q1 - q0).magnitude, z1 - z0), NightOverlayOrder + 1, Mathf.Atan2((q1 - q0).y, (q1 - q0).x) * Mathf.Rad2Deg), 0.9f);
                            }
                        }
                    }
                    else if (d.wall == 2 && Mathf.Abs(o.x) < 0.35f && Hash01(p + new Vector2(k, 3f), i) < 0.5f)
                    {
                        // Edificio de cristal: de noche se ilumina algún piso entero.
                        Vector2 wc0 = (a + b) * 0.5f + new Vector2(0f, (z0 + z1) * 0.5f);
                        RegNightGlow(SpriteFactory.Make("FloorLit", parent, SpriteFactory.Square, new Color(1f, 0.88f, 0.6f, 0f), wc0, new Vector2((b - a).magnitude * 0.96f, z1 - z0), NightOverlayOrder + 1, Mathf.Atan2((b - a).y, (b - a).x) * Mathf.Rad2Deg), 0.55f);
                    }
                    if (d.wall == 2 && k > 0)
                    {
                        float zl = H * k / d.height;
                        mb.Quad(a + new Vector2(0f, zl - 0.006f), b + new Vector2(0f, zl - 0.006f), b + new Vector2(0f, zl + 0.006f), a + new Vector2(0f, zl + 0.006f), new Color(0.25f, 0.3f, 0.36f, 1f));
                    }
                }
            }
            mb.Build(parent, "BWalls", 5);

            var rb = new MeshBatch();
            Vector2 r0 = c[0] + up, r1 = c[1] + up, r2 = c[2] + up, r3 = c[3] + up, rc = p + up;
            switch (d.roof)
            {
                case 1:   // plano con pretil
                    rb.Quad(r0, r1, r2, r3, Color.Lerp(roofC, Color.black, 0.18f));
                    {
                        float ins = Mathf.Min(0.06f, S * 0.08f);
                        Vector2 i0 = r0 + (f + n) * ins, i1 = r1 + (-f + n) * ins, i2 = r2 + (-f - n) * ins, i3 = r3 + (f - n) * ins;
                        rb.Quad(i0, i1, i2, i3, roofC);
                    }
                    break;
                case 2:   // cuatro aguas
                    {
                        Vector2[] rr = { r0, r1, r2, r3 };
                        Vector2 ra = rc - f * S * 0.15f, rbb = rc + f * S * 0.15f;
                        for (int i = 0; i < 4; i++)
                        {
                            Vector2 a = rr[i], b = rr[(i + 1) % 4];
                            Vector2 o = Geo.Right((b - a).normalized);
                            float lit = 0.88f + 0.14f * Vector2.Dot(o, new Vector2(-0.6f, 0.8f));
                            Color col = Color.Lerp(Color.black, roofC, Mathf.Clamp01(lit));
                            Vector2 ca = Vector2.Dot(a - rc, f) < 0f ? ra : rbb, cb = Vector2.Dot(b - rc, f) < 0f ? ra : rbb;
                            if (ca == cb) rb.Tri(a, b, ca, col); else rb.Quad(a, b, cb, ca, col);
                        }
                        rb.Rect(rc, f, S * 0.3f, 0.025f, Color.Lerp(roofC, Color.black, 0.45f));
                    }
                    break;
                case 3:   // terraza: suelo claro con baldosas
                    rb.Quad(r0, r1, r2, r3, Color.Lerp(roofC, Color.white, 0.25f));
                    for (float t = 0.25f; t < 0.99f; t += 0.25f)
                    {
                        rb.Rect(Vector2.Lerp(r0, r1, t) + n * S * 0.5f, n, S, 0.012f, Color.Lerp(roofC, Color.black, 0.2f));
                        rb.Rect(Vector2.Lerp(r0, r3, t) + f * S * 0.5f, f, S, 0.012f, Color.Lerp(roofC, Color.black, 0.2f));
                    }
                    break;
                default:  // dos aguas: dos faldones y la cumbrera
                    {
                        Vector2 m0 = (r0 + r3) * 0.5f, m1 = (r1 + r2) * 0.5f;
                        float litA = Vector2.Dot(-n, new Vector2(-0.6f, 0.8f)), litB = Vector2.Dot(n, new Vector2(-0.6f, 0.8f));
                        rb.Quad(r0, r1, m1, m0, Color.Lerp(Color.black, roofC, Mathf.Clamp01(0.86f + 0.16f * litA)));
                        rb.Quad(m0, m1, r2, r3, Color.Lerp(Color.black, roofC, Mathf.Clamp01(0.86f + 0.16f * litB)));
                        rb.Rect((m0 + m1) * 0.5f, f, S, 0.03f, Color.Lerp(roofC, Color.black, 0.45f));
                    }
                    break;
            }
            if (d.roofItems)
            {
                // Aparatos del techo (aire acondicionado) y una claraboya.
                for (int i = 0; i < 2; i++)
                {
                    Vector2 q = rc + f * S * (i == 0 ? -0.22f : 0.18f) + n * S * (i == 0 ? 0.18f : -0.2f);
                    rb.Rect(q, f, S * 0.18f, S * 0.14f, new Color(0.72f, 0.74f, 0.76f));
                    rb.Rect(q + new Vector2(0f, 0.025f), f, S * 0.18f, S * 0.1f, new Color(0.85f, 0.86f, 0.88f));
                    rb.Disc(q + new Vector2(0f, 0.025f), S * 0.035f, new Color(0.35f, 0.37f, 0.4f), 10);
                }
                rb.Rect(rc + n * S * 0.2f + f * S * 0.2f, f, S * 0.2f, S * 0.12f, new Color(0.45f, 0.62f, 0.78f));
            }
            rb.Build(parent, "BRoof", 6);
        }

        // ---------------- Árbol ----------------

        static readonly Color[] TreeTints = { new Color(0.42f, 0.66f, 0.32f), new Color(0.98f, 0.72f, 0.22f), new Color(0.45f, 0.72f, 0.33f), new Color(0.24f, 0.5f, 0.3f) };

        void DrawTree2(DecorItem d)
        {
            Transform parent = staticRoot;
            Vector2 p = d.Pos;
            float sz = 0.86f * d.size;
            int kind = Mathf.Abs(d.variant) % 4;
            int version = Mathf.FloorToInt(Hash01(p, 17) * 4f);
            float rot = Hash01(p, 19) * 360f;
            // Sombra con la misma forma (la copa en negro), corrida hacia el lado de la sombra.
            var sprite = TreeArt.Get(kind, version);
            RegShadow(SpriteFactory.Make("TreeShadow", parent, sprite, new Color(0f, 0f, 0f, 0.3f), p + SunDesign * 0.18f * sz, Vector2.one * sz, 3, rot), p);
            Color tint = TreeTints[kind];
            // Un poco distinto cada árbol (más claro u oscuro), como en la naturaleza.
            tint = Color.Lerp(tint, Color.black, (Hash01(p, 23) - 0.5f) * 0.25f);
            tint.a = 1f;
            SpriteFactory.Make("Tree", parent, sprite, tint, p, Vector2.one * sz, 4, rot);
        }

        // ---------------- Valla ----------------

        void DrawFence2(DecorItem d, MapData map)
        {
            Transform parent = staticRoot;
            Vector2 p = d.Pos;
            float ang = d.rot != 0f ? d.rot : map.RoadAngleNear(p);
            Vector2 f = Geo.FromAngle(ang);
            float L = 0.9f * d.size;
            Vector2 a = p - f * L * 0.5f, b = p + f * L * 0.5f, h = new Vector2(0f, 0.05f);
            int kind = Mathf.Abs(d.variant) % FenceNames.Length;
            Color c = FenceTints[kind];
            Color dark = Color.Lerp(c, Color.black, 0.35f);
            RegShadow(Bar(parent, "FenceShadow", a + SunDesign * 0.06f, b + SunDesign * 0.06f, 0.05f, new Color(0f, 0f, 0f, 0.2f), 3), p);
            System.Action<float> posts = step =>
            {
                int m = Mathf.Max(1, Mathf.RoundToInt(L / step));
                for (int i = 0; i <= m; i++)
                {
                    Vector2 q = Vector2.Lerp(a, b, (float)i / m);
                    Bar(parent, "Post", q, q + h * 1.2f, 0.03f, dark, 5);
                }
            };
            switch (kind)
            {
                case 1:   // guardarraíl: dos bandas y postes
                    Bar(parent, "Rail", a + h * 0.8f, b + h * 0.8f, 0.022f, c, 6);
                    Bar(parent, "Rail2", a + h * 0.45f, b + h * 0.45f, 0.022f, Color.Lerp(c, Color.black, 0.15f), 6);
                    posts(0.3f);
                    break;
                case 4:   // estacas de madera
                    Bar(parent, "Rail", a + h * 0.5f, b + h * 0.5f, 0.02f, dark, 4);
                    for (float t = 0.03f; t < L; t += 0.07f)
                    {
                        Vector2 q = a + f * t;
                        Bar(parent, "Picket", q, q + h * 1.3f, 0.035f, c, 5);
                    }
                    break;
                case 5:   // tablas: panel grueso con juntas
                    Bar(parent, "Panel", a + h * 0.5f, b + h * 0.5f, 0.09f, c, 5);
                    for (float t = 0.15f; t < L; t += 0.15f) Bar(parent, "Joint", a + f * t + h * 0.08f, a + f * t + h * 0.95f, 0.012f, dark, 6);
                    break;
                case 6:   // rústica: dos travesaños y postes
                    Bar(parent, "Rail", a + h * 0.9f, b + h * 0.9f, 0.018f, c, 6);
                    Bar(parent, "Rail2", a + h * 0.45f, b + h * 0.45f, 0.018f, c, 6);
                    posts(0.3f);
                    break;
                case 7:   // hormigón
                    Bar(parent, "Wall", a + h * 0.5f, b + h * 0.5f, 0.1f, c, 5);
                    Bar(parent, "Top", a + h, b + h, 0.03f, Color.Lerp(c, Color.white, 0.3f), 6);
                    for (float t = 0.3f; t < L; t += 0.3f) Bar(parent, "Joint", a + f * t, a + f * t + h, 0.01f, dark, 6);
                    break;
                case 9:
                case 10:  // barrera a franjas
                    {
                        Color c2 = kind == 9 ? Color.white : new Color(0.12f, 0.12f, 0.12f);
                        int m = Mathf.Max(2, Mathf.RoundToInt(L / 0.12f));
                        for (int i = 0; i < m; i++)
                            Bar(parent, "Stripe", Vector2.Lerp(a, b, (float)i / m) + h * 0.6f, Vector2.Lerp(a, b, (float)(i + 1) / m) + h * 0.6f, 0.06f, i % 2 == 0 ? c : c2, 5);
                        posts(L * 0.5f);
                        break;
                    }
                default:  // malla, blanca, oscura, metal: barra con postes
                    Bar(parent, "Fence", a + h * 0.6f, b + h * 0.6f, kind == 0 ? 0.05f : 0.035f, kind == 0 ? new Color(c.r, c.g, c.b, 0.8f) : c, 4);
                    posts(kind == 0 ? 0.22f : 0.3f);
                    break;
            }
        }

        // ---------------- Coches (aparcado y en aparcamientos) ----------------

        static readonly CarArt.Model[] ParkModels = { CarArt.Model.Sedan, CarArt.Model.Hatch, CarArt.Model.Suv, CarArt.Model.Van, CarArt.Model.Truck, CarArt.Model.Bus };

        void DrawCarArt(Transform parent, Vector2 pos, float angDeg, CarArt.Model model, Color paint, float scale, int flags, bool editor)
        {
            float L = 0.46f * scale, W = 0.26f * scale;
            if (model == CarArt.Model.Van) { L = 0.52f * scale; W = 0.28f * scale; }
            else if (model == CarArt.Model.Truck) { L = 0.8f * scale; W = 0.3f * scale; }
            else if (model == CarArt.Model.Bus) { L = 1.1f * scale; W = 0.32f * scale; }
            Vector2 f = Geo.FromAngle(angDeg), n = new Vector2(-f.y, f.x);
            RegShadow(SpriteFactory.Make("CarShadow", parent, SpriteFactory.Square, new Color(0f, 0f, 0f, 0.26f), pos + SunDesign * 0.05f, new Vector2(L * 0.98f, W * 1.02f), 3, angDeg), pos);
            SpriteFactory.Make("Car", parent, CarArt.Get(model, L, W), paint, pos, new Vector2(L, L), 4, angDeg);
            float ly = W * 0.34f;
            var amber = new Color(1f, 0.62f, 0.1f);
            if ((flags & 1) != 0)
            {
                for (int s = -1; s <= 1; s += 2)
                {
                    SpriteFactory.Make("Head", parent, SpriteFactory.Circle, new Color(1f, 0.97f, 0.85f), pos + f * L * 0.48f + n * ly * s, Vector2.one * W * 0.16f, 5);
                    RegNightGlow(SpriteFactory.Make("Beam", parent, SpriteFactory.Cone, new Color(1f, 0.97f, 0.88f, 0f), pos + f * L * 0.49f + n * ly * s, new Vector2(0.9f, W * 1.35f), NightOverlayOrder + 1, angDeg - 5f * s), 0.42f);
                    RegNightGlow(SpriteFactory.Make("Tail", parent, SpriteFactory.Soft, new Color(1f, 0.1f, 0.08f, 0f), pos - f * L * 0.5f + n * ly * s, Vector2.one * 0.11f, NightOverlayOrder + 1), 0.3f);
                }
            }
            for (int s = -1; s <= 1; s += 2)
            {
                bool on = (flags & 2) != 0 || (s > 0 && (flags & 4) != 0) || (s < 0 && (flags & 8) != 0);
                if (!on) continue;
                foreach (float e in new[] { 0.47f, -0.47f })
                {
                    Vector2 q = pos + f * L * e + n * W * 0.48f * s;
                    AddBlinker(SpriteFactory.Make("Blink", parent, SpriteFactory.Circle, amber, q, Vector2.one * W * 0.16f, NightOverlayOrder + 2), 0.65f);
                    AddBlinker(SpriteFactory.Make("BlinkHalo", parent, SpriteFactory.Soft, new Color(amber.r, amber.g, amber.b, 0.55f), q, Vector2.one * W * 0.6f, NightOverlayOrder + 2), 0.65f);
                }
            }
        }

        Color CarPaint(int a, int b, Vector2 seed)
        {
            System.Func<int, Color> pal = i => SpriteFactory.Hex(CarColorHex[(i - 1) % CarColorHex.Length]);
            Color ca = a > 0 ? pal(a) : SpriteFactory.Hex(CarColorHex[Mathf.FloorToInt(Hash01(seed, 3) * CarColorHex.Length) % CarColorHex.Length]);
            if (b <= 0 || a <= 0) return ca;
            return Color.Lerp(ca, pal(b), Hash01(seed, 5));   // entre el color A y el B
        }

        CarArt.Model CarModelOf(int variant, Vector2 seed)
        {
            int v = Mathf.Abs(variant) % CarModelNames.Length;
            if (v > 0) return ParkModels[v - 1];
            return ParkModels[Mathf.FloorToInt(Hash01(seed, 7) * 4f) % 4];   // aleatorio entre los coches
        }

        void DrawParkedCar2(DecorItem d, MapData map)
        {
            Vector2 p = d.Pos;
            // «Uso»: al jugar, el coche está con esa probabilidad (en el editor siempre se ve).
            if (!curEditor && d.presence < 0.999f && Hash01(p, 11) >= d.presence) return;
            float ang = d.rot != 0f ? d.rot : map.RoadAngleNear(p);
            Color paint = CarPaint(d.color, d.color2, p);
            if (RoadData.ValidHex(d.hexA)) paint = RoadData.ValidHex(d.hexB) ? Color.Lerp(SpriteFactory.Hex(d.hexA), SpriteFactory.Hex(d.hexB), Hash01(p, 5)) : SpriteFactory.Hex(d.hexA);
            DrawCarArt(staticRoot, p, ang, CarModelOf(d.variant, p), paint, d.size, d.carFlags, curEditor);
        }

        // ---------------- Aparcamiento ----------------

        void DrawParking2(DecorItem d)
        {
            Transform parent = staticRoot;
            Vector2 p = d.Pos;
            Vector2 half = new Vector2(Mathf.Abs(d.x2), Mathf.Abs(d.y2)) * d.size;
            bool alongX = half.x >= half.y;
            Vector2 u = alongX ? Vector2.right : Vector2.up, v = alongX ? Vector2.up : Vector2.right;
            float Llong = (alongX ? half.x : half.y) * 2f, Lshort = (alongX ? half.y : half.x) * 2f;
            float groundAng = 0f;
            if (d.rot != 0f)
            {
                // Girado (importado del original): las plazas van a lo largo del giro (semilargo x2, semifondo y2).
                u = Geo.FromAngle(d.rot); v = new Vector2(u.y, -u.x);   // filas a la derecha del giro, como el original
                Llong = half.x * 2f; Lshort = half.y * 2f;
                groundAng = d.rot;
            }
            int rows = d.twoWay ? 2 : 1;
            int type = Mathf.Abs(d.variant) % ParkingNames.Length;
            int spaces = Mathf.Max(1, d.spaces);
            float sw = Llong / spaces, depth = Lshort / rows;
            Color line = RoadData.ValidHex(d.hexA) ? SpriteFactory.Hex(d.hexA) : (d.color == 0 ? new Color(1f, 1f, 1f, 0.9f) : ObjCol(d.color));
            if (!d.invisible)
            {
                SpriteFactory.Make("Parking", parent, SpriteFactory.Square, new Color(0.5f, 0.51f, 0.53f), p, d.rot != 0f ? new Vector2(Llong, Lshort) : half * 2f, -15, groundAng);
                var mb = new MeshBatch();
                for (int r = 0; r < rows; r++)
                {
                    Vector2 rowC = p + v * (-Lshort * 0.5f + depth * (r + 0.5f));
                    float sgn = r == 0 ? 1f : -1f;
                    for (int i = 0; i <= spaces; i++)
                    {
                        Vector2 q = rowC + u * (-Llong * 0.5f + sw * i);
                        if (type == 2) mb.Rect(q + v * sgn * (depth * 0.4f), v, depth * 0.2f, 0.035f, line);   // en línea: marcas cortas
                        else if (type == 1)
                        {
                            Vector2 dir = (v * sgn + u).normalized;
                            mb.Rect(q, dir, depth * 1.2f, 0.035f, line);
                        }
                        else mb.Rect(q, v, depth * 0.94f, 0.035f, line);
                    }
                    // Línea del fondo de la fila (con dos filas, como el original: la compartida, en el medio).
                    mb.Rect(rowC + v * sgn * depth * (rows == 2 ? 0.5f : -0.47f), u, Llong, 0.035f, line);
                }
                mb.Build(parent, "ParkLines", -14);
            }
            // Coches: en cada plaza con probabilidad «Uso».
            float scale = Mathf.Clamp(Mathf.Min(depth / 0.56f, sw / 0.34f), 0.5f, 1.4f);
            if (type == 2) scale = Mathf.Clamp(Mathf.Min(sw / 0.56f, depth / 0.34f), 0.5f, 1.4f);
            for (int r = 0; r < rows; r++)
            {
                Vector2 rowC = p + v * (-Lshort * 0.5f + depth * (r + 0.5f));
                float sgn = r == 0 ? 1f : -1f;
                for (int i = 0; i < spaces; i++)
                {
                    Vector2 q = rowC + u * (-Llong * 0.5f + sw * (i + 0.5f));
                    if (Hash01(q, 13) >= d.usage) continue;
                    float baseAng = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg * 1f;
                    float ang = type == 2 ? Mathf.Atan2(u.y, u.x) * Mathf.Rad2Deg + (r == 0 ? 0f : 180f)
                        : (type == 1 ? baseAng + (sgn > 0 ? -45f : 135f) : baseAng + (sgn > 0 ? 0f : 180f));
                    DrawCarArt(parent, q, ang, CarModelOf(0, q), CarPaint(0, 0, q), scale * 0.95f, 0, curEditor);
                }
            }
        }

        // ---------------- Material ----------------

        void DrawMaterial2(DecorItem d)
        {
            Transform parent = staticRoot;
            Vector2 p = d.Pos;
            int kind = Mathf.Abs(d.variant) % MaterialColors.Length;
            Color c = RoadData.ValidHex(d.hexA) ? SpriteFactory.Hex(d.hexA)
                : (MaterialColors[kind] == "" ? (curEditor ? asphalt : PlayAsphalt()) : SpriteFactory.Hex(MaterialColors[kind]));   // asfalto = como la calzada
            float alpha = 1f - Mathf.Clamp(d.transparency, 0f, 0.9f);
            c.a = alpha;
            int order = d.overLane ? RoadOrder(0) + 1 : -15;
            // Contorno: las 4 esquinas (importado del original, puede estar deformado) o el rectángulo girado.
            var ring = new List<Vector2>();
            if (d.pts != null && d.pts.Count >= 3) for (int i = 0; i < d.pts.Count; i++) ring.Add(p + d.pts[i] * d.size);
            else
            {
                Vector2 half = new Vector2(Mathf.Abs(d.x2), Mathf.Abs(d.y2)) * d.size;
                ring.Add(p + new Vector2(-half.x, -half.y)); ring.Add(p + new Vector2(half.x, -half.y));
                ring.Add(p + new Vector2(half.x, half.y)); ring.Add(p + new Vector2(-half.x, half.y));
            }
            var fill = new MeshBatch();
            var tris = Triangulate(ring);
            for (int i = 0; i + 2 < tris.Count; i += 3) fill.Tri(ring[tris[i]], ring[tris[i + 1]], ring[tris[i + 2]], c);
            fill.Build(parent, "Material", order);
            var mb = new MeshBatch();
            Rect bb = Bounds(ring);
            if (kind == 2)
                for (float yy = bb.yMin + 0.3f; yy < bb.yMax - 0.1f; yy += 0.45f)
                    ClipStripe(mb, ring, new Vector2(bb.xMin, yy), Vector2.right, bb.width, 0.03f, new Color(1f, 1f, 1f, 0.25f * alpha));
            if (d.bars > 0)
            {
                // Rayas como el original: finas, paralelas al primer lado del material (así salen según cómo se puso),
                // más juntas cuantas más barras; con borde del mismo color.
                Color mc = ObjCol(d.color2); mc.a = 0.9f * alpha;
                float spacing = d.bars == 1 ? 0.34f : (d.bars == 2 ? 0.22f : 0.13f);
                // Rectángulo del editor: la dirección la elige «Rayas» (giro); importado: la del primer lado.
                Vector2 dir = (d.pts != null && d.pts.Count >= 3) ? ring[1] - ring[0] : Geo.FromAngle(d.rot);
                dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector2.right;
                Vector2 nrm = new Vector2(-dir.y, dir.x);
                float lo = float.MaxValue, hi = float.MinValue, a0 = float.MaxValue, a1 = float.MinValue;
                foreach (var q in ring)
                {
                    float u = Vector2.Dot(q - ring[0], nrm), w = Vector2.Dot(q - ring[0], dir);
                    lo = Mathf.Min(lo, u); hi = Mathf.Max(hi, u); a0 = Mathf.Min(a0, w); a1 = Mathf.Max(a1, w);
                }
                for (float k = lo + spacing * 0.5f; k < hi; k += spacing)
                    ClipStripe(mb, ring, ring[0] + nrm * k + dir * a0, dir, a1 - a0, 0.035f, mc);
                for (int i = 0; i < ring.Count; i++) Seg(mb, ring[i], ring[(i + 1) % ring.Count], 0.04f, mc);
            }
            mb.Build(parent, "MaterialMarks", order + 1);
        }

        /// <summary>Raya de «len» desde «from» en la dirección «dir», solo en los tramos dentro del contorno.</summary>
        static void ClipStripe(MeshBatch mb, List<Vector2> ring, Vector2 from, Vector2 dir, float len, float w, Color col)
        {
            const float step = 0.025f;
            bool inRun = false; Vector2 runA = from;
            for (float t = -step; t <= len + step; t += step)
            {
                Vector2 q = from + dir * t;
                bool ins = Inside(ring, q);
                if (ins && !inRun) { runA = q; inRun = true; }
                else if (!ins && inRun) { if ((q - runA).sqrMagnitude > 0.004f) Seg(mb, runA, q - dir * step, w, col); inRun = false; }
            }
        }

        // ---------------- Pegatinas ----------------

        void DrawSticker2(DecorItem d)
        {
            Transform parent = staticRoot;
            Vector2 p = d.Pos;
            float s = d.size, a = d.rot;
            int kind = (Mathf.Abs(d.variant) - 6) % Sticker2Names.Length;
            Color c = RoadData.ValidHex(d.hexA) ? SpriteFactory.Hex(d.hexA) : (d.color == 0 ? new Color(1f, 1f, 1f, 0.92f) : ObjCol(d.color));
            Vector2 f = Geo.FromAngle(a), n = new Vector2(-f.y, f.x);
            var mb = new MeshBatch();
            System.Action<int, float, Color, float> speckle = (count, rad, col, spread) =>
            {
                for (int i = 0; i < count; i++)
                {
                    Vector2 o = new Vector2(Hash01(p, i * 2) - 0.5f, Hash01(p, i * 2 + 1) - 0.5f) * spread * s;
                    mb.Disc(p + o, rad * s * (0.6f + 0.8f * Hash01(p, i + 50)), col, 8);
                }
            };
            System.Action<Vector2, Vector2, float> arrow = (from, to, w) =>
            {
                Vector2 dv = to - from; float len = dv.magnitude; Vector2 u = dv / Mathf.Max(0.001f, len), r = new Vector2(-u.y, u.x);
                mb.Rect(from + dv * 0.35f, u, len * 0.7f, w * s, c);
                mb.Tri(to, to - u * 0.22f * s + r * 0.13f * s, to - u * 0.22f * s - r * 0.13f * s, c);
            };
            switch (kind)
            {
                case 0: speckle(40, 0.035f, new Color(0.35f, 0.55f, 0.25f, 0.85f), 0.9f); break;                    // césped
                case 1: speckle(14, 0.12f, new Color(0.1f, 0.1f, 0.11f, 0.45f), 0.6f); break;                       // mancha
                case 2: speckle(7, 0.08f, new Color(0.1f, 0.1f, 0.11f, 0.4f), 0.35f); break;                        // mancha pequeña
                case 3: speckle(10, 0.13f, new Color(0.1f, 0.1f, 0.11f, 0.2f), 0.6f); break;                        // mancha suave
                case 4: speckle(8, 0.14f, new Color(0.3f, 0.3f, 0.32f, 0.5f), 0.5f); speckle(20, 0.02f, new Color(0.15f, 0.15f, 0.16f, 0.6f), 0.6f); break;
                case 5:                                                                                             // grietas
                    for (int i = 0; i < 6; i++)
                    {
                        Vector2 q0 = p + new Vector2(Hash01(p, i) - 0.5f, Hash01(p, i + 9) - 0.5f) * 0.7f * s;
                        Vector2 q1 = q0 + Geo.FromAngle(Hash01(p, i + 20) * 360f) * 0.25f * s;
                        mb.Rect((q0 + q1) * 0.5f, (q1 - q0).normalized, (q1 - q0).magnitude, 0.015f * s, new Color(0.12f, 0.12f, 0.13f, 0.7f));
                    }
                    break;
                case 6: arrow(p - f * 0.3f * s, p + f * 0.3f * s, 0.08f); break;
                case 7:
                case 19:                                                                                            // discapacitados
                    {
                        Color wc = kind == 19 ? Color.white : c;
                        if (kind == 19) mb.Rect(p, f, 0.62f * s, 0.62f * s, new Color(0.15f, 0.4f, 0.8f, 0.95f));
                        mb.Disc(p + n * 0.18f * s, 0.05f * s, wc, 10);
                        mb.Rect(p + n * 0.02f * s, n, 0.24f * s, 0.06f * s, wc);
                        mb.Rect(p - n * 0.08f * s + f * 0.08f * s, f, 0.18f * s, 0.05f * s, wc);
                        mb.Annulus(p - n * 0.12f * s, 0.11f * s, 0.15f * s, wc, 20);
                        break;
                    }
                case 8:                                                                                             // rombo
                    mb.Quad(p + f * 0.35f * s, p + n * 0.15f * s, p - f * 0.35f * s, p - n * 0.15f * s, c);
                    mb.Quad(p + f * 0.25f * s, p + n * 0.09f * s, p - f * 0.25f * s, p - n * 0.09f * s, new Color(0.45f, 0.45f, 0.47f, 1f));
                    break;
                case 9:                                                                                             // paso a nivel: X con R R
                    mb.Rect(p, (f + n).normalized, 0.75f * s, 0.07f * s, c);
                    mb.Rect(p, (f - n).normalized, 0.75f * s, 0.07f * s, c);
                    mb.Rect(p + n * 0.38f * s, f, 0.5f * s, 0.06f * s, c);
                    mb.Rect(p - n * 0.38f * s, f, 0.5f * s, 0.06f * s, c);
                    break;
                case 10:                                                                                            // ceda (triángulo)
                    mb.Tri(p - f * 0.3f * s, p + f * 0.3f * s + n * 0.2f * s, p + f * 0.3f * s - n * 0.2f * s, c);
                    mb.Tri(p - f * 0.18f * s, p + f * 0.22f * s + n * 0.12f * s, p + f * 0.22f * s - n * 0.12f * s, new Color(0.45f, 0.45f, 0.47f, 1f));
                    break;
                case 11:                                                                                            // bicicleta
                    mb.Annulus(p - f * 0.18f * s, 0.09f * s, 0.12f * s, c, 20);
                    mb.Annulus(p + f * 0.18f * s, 0.09f * s, 0.12f * s, c, 20);
                    mb.Rect(p + n * 0.05f * s, (f + n * 0.4f).normalized, 0.34f * s, 0.035f * s, c);
                    mb.Disc(p + n * 0.2f * s, 0.045f * s, c, 10);
                    break;
                case 12: arrow(p - f * 0.45f * s, p + f * 0.45f * s, 0.06f); break;                                 // flecha larga
                case 13: arrow(p - f * 0.4f * s, p + f * 0.15f * s, 0.06f); arrow(p + f * 0.0f * s, p + f * 0.4f * s + n * 0.15f * s, 0.05f); break;
                case 14: arrow(p - f * 0.4f * s, p + f * 0.15f * s, 0.06f); arrow(p + f * 0.0f * s, p + f * 0.4f * s - n * 0.15f * s, 0.05f); break;
                case 15: arrow(p - f * 0.4f * s, p + f * 0.4f * s, 0.06f); arrow(p, p + f * 0.35f * s + n * 0.2f * s, 0.05f); break;
                case 16: arrow(p, p + f * 0.4f * s, 0.06f); arrow(p, p - f * 0.4f * s, 0.06f); break;               // doble sentido
                case 17: arrow(p - f * 0.35f * s - n * 0.1f * s, p + n * 0.05f * s, 0.06f); arrow(p + n * 0.05f * s, p + f * 0.35f * s + n * 0.2f * s, 0.06f); break;
                case 18: arrow(p - f * 0.3f * s, p + f * 0.35f * s, 0.08f); mb.Tri(p + f * 0.1f * s, p - f * 0.12f * s + n * 0.13f * s, p - f * 0.12f * s - n * 0.13f * s, c); break;
                case 20:                                                                                            // adoquines
                    for (int yy = -2; yy <= 2; yy++)
                        for (int xx = -2; xx <= 2; xx++)
                        {
                            Vector2 q = p + f * (xx * 0.13f + (yy % 2 == 0 ? 0f : 0.065f)) * s + n * yy * 0.08f * s;
                            mb.Rect(q, f, 0.115f * s, 0.065f * s, new Color(0.62f, 0.42f, 0.35f, 0.95f));
                        }
                    break;
                case 21: speckle(70, 0.018f, new Color(0.85f, 0.85f, 0.82f, 0.9f), 0.7f); break;                   // gravilla
                case 22:                                                                                            // gravilla en línea
                    for (int i = 0; i < 40; i++)
                    {
                        Vector2 q = p + f * (Hash01(p, i) - 0.5f) * 0.9f * s + n * (Hash01(p, i + 70) - 0.5f) * 0.12f * s;
                        mb.Disc(q, 0.016f * s, new Color(0.85f, 0.85f, 0.82f, 0.9f), 8);
                    }
                    break;
                case 24: case 25: case 26: case 27: case 28: case 29: case 30:                                     // flechas del original
                    LaneArrow(mb, p, f, ArrowStickerMoves[kind - 24], c, s);
                    break;
                default: speckle(18, 0.017f, new Color(0.85f, 0.85f, 0.82f, 0.85f), 0.8f); break;                  // gravilla suelta
            }
            mb.Build(parent, "Sticker", 4);
        }
    }
}
