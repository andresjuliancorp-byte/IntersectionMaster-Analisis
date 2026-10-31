using System;
using System.Collections.Generic;
using UnityEngine;

namespace IntersectionMaster.UI
{
    /// <summary>
    /// Iconos de las herramientas del editor, dibujados por código (blanco sobre transparente, 96x96) con formas simples.
    /// No se usa ninguna imagen externa: todos los pictogramas son originales.
    /// </summary>
    public static class IconKit
    {
        const int N = 96;
        static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();
        static Sprite circle;

        /// <summary>Disco blanco suave para los botones redondos.</summary>
        public static Sprite Circle
        {
            get
            {
                if (circle == null)
                {
                    var c = new Canvas();
                    c.Disc(48, 48, 46);
                    circle = c.ToSprite();
                }
                return circle;
            }
        }

        static void ArrowS(Canvas c) { c.Line(48, 10, 48, 60, 10); c.Tri(30, 58, 66, 58, 48, 86); }
        static void ArrowL(Canvas c) { c.Line(54, 10, 54, 48, 10); c.Line(56, 48, 30, 48, 10); c.Tri(32, 30, 32, 66, 8, 48); }
        static void ArrowR(Canvas c) { c.Line(42, 10, 42, 48, 10); c.Line(40, 48, 66, 48, 10); c.Tri(64, 30, 64, 66, 88, 48); }

        public static Sprite Get(string name)
        {
            Sprite sp;
            if (cache.TryGetValue(name, out sp)) return sp;
            if (name.StartsWith("sign_"))
            {
                int si;
                int.TryParse(name.Substring(5), out si);
                sp = BuildSign(IntersectionMaster.Data.SignCatalog.Defs[IntersectionMaster.Data.SignCatalog.Clamp(si)]);
                cache[name] = sp;
                return sp;
            }
            var c = new Canvas();
            Draw(name, c);
            sp = c.ToSprite();
            cache[name] = sp;
            return sp;
        }

        // ---------- Señales de tráfico en color (catálogo de Decoración) ----------

        static Canvas Shape(IntersectionMaster.Data.SignCatalog.Shape sh, bool inner)
        {
            var c = new Canvas();
            switch (sh)
            {
                case IntersectionMaster.Data.SignCatalog.Shape.Circle: c.Disc(48, 48, inner ? 36 : 44); break;
                case IntersectionMaster.Data.SignCatalog.Shape.Triangle:
                    if (inner) c.Tri(48, 76, 17, 19, 79, 19); else c.Tri(48, 92, 3, 10, 93, 10); break;
                case IntersectionMaster.Data.SignCatalog.Shape.InvTriangle:
                    if (inner) c.Tri(17, 77, 79, 77, 48, 20); else c.Tri(3, 86, 93, 86, 48, 4); break;
                case IntersectionMaster.Data.SignCatalog.Shape.Square:
                    if (inner) c.Box(12, 12, 84, 84, 6); else c.Box(5, 5, 91, 91, 10); break;
                case IntersectionMaster.Data.SignCatalog.Shape.Octagon: c.Poly(48, 48, inner ? 39 : 45, 8, 22.5f); break;
                case IntersectionMaster.Data.SignCatalog.Shape.Diamond: c.Poly(48, 48, inner ? 33 : 45, 4, 0f); break;
            }
            return c;
        }

        static Sprite BuildSign(IntersectionMaster.Data.SignCatalog.Def def)
        {
            var outer = Shape(def.shape, false);
            var inner = Shape(def.shape, true);
            var ink = new Canvas();
            if (!string.IsNullOrEmpty(def.symbol)) Draw(def.symbol, ink);
            if (!string.IsNullOrEmpty(def.text))
            {
                string t = def.text;
                if (t.Length >= 4) ink.Text(t, 48, 48, 22, 13, 5);
                else if (t.Length == 3) ink.Text(t, 48, 48, 30, 13, 6);
                else if (t.Length == 2) ink.Text(t, 48, 48, 36, 17, 7);
                else ink.Text(t, 48, 48, 46, 26, 9);
            }
            var slash = new Canvas();
            if (def.slash) slash.Line(24, 72, 72, 24, 8);
            // Los triángulos tienen el centro óptico más abajo (o más arriba si están invertidos) y menos sitio.
            float dy = def.shape == IntersectionMaster.Data.SignCatalog.Shape.Triangle ? -7f : (def.shape == IntersectionMaster.Data.SignCatalog.Shape.InvTriangle ? 9f : 0f);
            float k = def.shape == IntersectionMaster.Data.SignCatalog.Shape.Triangle || def.shape == IntersectionMaster.Data.SignCatalog.Shape.InvTriangle ? 0.72f : 1f;
            Color bg = def.bg, border = def.border, inkC = def.ink, red = new Color32(214, 40, 40, 255);
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    int i = y * N + x;
                    float ao = outer.a[i];
                    if (ao <= 0f) { px[i] = new Color32(0, 0, 0, 0); continue; }
                    float ai = inner.a[i];
                    int sx = Mathf.RoundToInt(48f + (x - 48f) / k), sy = Mathf.RoundToInt(48f + (y - 48f - dy) / k);
                    float aInk = sx >= 0 && sy >= 0 && sx < N && sy < N ? ink.a[sy * N + sx] * ai : 0f;
                    float aSl = slash.a[i] * ai;
                    Color c = Color.Lerp(border, bg, ai);
                    c = Color.Lerp(c, inkC, aInk);
                    c = Color.Lerp(c, red, aSl);
                    c.a = ao;
                    px[i] = c;
                }
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
        }

        // Letras y números con trazos rectos (unidad 0..1; y hacia arriba).
        static readonly Dictionary<char, float[]> Strokes = new Dictionary<char, float[]>
        {
            { '0', new float[] { 0,0,1,0, 1,0,1,1, 1,1,0,1, 0,1,0,0 } },
            { '1', new float[] { 0.5f,0,0.5f,1, 0.5f,1,0.2f,0.78f } },
            { '2', new float[] { 0,1,1,1, 1,1,1,0.5f, 1,0.5f,0,0.5f, 0,0.5f,0,0, 0,0,1,0 } },
            { '3', new float[] { 0,1,1,1, 1,1,1,0, 1,0,0,0, 0.15f,0.5f,1,0.5f } },
            { '4', new float[] { 0,1,0,0.45f, 0,0.45f,1,0.45f, 0.8f,1,0.8f,0 } },
            { '5', new float[] { 1,1,0,1, 0,1,0,0.5f, 0,0.5f,1,0.5f, 1,0.5f,1,0, 1,0,0,0 } },
            { '6', new float[] { 1,1,0,1, 0,1,0,0, 0,0,1,0, 1,0,1,0.5f, 1,0.5f,0,0.5f } },
            { '7', new float[] { 0,1,1,1, 1,1,0.35f,0 } },
            { '8', new float[] { 0,0,1,0, 1,0,1,1, 1,1,0,1, 0,1,0,0, 0,0.5f,1,0.5f } },
            { '9', new float[] { 1,0.5f,0,0.5f, 0,0.5f,0,1, 0,1,1,1, 1,1,1,0, 1,0,0,0 } },
            { 'S', new float[] { 1,1,0,1, 0,1,0,0.5f, 0,0.5f,1,0.5f, 1,0.5f,1,0, 1,0,0,0 } },
            { 'T', new float[] { 0,1,1,1, 0.5f,1,0.5f,0 } },
            { 'O', new float[] { 0,0,1,0, 1,0,1,1, 1,1,0,1, 0,1,0,0 } },
            { 'P', new float[] { 0,0,0,1, 0,1,1,1, 1,1,1,0.5f, 1,0.5f,0,0.5f } },
            { 'H', new float[] { 0,0,0,1, 1,0,1,1, 0,0.5f,1,0.5f } },
            { 'I', new float[] { 0.5f,0,0.5f,1, 0.2f,0,0.8f,0, 0.2f,1,0.8f,1 } },
            { 'A', new float[] { 0,0,0.5f,1, 0.5f,1,1,0, 0.2f,0.4f,0.8f,0.4f } },
        };

        // ---------- Lienzo con formas suavizadas ----------
        class Canvas
        {
            public readonly float[] a = new float[N * N];

            static float Cov(float d) { return Mathf.Clamp01(0.5f - d); }

            void Put(int x, int y, float v) { int i = y * N + x; if (v > a[i]) a[i] = v; }

            public void Disc(float cx, float cy, float r)
            {
                for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
                    Put(x, y, Cov(Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) - r));
            }

            public void Ring(float cx, float cy, float r, float w)
            {
                for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
                    Put(x, y, Cov(Mathf.Abs(Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) - r) - w * 0.5f));
            }

            public void Box(float x0, float y0, float x1, float y1, float rad = 0f)
            {
                float cx = (x0 + x1) * 0.5f, cy = (y0 + y1) * 0.5f, hx = (x1 - x0) * 0.5f - rad, hy = (y1 - y0) * 0.5f - rad;
                for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
                {
                    float dx = Mathf.Max(Mathf.Abs(x - cx) - hx, 0f), dy = Mathf.Max(Mathf.Abs(y - cy) - hy, 0f);
                    Put(x, y, Cov(Mathf.Sqrt(dx * dx + dy * dy) - rad));
                }
            }

            public void Line(float x0, float y0, float x1, float y1, float w)
            {
                float vx = x1 - x0, vy = y1 - y0, l2 = Mathf.Max(0.0001f, vx * vx + vy * vy);
                for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
                {
                    float t = Mathf.Clamp01(((x - x0) * vx + (y - y0) * vy) / l2);
                    float px = x0 + vx * t, py = y0 + vy * t;
                    Put(x, y, Cov(Mathf.Sqrt((x - px) * (x - px) + (y - py) * (y - py)) - w * 0.5f));
                }
            }

            public void Arc(float cx, float cy, float r, float a0, float a1, float w)
            {
                float prevX = cx + Mathf.Cos(a0 * Mathf.Deg2Rad) * r, prevY = cy + Mathf.Sin(a0 * Mathf.Deg2Rad) * r;
                for (int i = 1; i <= 16; i++)
                {
                    float a = Mathf.Lerp(a0, a1, i / 16f) * Mathf.Deg2Rad;
                    float x = cx + Mathf.Cos(a) * r, y = cy + Mathf.Sin(a) * r;
                    Line(prevX, prevY, x, y, w);
                    prevX = x; prevY = y;
                }
            }

            /// <summary>Polígono regular (abanico de triángulos).</summary>
            public void Poly(float cx, float cy, float r, int sides, float rotDeg)
            {
                for (int i = 0; i < sides; i++)
                {
                    float a0 = (rotDeg + i * 360f / sides) * Mathf.Deg2Rad, a1 = (rotDeg + (i + 1) * 360f / sides) * Mathf.Deg2Rad;
                    Tri(cx, cy, cx + Mathf.Cos(a0) * r, cy + Mathf.Sin(a0) * r, cx + Mathf.Cos(a1) * r, cy + Mathf.Sin(a1) * r);
                }
            }

            /// <summary>Texto con trazos rectos centrado en (cx, cy): h = alto de letra, w = ancho, sw = grosor.</summary>
            public void Text(string s, float cx, float cy, float h, float w, float sw)
            {
                float gap = w * 0.38f;
                float total = s.Length * w + (s.Length - 1) * gap;
                float x0 = cx - total * 0.5f, y0 = cy - h * 0.5f;
                for (int k = 0; k < s.Length; k++)
                {
                    float[] st;
                    if (!Strokes.TryGetValue(s[k], out st)) continue;
                    float ox = x0 + k * (w + gap);
                    for (int j = 0; j + 3 < st.Length; j += 4)
                        Line(ox + st[j] * w, y0 + st[j + 1] * h, ox + st[j + 2] * w, y0 + st[j + 3] * h, sw);
                }
            }

            public void Tri(float x0, float y0, float x1, float y1, float x2, float y2)
            {
                float area = (x1 - x0) * (y2 - y0) - (y1 - y0) * (x2 - x0);
                float sg = area >= 0f ? 1f : -1f;
                for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
                {
                    float e0 = Edge(x, y, x0, y0, x1, y1) * -sg, e1 = Edge(x, y, x1, y1, x2, y2) * -sg, e2 = Edge(x, y, x2, y2, x0, y0) * -sg;
                    Put(x, y, Cov(-Mathf.Min(e0, Mathf.Min(e1, e2))));
                }
            }

            static float Edge(float px, float py, float ax, float ay, float bx, float by)
            {
                float vx = bx - ax, vy = by - ay;
                float l = Mathf.Max(0.0001f, Mathf.Sqrt(vx * vx + vy * vy));
                return ((px - ax) * vy - (py - ay) * vx) / l;
            }

            public Sprite ToSprite()
            {
                var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
                var px = new Color32[N * N];
                for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a[i]) * 255f));
                tex.SetPixels32(px);
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Bilinear;
                tex.Apply();
                return Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
            }
        }

        // ---------- Pictogramas ----------
        static void Draw(string n, Canvas c)
        {
            switch (n)
            {
                case "menu": c.Box(22, 62, 74, 70, 3); c.Box(22, 44, 74, 52, 3); c.Box(22, 26, 74, 34, 3); break;
                case "play": c.Tri(34, 24, 34, 72, 74, 48); break;
                case "center": c.Ring(48, 48, 16, 6); c.Disc(48, 48, 6); c.Line(48, 14, 48, 26, 6); c.Line(48, 70, 48, 82, 6); c.Line(14, 48, 26, 48, 6); c.Line(70, 48, 82, 48, 6); break;
                case "mapset": c.Tri(14, 40, 50, 66, 86, 40); c.Tri(14, 40, 50, 14, 86, 40); c.Ring(70, 26, 12, 7); c.Disc(70, 26, 5); break;
                case "layer": c.Tri(48, 74, 18, 58, 78, 58); c.Tri(18, 46, 78, 46, 48, 30); c.Line(18, 34, 48, 20, 6); c.Line(78, 34, 48, 20, 6); break;
                case "table": break;
                case "select": c.Tri(30, 76, 30, 22, 72, 52); c.Line(46, 46, 66, 24, 9); break;
                case "node": c.Ring(48, 48, 20, 8); c.Disc(48, 48, 7); break;
                case "restrict": c.Box(14, 36, 58, 66, 4); c.Box(58, 36, 82, 54, 4); c.Disc(30, 34, 8); c.Disc(68, 34, 8); break;
                case "straight": c.Line(24, 72, 72, 24, 10); break;
                case "curve": c.Arc(24, 24, 44, 0, 90, 10); break;
                case "vehicle": c.Box(14, 40, 82, 58, 6); c.Box(30, 56, 66, 70, 5); c.Disc(30, 38, 8); c.Disc(66, 38, 8); break;
                case "build":
                case "dig": c.Line(20, 28, 64, 28, 12); c.Box(22, 36, 56, 52, 4); c.Line(50, 56, 72, 74, 8); c.Line(72, 74, 82, 56, 8); c.Tri(82, 56, 70, 46, 88, 46); break;
                case "tree": c.Tri(48, 82, 22, 42, 74, 42); c.Tri(48, 66, 28, 28, 68, 28); c.Box(44, 12, 52, 30, 1); break;
                case "building": c.Tri(48, 78, 14, 50, 82, 50); c.Box(24, 20, 72, 52, 2); break;
                case "parked": c.Box(18, 18, 78, 78, 8); c.Box(36, 32, 44, 64, 1); c.Ring(53, 56, 9, 6); break;
                case "lamp": c.Tri(24, 30, 56, 30, 44, 72); c.Line(44, 72, 70, 72, 8); c.Line(70, 72, 70, 24, 6); break;
                case "smoke": c.Disc(34, 38, 14); c.Disc(54, 44, 18); c.Disc(68, 34, 11); c.Disc(48, 64, 14); break;
                case "hedge": c.Box(12, 30, 84, 62, 12); break;
                case "fence": c.Line(14, 38, 82, 38, 6); c.Line(14, 58, 82, 58, 6); for (int i = 0; i < 5; i++) c.Box(18 + i * 15, 26, 25 + i * 15, 70, 2); break;
                case "park": c.Box(16, 16, 80, 80, 10); c.Disc(36, 56, 12); c.Disc(60, 38, 12); break;
                case "light": c.Disc(48, 48, 14); for (int i = 0; i < 8; i++) { float a = i * 45f * Mathf.Deg2Rad; c.Line(48 + Mathf.Cos(a) * 24, 48 + Mathf.Sin(a) * 24, 48 + Mathf.Cos(a) * 36, 48 + Mathf.Sin(a) * 36, 5); } break;
                case "signal": c.Box(32, 12, 64, 84, 8); break;
                case "stop": c.Disc(48, 48, 38); break;
                case "yield": c.Tri(48, 18, 12, 76, 84, 76); break;
                case "priority": c.Tri(48, 84, 12, 48, 48, 12); c.Tri(48, 84, 84, 48, 48, 12); break;
                case "noleft": c.Ring(48, 48, 32, 8); c.Line(34, 48, 62, 48, 8); c.Line(34, 48, 44, 60, 8); c.Line(34, 48, 44, 36, 8); break;
                case "noright": c.Ring(48, 48, 32, 8); c.Line(34, 48, 62, 48, 8); c.Line(62, 48, 52, 60, 8); c.Line(62, 48, 52, 36, 8); break;
                case "nostraight": c.Ring(48, 48, 32, 8); c.Line(48, 32, 48, 64, 8); c.Line(48, 64, 38, 54, 8); c.Line(48, 64, 58, 54, 8); break;
                case "speed": c.Ring(48, 48, 34, 10); c.Box(36, 40, 44, 56, 1); c.Box(52, 40, 60, 56, 1); break;
                case "entry": c.Line(14, 48, 62, 48, 10); c.Tri(60, 26, 60, 70, 86, 48); break;
                case "exit": c.Line(34, 48, 82, 48, 10); c.Tri(36, 26, 36, 70, 10, 48); break;
                case "round": c.Ring(48, 48, 28, 12); c.Line(48, 76, 48, 90, 10); break;
                case "crosswalk": for (int i = 0; i < 5; i++) c.Box(14 + i * 15, 24, 24 + i * 15, 72, 1); break;
                case "oneway": c.Line(16, 48, 70, 48, 12); c.Tri(64, 22, 64, 74, 88, 48); break;
                case "lanes": c.Line(24, 14, 24, 82, 7); c.Line(72, 14, 72, 82, 7); c.Line(48, 18, 48, 34, 6); c.Line(48, 46, 48, 62, 6); c.Line(48, 72, 48, 84, 6); break;
                case "turns": c.Line(34, 14, 34, 54, 9); c.Line(34, 54, 66, 54, 9); c.Tri(62, 36, 62, 72, 86, 54); break;
                case "erase": c.Line(24, 24, 72, 72, 12); c.Line(24, 72, 72, 24, 12); break;
                case "lt_vertical": c.Box(36, 12, 60, 84, 6); c.Disc(48, 70, 7); c.Disc(48, 48, 7); c.Disc(48, 26, 7); break;
                case "lt_horizontal": c.Line(14, 20, 14, 70, 6); c.Line(14, 70, 50, 70, 6); c.Box(46, 58, 86, 82, 6); break;
                case "lt_arrows": c.Box(32, 12, 64, 84, 6); c.Tri(40, 52, 56, 52, 48, 70); c.Tri(40, 44, 56, 44, 48, 26); break;
                case "lt_ped": c.Box(18, 30, 42, 84, 6); c.Box(54, 30, 78, 60, 6); c.Line(30, 12, 30, 30, 5); c.Line(66, 12, 66, 30, 5); break;
                case "sym_bar": c.Box(20, 40, 76, 56, 2); break;
                case "sym_left": c.Line(58, 22, 58, 50, 8); c.Line(60, 50, 38, 50, 8); c.Tri(40, 37, 40, 63, 22, 50); break;
                case "sym_right": c.Line(38, 22, 38, 50, 8); c.Line(36, 50, 58, 50, 8); c.Tri(56, 37, 56, 63, 74, 50); break;
                case "sym_up": c.Line(48, 22, 48, 58, 9); c.Tri(33, 56, 63, 56, 48, 76); break;
                case "sym_uturn": c.Line(36, 22, 36, 52, 7); c.Arc(48, 52, 12, 180, 0, 7); c.Line(60, 52, 60, 40, 7); c.Tri(51, 42, 69, 42, 60, 28); break;
                case "sym_truck": c.Box(20, 38, 58, 64, 3); c.Box(60, 38, 76, 56, 3); c.Disc(30, 34, 6); c.Disc(66, 34, 6); break;
                case "sym_bike": c.Ring(31, 38, 10, 4); c.Ring(65, 38, 10, 4); c.Line(31, 38, 46, 56, 4); c.Line(46, 56, 65, 38, 4); c.Line(44, 56, 58, 56, 4); c.Line(40, 62, 50, 62, 4); break;
                case "sym_walk": c.Disc(50, 72, 6); c.Line(48, 63, 44, 42, 7); c.Line(44, 42, 34, 22, 6); c.Line(44, 42, 56, 24, 6); c.Line(47, 58, 34, 48, 5); c.Line(47, 58, 60, 48, 5); break;
                case "sym_round": c.Ring(48, 48, 18, 6); c.Tri(48, 74, 48, 58, 60, 66); c.Tri(26, 40, 40, 40, 33, 30); c.Tri(70, 40, 56, 40, 63, 30); break;
                case "sym_excl": c.Box(43, 38, 53, 74, 3); c.Disc(48, 27, 6); break;
                case "sym_curveL": c.Line(56, 20, 56, 46, 7); c.Arc(40, 46, 16, 0, 90, 7); c.Tri(42, 71, 42, 53, 28, 62); break;
                case "sym_curveR": c.Line(40, 20, 40, 46, 7); c.Arc(56, 46, 16, 180, 90, 7); c.Tri(54, 71, 54, 53, 68, 62); break;
                case "sym_signal": c.Box(38, 20, 58, 76, 5); break;
                case "sym_work": c.Tri(20, 22, 46, 22, 33, 40); c.Disc(60, 70, 6); c.Line(58, 62, 52, 42, 6); c.Line(52, 42, 44, 24, 5); c.Line(52, 42, 62, 24, 5); c.Line(55, 54, 72, 50, 4); break;
                case "sym_train": c.Box(26, 30, 70, 68, 6); c.Disc(36, 25, 6); c.Disc(60, 25, 6); break;
                case "sym_narrow": c.Line(34, 20, 34, 40, 6); c.Line(34, 40, 41, 56, 6); c.Line(41, 56, 41, 76, 6); c.Line(62, 20, 62, 40, 6); c.Line(62, 40, 55, 56, 6); c.Line(55, 56, 55, 76, 6); break;
                case "sym_bus": c.Box(24, 32, 72, 72, 6); c.Disc(34, 28, 6); c.Disc(62, 28, 6); break;
                case "sym_deadend": c.Line(48, 20, 48, 60, 11); c.Box(26, 60, 70, 74, 2); break;
                case "sym_highway": c.Line(34, 20, 42, 68, 6); c.Line(62, 20, 54, 68, 6); c.Line(48, 24, 48, 34, 4); c.Line(48, 44, 48, 54, 4); c.Box(28, 70, 68, 77, 2); break;
                case "sym_endslash": c.Line(26, 26, 70, 70, 5); c.Line(32, 22, 74, 64, 3); c.Line(22, 32, 64, 74, 3); break;
                case "sym_twocars": c.Box(24, 36, 44, 70, 6); c.Box(52, 36, 72, 70, 6); break;
                case "sym_moto": c.Ring(30, 38, 9, 4); c.Ring(66, 38, 9, 4); c.Line(30, 38, 48, 52, 5); c.Line(48, 52, 66, 38, 5); c.Line(44, 58, 62, 58, 5); break;
                case "sym_xbar": c.Line(26, 26, 70, 70, 7); c.Line(26, 70, 70, 26, 7); break;
                case "sym_upleft": c.Line(54, 22, 54, 66, 7); c.Tri(42, 64, 66, 64, 54, 80); c.Line(54, 46, 36, 46, 7); c.Tri(38, 36, 38, 56, 24, 46); break;
                case "sym_upright": c.Line(42, 22, 42, 66, 7); c.Tri(30, 64, 54, 64, 42, 80); c.Line(42, 46, 60, 46, 7); c.Tri(58, 36, 58, 56, 72, 46); break;
                case "sym_leftright": c.Line(48, 22, 48, 50, 7); c.Line(30, 50, 66, 50, 7); c.Tri(32, 40, 32, 60, 18, 50); c.Tri(64, 40, 64, 60, 78, 50); break;
                case "sym_keepright": c.Line(36, 68, 60, 34, 8); c.Tri(52, 30, 70, 30, 66, 46); break;
                case "sym_keepleft": c.Line(60, 68, 36, 34, 8); c.Tri(44, 30, 26, 30, 30, 46); break;
                case "sym_children": c.Disc(38, 70, 6); c.Line(38, 62, 36, 42, 6); c.Line(36, 42, 30, 24, 5); c.Line(36, 42, 44, 24, 5); c.Disc(60, 64, 5); c.Line(60, 57, 60, 40, 5); c.Line(60, 40, 54, 24, 4); c.Line(60, 40, 66, 24, 4); break;
                case "sym_deer": c.Box(30, 40, 62, 54, 6); c.Line(34, 40, 30, 22, 5); c.Line(58, 40, 62, 22, 5); c.Line(60, 52, 70, 66, 5); c.Line(70, 66, 66, 76, 4); c.Line(70, 66, 76, 74, 4); break;
                case "sym_slip": c.Box(34, 52, 62, 68, 5); c.Line(30, 40, 40, 26, 4); c.Line(44, 40, 54, 26, 4); c.Line(58, 40, 68, 26, 4); break;
                case "sym_bump": c.Line(18, 30, 36, 30, 6); c.Arc(48, 30, 12, 180, 0, 6); c.Line(60, 30, 78, 30, 6); break;
                case "sym_twoway": c.Line(38, 24, 38, 62, 6); c.Tri(30, 60, 46, 60, 38, 74); c.Line(58, 72, 58, 34, 6); c.Tri(50, 36, 66, 36, 58, 22); break;
                case "sym_merge": c.Line(56, 20, 56, 76, 8); c.Line(34, 24, 54, 50, 6); break;
                case "sym_cross": c.Line(48, 20, 48, 76, 8); c.Line(24, 48, 72, 48, 8); break;
                case "sym_zigzag": c.Line(44, 20, 54, 38, 6); c.Line(54, 38, 42, 56, 6); c.Line(42, 56, 52, 72, 6); c.Tri(44, 70, 60, 70, 52, 80); break;
                case "sym_hill": c.Tri(20, 24, 76, 24, 76, 62); break;
                case "sym_priocross": c.Line(48, 20, 48, 76, 10); c.Line(30, 48, 66, 48, 5); break;
                case "sym_tunnel": c.Arc(48, 30, 26, 0, 180, 8); c.Line(22, 30, 22, 20, 8); c.Line(74, 30, 74, 20, 8); break;
                case "sym_exitramp": c.Line(40, 20, 40, 70, 7); c.Line(40, 40, 64, 64, 7); c.Tri(58, 72, 74, 72, 72, 56); break;
                case "sym_chevL": c.Line(60, 24, 36, 48, 9); c.Line(36, 48, 60, 72, 9); break;
                case "sym_chevR": c.Line(36, 24, 60, 48, 9); c.Line(60, 48, 36, 72, 9); break;
                case "pole": c.Disc(48, 48, 16); c.Line(48, 48, 48, 88, 8); break;
                case "grid_none": c.Ring(48, 48, 26, 7); break;
                case "grid_diag": for (int gd = 0; gd < 3; gd++) { c.Line(18 + gd * 30, 14, 18 + gd * 30, 82, 4); c.Line(14, 18 + gd * 30, 82, 18 + gd * 30, 4); } c.Line(18, 18, 78, 78, 3); c.Line(18, 78, 78, 18, 3); break;
                case "ln_auto": c.Text("A", 48, 48, 50, 36, 9); break;
                case "stl_line": c.Box(14, 40, 82, 56, 3); break;
                case "stl_squares": for (int sq = 0; sq < 4; sq++) c.Box(12 + sq * 19, 40, 26 + sq * 19, 56, 2); break;
                case "stl_user": c.Box(14, 22, 82, 34, 3); c.Disc(48, 64, 10); c.Line(48, 54, 48, 38, 8); break;
                case "yl_teeth": for (int tt = 0; tt < 3; tt++) c.Tri(14 + tt * 24, 62, 34 + tt * 24, 62, 24 + tt * 24, 36); break;
                case "yl_dash": for (int dd = 0; dd < 4; dd++) c.Box(12 + dd * 19, 42, 24 + dd * 19, 54, 1); break;
                case "yl_dots": for (int dt = 0; dt < 5; dt++) c.Disc(16 + dt * 16, 48, 5); break;
                case "pause": c.Box(28, 22, 42, 74, 2); c.Box(54, 22, 68, 74, 2); break;
                case "ff2": c.Tri(16, 24, 16, 72, 48, 48); c.Tri(48, 24, 48, 72, 80, 48); break;
                case "ff3": c.Tri(8, 26, 8, 70, 34, 48); c.Tri(34, 26, 34, 70, 60, 48); c.Tri(60, 26, 60, 70, 86, 48); break;
                case "clock": c.Ring(48, 48, 38, 6); c.Line(48, 48, 48, 74, 6); c.Line(48, 48, 68, 40, 6); break;
                case "none": break;
                case "avoid": c.Ring(48, 48, 32, 8); c.Line(26, 26, 70, 70, 8); break;
                case "clockface": c.Ring(48, 48, 38, 6); c.Disc(48, 48, 5); break;
                case "restart": c.Arc(48, 48, 28, 40, 330, 8); c.Tri(70, 74, 82, 52, 60, 54); break;
                case "traffic": c.Box(12, 40, 60, 60, 6); c.Box(24, 56, 52, 70, 5); c.Disc(24, 38, 7); c.Disc(50, 38, 7); c.Box(46, 22, 86, 40, 5); c.Disc(56, 20, 5); c.Disc(78, 20, 5); break;
                case "amount": c.Box(16, 20, 28, 40, 2); c.Box(36, 20, 48, 56, 2); c.Box(56, 20, 68, 70, 2); c.Box(76, 20, 88, 84, 2); break;
                case "info": c.Ring(48, 48, 36, 7); c.Disc(48, 68, 6); c.Box(43, 24, 53, 56, 3); break;
                case "patience": c.Ring(48, 48, 34, 7); c.Line(48, 48, 48, 72, 6); c.Line(48, 48, 66, 48, 6); c.Disc(48, 48, 5); break;
                case "allred": c.Box(18, 12, 44, 84, 6); c.Box(52, 12, 78, 84, 6); break;
                case "truck": c.Box(10, 34, 60, 68, 4); c.Box(62, 34, 86, 58, 5); c.Disc(24, 32, 8); c.Disc(72, 32, 8); break;
                case "bus": c.Box(10, 30, 86, 70, 8); for (int bw = 0; bw < 4; bw++) c.Box(16 + bw * 17, 52, 28 + bw * 17, 64, 2); c.Disc(26, 28, 7); c.Disc(70, 28, 7); break;
                case "tram": c.Box(14, 26, 82, 66, 8); c.Line(48, 66, 48, 82, 4); c.Line(34, 84, 62, 84, 4); for (int tw = 0; tw < 3; tw++) c.Box(20 + tw * 21, 46, 34 + tw * 21, 60, 2); c.Line(10, 20, 86, 20, 4); break;
                case "moto": c.Ring(26, 34, 12, 6); c.Ring(70, 34, 12, 6); c.Line(26, 34, 48, 56, 6); c.Line(48, 56, 70, 34, 6); c.Line(44, 64, 62, 64, 6); break;
                case "ambulance": c.Box(12, 32, 84, 66, 6); c.Box(42, 40, 54, 60, 1); c.Box(38, 44, 58, 56, 1); c.Disc(26, 30, 7); c.Disc(70, 30, 7); break;
                case "van": c.Box(12, 32, 84, 70, 8); c.Disc(26, 30, 7); c.Disc(70, 30, 7); break;
                case "taxi": c.Box(12, 36, 84, 58, 6); c.Box(30, 56, 66, 70, 5); c.Box(42, 70, 54, 78, 1); c.Disc(28, 34, 7); c.Disc(68, 34, 7); break;
                case "fire": c.Tri(48, 88, 22, 34, 74, 34); c.Disc(48, 34, 26); c.Tri(30, 70, 24, 40, 44, 40); break;
                case "cone": c.Tri(48, 86, 28, 24, 68, 24); c.Box(18, 14, 78, 24, 3); break;
                case "hydrant": c.Box(34, 14, 62, 64, 6); c.Disc(48, 68, 16); c.Box(20, 38, 76, 48, 3); c.Box(26, 10, 70, 18, 2); break;
                case "settings": c.Ring(48, 48, 20, 12); for (int gi = 0; gi < 8; gi++) { float ga = gi * 45f * Mathf.Deg2Rad; c.Line(48 + Mathf.Cos(ga) * 26, 48 + Mathf.Sin(ga) * 26, 48 + Mathf.Cos(ga) * 38, 48 + Mathf.Sin(ga) * 38, 10); } break;
                case "sound": c.Box(16, 36, 34, 60, 2); c.Tri(30, 48, 56, 80, 56, 16); c.Arc(56, 48, 16, -50, 50, 5); c.Arc(56, 48, 28, -50, 50, 5); break;
                case "music": c.Disc(30, 26, 11); c.Disc(68, 34, 11); c.Line(39, 26, 39, 76, 5); c.Line(77, 34, 77, 84, 5); c.Line(39, 76, 77, 84, 8); break;
                case "trash": c.Box(18, 70, 78, 78, 2); c.Box(38, 76, 58, 84, 2); c.Box(24, 12, 72, 66, 6); break;
                case "eyeoff": c.Ring(48, 48, 26, 7); c.Disc(48, 48, 9); c.Line(18, 18, 78, 78, 8); break;
                case "pan": c.Line(48, 14, 48, 82, 7); c.Line(14, 48, 82, 48, 7); c.Tri(48, 90, 36, 76, 60, 76); c.Tri(48, 6, 36, 20, 60, 20); c.Tri(6, 48, 20, 36, 20, 60); c.Tri(90, 48, 76, 36, 76, 60); break;
                case "area": c.Line(18, 20, 78, 20, 7); c.Line(18, 76, 78, 76, 7); c.Line(18, 20, 18, 76, 7); c.Line(78, 20, 78, 76, 7); break;
                case "selzone": c.Line(18, 20, 40, 20, 7); c.Line(56, 20, 78, 20, 7); c.Line(18, 76, 40, 76, 7); c.Line(56, 76, 78, 76, 7); c.Line(18, 20, 18, 76, 7); c.Line(78, 20, 78, 76, 7); break;
                case "copy": c.Box(14, 30, 58, 82, 4); c.Box(38, 14, 82, 66, 4); break;
                case "cut": c.Line(20, 76, 76, 20, 8); c.Line(20, 20, 76, 76, 8); c.Ring(24, 24, 10, 6); c.Ring(24, 72, 10, 6); break;
                case "paste": c.Box(20, 14, 76, 72, 5); c.Box(36, 66, 60, 84, 3); break;
                case "cat_lane": c.Line(14, 14, 82, 82, 18); break;
                case "cat_logic": c.Tri(48, 82, 12, 50, 48, 18); c.Tri(48, 82, 84, 50, 48, 18); c.Disc(48, 56, 8); break;
                case "cat_decor": c.Tri(48, 80, 12, 48, 84, 48); c.Box(22, 14, 74, 48, 2); break;
                case "cat_zone": c.Line(18, 20, 40, 20, 7); c.Line(56, 20, 78, 20, 7); c.Line(18, 76, 78, 76, 7); c.Line(18, 20, 18, 76, 7); c.Line(78, 20, 78, 76, 7); break;
                case "save": c.Box(16, 16, 80, 80, 6); break;
                case "check": c.Line(20, 48, 40, 28, 10); c.Line(40, 28, 78, 72, 10); break;
                case "undo": c.Arc(52, 44, 26, 90, -90, 9); c.Tri(52, 82, 52, 56, 22, 70); break;
                case "redo": c.Arc(44, 44, 26, 90, 270, 9); c.Tri(44, 82, 44, 56, 74, 70); break;
                case "trailer": c.Box(10, 36, 70, 60, 4); c.Line(70, 48, 88, 48, 6); c.Disc(30, 34, 7); c.Disc(52, 34, 7); break;
                case "flood": c.Line(28, 14, 28, 70, 7); c.Box(18, 66, 62, 80, 3); c.Tri(62, 70, 90, 88, 90, 52); break;
                case "glow": c.Disc(48, 48, 14); c.Ring(48, 48, 30, 5); c.Ring(48, 48, 40, 3); break;
                case "railsign": c.Line(48, 10, 48, 50, 7); c.Line(22, 28, 74, 80, 11); c.Line(22, 80, 74, 28, 11); break;
                case "phone": c.Line(48, 10, 48, 86, 8); c.Line(20, 70, 76, 70, 7); c.Line(28, 50, 68, 50, 6); break;
                case "roadsign": c.Line(48, 10, 48, 50, 7); c.Box(18, 50, 78, 84, 5); break;
                case "tank": c.Disc(48, 48, 36); break;
                case "shelter": c.Box(12, 52, 84, 70, 3); c.Line(20, 52, 20, 18, 6); c.Line(76, 52, 76, 18, 6); c.Box(30, 28, 66, 36, 2); break;
                case "containers": c.Box(10, 14, 46, 40, 2); c.Box(50, 14, 86, 40, 2); c.Box(10, 44, 46, 70, 2); c.Box(50, 44, 86, 70, 2); break;
                case "powerline": c.Line(30, 12, 30, 84, 7); c.Line(14, 70, 46, 70, 6); c.Line(18, 48, 42, 48, 5); c.Line(46, 74, 90, 62, 3); c.Line(46, 70, 90, 56, 3); break;
                case "crane": c.Line(16, 14, 16, 70, 8); c.Line(80, 14, 80, 70, 8); c.Box(10, 66, 90, 82, 3); c.Box(40, 40, 60, 66, 2); break;
                case "sticker": c.Box(14, 14, 82, 82, 10); break;
                case "text": c.Line(24, 76, 72, 76, 9); c.Line(48, 76, 48, 16, 11); break;
                case "lines": c.Line(14, 20, 82, 76, 8); c.Line(14, 76, 82, 20, 8); break;
                case "misc": c.Tri(48, 82, 16, 18, 80, 18); c.Box(10, 12, 86, 24, 3); break;
                case "boat": c.Box(14, 34, 66, 62, 6); c.Tri(66, 34, 66, 62, 88, 48); c.Box(30, 62, 52, 78, 2); break;
                case "material": c.Box(12, 12, 84, 84, 8); break;
                case "zone": c.Tri(12, 20, 84, 14, 70, 60); c.Tri(12, 20, 70, 60, 30, 84); c.Disc(12, 20, 7); c.Disc(84, 14, 7); c.Disc(70, 60, 7); c.Disc(30, 84, 7); break;
                case "trail": c.Line(10, 80, 36, 40, 10); c.Line(36, 40, 62, 60, 10); c.Line(62, 60, 86, 16, 10); c.Disc(36, 40, 5); c.Disc(62, 60, 5); break;
                case "wallsticker": c.Box(14, 10, 82, 86, 4); c.Box(26, 54, 44, 74, 3); c.Box(52, 54, 70, 74, 3); c.Box(38, 10, 58, 36, 3); break;
                case "parking": c.Box(14, 14, 82, 82, 6); break;
                case "controller": c.Box(26, 8, 56, 88, 6); c.Ring(72, 28, 16, 6); c.Line(72, 28, 72, 18, 5); c.Line(72, 28, 80, 28, 5); break;
                case "busstop": c.Box(14, 32, 82, 66, 8); c.Disc(32, 28, 8); c.Disc(64, 28, 8); c.Box(22, 44, 74, 58, 2); break;
                case "toll": c.Line(10, 56, 86, 56, 8); c.Box(38, 14, 58, 56, 3); c.Line(14, 72, 40, 72, 6); break;
                case "detector": c.Ring(48, 44, 28, 7); c.Disc(48, 44, 8); c.Tri(48, 8, 36, 22, 60, 22); break;
                case "gbutton": c.Ring(48, 48, 32, 9); c.Disc(48, 48, 16); c.Line(48, 10, 48, 20, 6); break;
                case "stopline": c.Line(8, 48, 88, 48, 14); c.Line(8, 22, 88, 22, 4); c.Line(8, 74, 88, 74, 4); break;
                case "sidewalk": c.Box(8, 30, 88, 66, 3); c.Line(8, 48, 88, 48, 3); break;
                case "median": c.Box(8, 38, 88, 58, 6); c.Line(8, 18, 88, 18, 4); c.Line(8, 78, 88, 78, 4); break;
                case "grid": for (int gi = 0; gi < 4; gi++) { c.Line(14 + gi * 22, 10, 14 + gi * 22, 86, 4); c.Line(10, 14 + gi * 22, 86, 14 + gi * 22, 4); } break;
                case "lanestart": c.Line(30, 10, 30, 86, 6); c.Line(62, 50, 62, 86, 6); c.Line(30, 30, 62, 50, 6); c.Tri(62, 92, 52, 74, 72, 74); break;
                case "laneend": c.Line(30, 10, 30, 86, 6); c.Line(62, 10, 62, 46, 6); c.Line(62, 46, 30, 70, 6); c.Tri(30, 92, 20, 74, 40, 74); break;
                case "group": c.Box(10, 10, 46, 46, 4); c.Box(50, 50, 86, 86, 4); c.Ring(48, 48, 44, 4); break;
                case "ungroup": c.Box(10, 10, 46, 46, 4); c.Box(50, 50, 86, 86, 4); break;
                case "back": c.Tri(14, 48, 50, 80, 50, 16); c.Box(46, 40, 84, 56, 3); break;
                case "walk": c.Disc(48, 78, 9); c.Line(48, 66, 48, 38, 9); c.Line(48, 62, 30, 46, 7); c.Line(48, 62, 66, 46, 7); c.Line(48, 38, 34, 12, 8); c.Line(48, 38, 62, 12, 8); break;
                case "train": c.Box(16, 30, 80, 76, 8); c.Box(24, 54, 44, 68, 2); c.Box(52, 54, 72, 68, 2); c.Disc(30, 24, 7); c.Disc(66, 24, 7); c.Line(10, 14, 86, 14, 5); break;
                case "bike": c.Ring(26, 34, 16, 6); c.Ring(70, 34, 16, 6); c.Line(26, 34, 44, 62, 6); c.Line(44, 62, 70, 34, 6); c.Line(44, 62, 62, 62, 6); c.Line(40, 72, 50, 72, 6); break;
                case "phase": c.Box(10, 26, 38, 72, 5); c.Box(58, 26, 86, 72, 5); c.Line(40, 49, 56, 49, 6); c.Tri(56, 40, 56, 58, 66, 49); break;
                case "edgeL": c.Line(24, 8, 24, 88, 12); c.Line(60, 10, 60, 28, 6); c.Line(60, 40, 60, 58, 6); c.Line(60, 70, 60, 88, 6); break;
                case "edgeR": c.Line(72, 8, 72, 88, 12); c.Line(36, 10, 36, 28, 6); c.Line(36, 40, 36, 58, 6); c.Line(36, 70, 36, 88, 6); break;
                case "close": c.Line(24, 24, 72, 72, 12); c.Line(24, 72, 72, 24, 12); break;
                case "rampflat": c.Line(8, 22, 88, 22, 6); c.Box(18, 30, 78, 48, 6); c.Box(32, 46, 62, 60, 4); c.Disc(30, 30, 7); c.Disc(66, 30, 7); break;
                case "rampup": c.Line(8, 12, 88, 52, 6); c.Box(24, 38, 72, 54, 6); c.Line(30, 52, 60, 66, 10); c.Disc(32, 34, 7); c.Disc(64, 50, 7); break;
                case "rampdown": c.Line(8, 52, 88, 12, 6); c.Box(24, 38, 72, 54, 6); c.Line(36, 66, 66, 52, 10); c.Disc(32, 50, 7); c.Disc(64, 34, 7); break;
                case "ar_none": c.Ring(48, 48, 30, 6); c.Line(28, 28, 68, 68, 6); break;
                case "cw_none": c.Ring(48, 48, 30, 6); c.Line(28, 28, 68, 68, 6); break;
                case "cw_solid": c.Box(26, 10, 70, 86, 4); break;
                case "cw_lines": c.Line(28, 10, 28, 86, 9); c.Line(68, 10, 68, 86, 9); break;
                case "cw_zebra": for (int zi = 0; zi < 5; zi++) c.Box(20, 10 + zi * 16, 76, 18 + zi * 16); break;
                case "cw_diag": for (int zi = 0; zi < 5; zi++) c.Line(18, 22 + zi * 16, 78, 2 + zi * 16 + 16, 7); break;
                case "cw_ladder": c.Line(16, 10, 16, 86, 7); c.Line(80, 10, 80, 86, 7); for (int zi = 0; zi < 5; zi++) c.Box(26, 10 + zi * 16, 70, 18 + zi * 16); break;
                case "ar_s": ArrowS(c); break;
                case "ar_l": ArrowL(c); break;
                case "ar_r": ArrowR(c); break;
                case "ar_sl": ArrowS(c); ArrowL(c); break;
                case "ar_sr": ArrowS(c); ArrowR(c); break;
                case "ar_lr": ArrowL(c); ArrowR(c); break;
                case "ar_lsr": ArrowS(c); ArrowL(c); ArrowR(c); break;
                case "ar_cl": c.Line(56, 10, 56, 44, 10); c.Line(56, 44, 44, 66, 10); c.Tri(28, 62, 52, 72, 34, 92); break;
                case "ar_cr": c.Line(40, 10, 40, 44, 10); c.Line(40, 44, 52, 66, 10); c.Tri(68, 62, 44, 72, 62, 92); break;
                case "ln_none": c.Line(48, 10, 48, 22, 6); c.Line(48, 42, 48, 54, 6); c.Line(48, 74, 48, 86, 6); c.Line(20, 20, 76, 76, 6); break;
                case "ln_solid": c.Line(48, 8, 48, 88, 12); break;
                case "ln_dash": c.Line(48, 8, 48, 26, 12); c.Line(48, 39, 48, 57, 12); c.Line(48, 70, 48, 88, 12); break;
                case "ln_double": c.Line(38, 8, 38, 88, 10); c.Line(58, 8, 58, 88, 10); break;
                case "swatch": c.Disc(48, 48, 34); break;
                case "eye": c.Ring(48, 48, 30, 8); c.Disc(48, 48, 10); break;
                case "lanechange": c.Line(24, 12, 24, 84, 6); c.Line(72, 12, 72, 84, 6); c.Line(28, 26, 66, 64, 8); c.Tri(70, 72, 52, 62, 74, 50); break;
                case "color": c.Ring(48, 48, 32, 8); c.Disc(36, 58, 8); c.Disc(60, 58, 8); c.Disc(48, 34, 8); break;
                case "marks": c.Line(48, 10, 48, 26, 8); c.Line(48, 40, 48, 56, 8); c.Line(48, 70, 48, 86, 8); c.Line(18, 10, 18, 86, 5); c.Line(78, 10, 78, 86, 5); break;
                case "cables": c.Line(10, 72, 48, 50, 4); c.Line(48, 50, 86, 72, 4); c.Line(48, 10, 48, 50, 7); c.Line(24, 28, 72, 28, 6); break;
                case "rails": c.Line(8, 40, 88, 40, 6); c.Line(8, 62, 88, 62, 6); c.Line(20, 28, 20, 74, 6); c.Line(48, 28, 48, 74, 6); c.Line(76, 28, 76, 74, 6); break;
                case "flip": c.Line(14, 62, 70, 62, 7); c.Tri(68, 48, 68, 76, 88, 62); c.Line(82, 34, 26, 34, 7); c.Tri(28, 20, 28, 48, 8, 34); break;
                case "split": c.Line(8, 48, 36, 48, 8); c.Line(60, 48, 88, 48, 8); c.Disc(48, 48, 9); c.Line(48, 14, 48, 32, 6); c.Line(48, 64, 48, 82, 6); break;
                case "lock": c.Box(22, 12, 74, 50, 5); c.Ring(48, 58, 15, 7); break;
                case "plus": c.Line(18, 48, 78, 48, 12); c.Line(48, 18, 48, 78, 12); break;
                case "minus": c.Line(18, 48, 78, 48, 12); break;
                default: c.Disc(48, 48, 22); break;
            }
        }
    }
}
