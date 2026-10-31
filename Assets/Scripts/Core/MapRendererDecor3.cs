using System.Collections.Generic;
using UnityEngine;
using IntersectionMaster.Data;

namespace IntersectionMaster.Core
{
    /// <summary>
    /// Decoración de muchos puntos (zonas y caminos dibujados a mano) y pegatinas de pared, como las del original.
    /// Dibujo propio: polígonos rellenos y tiras con borde.
    /// </summary>
    public partial class MapRenderer
    {
        public static readonly string[] ZoneNames = { "Césped", "Agua", "Arena", "Tierra", "Hormigón", "Asfalto", "Flores", "Nieve" };
        public static readonly string[] ZoneHex = { "#6FA84B", "#3E7FB8", "#E3CF95", "#9B7A55", "#B9B6AE", "#4B4E54", "#7DB04F", "#F2F5F7" };

        public static readonly string[] TrailNames = { "Camino de tierra", "Grava", "Canal de agua", "Seto", "Muro", "Vía de tren", "Carril bici", "Baldosas", "Berma" };
        public static readonly string[] TrailHex = { "#A88560", "#C9C3B5", "#3E7FB8", "#3F7A35", "#9E9890", "#6B6258", "#B5524A", "#D8CFC0", "#8E8E8E" };   // la berma usa el color del asfalto

        public static readonly string[] WallStickerNames = { "Ventana", "Puerta", "Persiana", "Aire acondicionado", "Cartel", "Grafiti", "Toldo", "Escaparate" };

        bool DrawDecorPoly(DecorItem d)
        {
            switch (d.type)
            {
                case DecorType.Zone: DrawZone(d); return true;
                case DecorType.Trail: DrawTrail(d); return true;
                case DecorType.WallSticker: DrawWallSticker(d); return true;
            }
            return false;
        }

        static Color PolyColor(DecorItem d, string[] hex)
        {
            Color c = RoadData.ValidHex(d.hexA) ? SpriteFactory.Hex(d.hexA) : SpriteFactory.Hex(hex[Mathf.Abs(d.variant) % hex.Length]);
            c.a = 1f - Mathf.Clamp(d.transparency, 0f, 0.9f);
            return c;
        }

        // ---------------- Zona ----------------

        void DrawZone(DecorItem d)
        {
            if (d.pts == null || d.pts.Count < 3) { DrawPolyDots(d); return; }
            var ring = new List<Vector2>(d.pts.Count);
            for (int i = 0; i < d.pts.Count; i++) ring.Add(d.PolyPoint(i));
            Color c = PolyColor(d, ZoneHex);
            int kind = Mathf.Abs(d.variant) % ZoneHex.Length;
            int order = d.overLane ? RoadOrder(0) + 1 : -16;
            var mb = new MeshBatch();
            var tris = Triangulate(ring);
            for (int i = 0; i + 2 < tris.Count; i += 3) mb.Tri(ring[tris[i]], ring[tris[i + 1]], ring[tris[i + 2]], c);
            // Borde: más oscuro en el agua y la tierra, claro en el hormigón.
            Color edge = kind == 4 || kind == 7 ? Color.Lerp(c, Color.white, 0.3f) : Color.Lerp(c, Color.black, kind == 1 ? 0.35f : 0.18f);
            edge.a = c.a;
            float ew = kind == 1 ? 0.07f : 0.04f;
            for (int i = 0; i < ring.Count; i++) Seg(mb, ring[i], ring[(i + 1) % ring.Count], ew, edge);
            // Detalle: flores (puntitos de colores) y olas (rayitas claras en el agua).
            if (kind == 6 || kind == 1)
            {
                Rect bb = Bounds(ring);
                var rnd = new System.Random(Mathf.RoundToInt(d.x * 97f + d.y * 31f));
                int count = Mathf.Clamp(Mathf.RoundToInt(bb.width * bb.height * (kind == 6 ? 6f : 1.2f)), 0, 400);
                string[] fl = { "#E8505B", "#F7D046", "#FFFFFF", "#C774E8" };
                for (int k = 0; k < count; k++)
                {
                    Vector2 q = new Vector2(bb.xMin + (float)rnd.NextDouble() * bb.width, bb.yMin + (float)rnd.NextDouble() * bb.height);
                    if (!Inside(ring, q)) continue;
                    if (kind == 6) mb.Disc(q, 0.035f, SpriteFactory.Hex(fl[rnd.Next(fl.Length)]), 6);
                    else mb.Rect(q, Vector2.right, 0.22f, 0.025f, new Color(1f, 1f, 1f, 0.28f * c.a));
                }
            }
            mb.Build(staticRoot, "Zone", order);
        }

        // ---------------- Camino ----------------

        void DrawTrail(DecorItem d)
        {
            if (d.pts == null || d.pts.Count < 2) { DrawPolyDots(d); return; }
            int kind = Mathf.Abs(d.variant) % TrailHex.Length;
            Color c = PolyColor(d, TrailHex);
            if (kind == 8 && !RoadData.ValidHex(d.hexA)) { c = curEditor ? asphalt : PlayAsphalt(); c.a = 1f - Mathf.Clamp(d.transparency, 0f, 0.9f); }   // berma: igual que la calzada
            float w = d.TrailWidth;
            int order = d.overLane ? RoadOrder(0) + 1 : (kind == 3 || kind == 4 ? 4 : -14);
            var mb = new MeshBatch();
            Color dark = Color.Lerp(c, Color.black, 0.3f); dark.a = c.a;
            for (int i = 0; i + 1 < d.pts.Count; i++)
            {
                Vector2 a = d.PolyPoint(i), b = d.PolyPoint(i + 1);
                if (kind == 1 || kind == 2 || kind == 4 || kind == 7) Seg(mb, a, b, w + 0.06f, dark);   // borde
                Seg(mb, a, b, w, c);
            }
            for (int i = 0; i < d.pts.Count; i++)   // juntas redondas
            {
                mb.Disc(d.PolyPoint(i), w * 0.5f, c, 12);
            }
            // Detalles por tipo.
            for (int i = 0; i + 1 < d.pts.Count; i++)
            {
                Vector2 a = d.PolyPoint(i), b = d.PolyPoint(i + 1);
                float len = Vector2.Distance(a, b);
                if (len < 0.05f) continue;
                Vector2 f = (b - a) / len, n = new Vector2(-f.y, f.x);
                if (kind == 5)
                {
                    // Vía: traviesas y dos raíles.
                    for (float t = 0.08f; t < len; t += 0.16f) mb.Rect(a + f * t, n, w * 0.95f, 0.05f, new Color(0.42f, 0.32f, 0.22f, c.a));
                    Seg(mb, a + n * w * 0.22f, b + n * w * 0.22f, 0.03f, new Color(0.75f, 0.76f, 0.78f, c.a));
                    Seg(mb, a - n * w * 0.22f, b - n * w * 0.22f, 0.03f, new Color(0.75f, 0.76f, 0.78f, c.a));
                }
                else if (kind == 6)
                    for (float t = 0.3f; t < len; t += 0.9f) mb.Rect(a + f * t, f, 0.35f, 0.03f, new Color(1f, 1f, 1f, 0.8f * c.a));
                else if (kind == 7)
                    for (float t = 0.1f; t < len; t += 0.2f) mb.Rect(a + f * t, n, w, 0.012f, dark);
                else if (kind == 3)
                    for (float t = 0.1f; t < len; t += 0.22f) mb.Disc(a + f * t + n * w * 0.15f, w * 0.28f, Color.Lerp(c, Color.white, 0.12f), 8);
            }
            mb.Build(staticRoot, "Trail", order);
            if ((kind == 3 || kind == 4) && !d.noShadow)
                for (int i = 0; i + 1 < d.pts.Count; i++)
                    RegShadow(SpriteFactory.Make("TrailShadow", staticRoot, SpriteFactory.Square, new Color(0f, 0f, 0f, 0.18f),
                        (d.PolyPoint(i) + d.PolyPoint(i + 1)) * 0.5f + SunDesign * w * 0.4f,
                        new Vector2(Vector2.Distance(d.PolyPoint(i), d.PolyPoint(i + 1)) + w, w), 3,
                        Mathf.Atan2(d.PolyPoint(i + 1).y - d.PolyPoint(i).y, d.PolyPoint(i + 1).x - d.PolyPoint(i).x) * Mathf.Rad2Deg), d.Pos);
        }

        /// <summary>Zona o camino que aún no tiene puntos suficientes: se ven los puntos puestos.</summary>
        void DrawPolyDots(DecorItem d)
        {
            var mb = new MeshBatch();
            for (int i = 0; i < d.pts.Count; i++) mb.Disc(d.PolyPoint(i), 0.08f, new Color(1f, 1f, 1f, 0.8f), 12);
            mb.Build(staticRoot, "PolyDots", 8);
        }

        // ---------------- Pegatina de pared ----------------

        void DrawWallSticker(DecorItem d)
        {
            Vector2 f = Geo.FromAngle(d.rot);
            float s = d.size;
            // La «Altura» (height) sube la pegatina por la pared, como los pisos de los edificios (0,1 por piso).
            Vector2 p = d.Pos + new Vector2(0f, 0.1f * (d.height - 0.5f));
            float ang = Mathf.Atan2(f.y, f.x) * Mathf.Rad2Deg;
            Color c = RoadData.ValidHex(d.hexA) ? SpriteFactory.Hex(d.hexA) : ObjCol(d.color);
            var mb = new MeshBatch();
            System.Action<Vector2, float, float, Color> box = (center, wAlong, hUp, col) =>
            {
                Vector2 ha = f * wAlong * 0.5f, hu = new Vector2(0f, hUp * 0.5f);
                mb.Quad(center - ha - hu, center + ha - hu, center + ha + hu, center - ha + hu, col);
            };
            switch (Mathf.Abs(d.variant) % WallStickerNames.Length)
            {
                case 0:   // ventana con marco
                    box(p, 0.16f * s, 0.12f * s, new Color(0.9f, 0.9f, 0.88f));
                    box(p, 0.13f * s, 0.095f * s, new Color(0.25f, 0.33f, 0.45f));
                    box(p, 0.012f * s, 0.095f * s, new Color(0.9f, 0.9f, 0.88f));
                    RegNightGlow(SpriteFactory.Make("WallWinLit", staticRoot, SpriteFactory.Square, new Color(1f, 0.84f, 0.5f, 0f), p, new Vector2(0.13f, 0.095f) * s, NightOverlayOrder + 1, ang), 0.85f);
                    break;
                case 1:   // puerta (apoyada en el suelo)
                    p = d.Pos + new Vector2(0f, 0.08f * s);
                    box(p, 0.1f * s, 0.16f * s, Color.Lerp(c, Color.black, 0.25f));
                    box(p + f * 0.03f * s, 0.012f * s, 0.012f * s, new Color(0.9f, 0.8f, 0.3f));
                    break;
                case 2:   // persiana bajada
                    box(p, 0.16f * s, 0.12f * s, Color.Lerp(c, Color.white, 0.3f));
                    for (int k = 0; k < 5; k++) box(p + new Vector2(0f, (k - 2) * 0.022f * s), 0.15f * s, 0.006f * s, Color.Lerp(c, Color.black, 0.3f));
                    break;
                case 3:   // aire acondicionado
                    box(p, 0.11f * s, 0.07f * s, new Color(0.88f, 0.89f, 0.9f));
                    mb.Disc(p + f * 0.02f * s, 0.026f * s, new Color(0.35f, 0.37f, 0.4f), 12);
                    break;
                case 4:   // cartel luminoso
                    box(p, 0.3f * s, 0.08f * s, c);
                    box(p, 0.27f * s, 0.05f * s, Color.Lerp(c, Color.white, 0.55f));
                    RegNightGlow(SpriteFactory.Make("SignLit", staticRoot, SpriteFactory.Soft, new Color(c.r, c.g, c.b, 0f), p, new Vector2(0.45f, 0.18f) * s, NightOverlayOrder + 1, ang), 0.7f);
                    break;
                case 5:   // grafiti: manchas de color
                    {
                        var rnd = new System.Random(Mathf.RoundToInt(d.x * 53f + d.y * 19f));
                        for (int k = 0; k < 7; k++)
                        {
                            Color gc = Color.HSVToRGB((float)rnd.NextDouble(), 0.75f, 0.9f);
                            box(p + f * (((float)rnd.NextDouble() - 0.5f) * 0.22f * s) + new Vector2(0f, ((float)rnd.NextDouble() - 0.5f) * 0.06f * s),
                                (0.04f + (float)rnd.NextDouble() * 0.06f) * s, (0.02f + (float)rnd.NextDouble() * 0.03f) * s, gc);
                        }
                        break;
                    }
                case 6:   // toldo a rayas
                    for (int k = 0; k < 6; k++)
                        box(p + f * ((k - 2.5f) * 0.035f * s), 0.035f * s, 0.06f * s, k % 2 == 0 ? c : Color.white);
                    break;
                default:  // escaparate
                    p = d.Pos + new Vector2(0f, 0.07f * s);
                    box(p, 0.3f * s, 0.14f * s, new Color(0.2f, 0.2f, 0.22f));
                    box(p, 0.27f * s, 0.115f * s, new Color(0.55f, 0.72f, 0.82f));
                    RegNightGlow(SpriteFactory.Make("ShopLit", staticRoot, SpriteFactory.Square, new Color(1f, 0.92f, 0.7f, 0f), p, new Vector2(0.27f, 0.115f) * s, NightOverlayOrder + 1, ang), 0.9f);
                    break;
            }
            mb.Build(staticRoot, "WallSticker", 6);
        }

        // ---------------- Ayudas de geometría ----------------

        static void Seg(MeshBatch mb, Vector2 a, Vector2 b, float w, Color c)
        {
            float len = Vector2.Distance(a, b);
            if (len < 0.0001f) return;
            mb.Rect((a + b) * 0.5f, (b - a) / len, len, w, c);
        }

        static Rect Bounds(List<Vector2> ring)
        {
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            foreach (var q in ring) { x0 = Mathf.Min(x0, q.x); y0 = Mathf.Min(y0, q.y); x1 = Mathf.Max(x1, q.x); y1 = Mathf.Max(y1, q.y); }
            return Rect.MinMaxRect(x0, y0, x1, y1);
        }

        static bool Inside(List<Vector2> ring, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            {
                Vector2 a = ring[i], b = ring[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }

        /// <summary>Triangulación por «orejas» de un polígono simple (sirve también para polígonos cóncavos).</summary>
        public static List<int> Triangulate(List<Vector2> ring)
        {
            var result = new List<int>();
            int n = ring.Count;
            if (n < 3) return result;
            float area = 0f;
            for (int i = 0, j = n - 1; i < n; j = i++) area += ring[j].x * ring[i].y - ring[i].x * ring[j].y;
            var idx = new List<int>(n);
            for (int i = 0; i < n; i++) idx.Add(area > 0f ? i : n - 1 - i);   // en sentido antihorario
            int guard = 0;
            while (idx.Count > 3 && guard++ < 4096)
            {
                bool cut = false;
                for (int k = 0; k < idx.Count; k++)
                {
                    int ia = idx[(k + idx.Count - 1) % idx.Count], ib = idx[k], ic = idx[(k + 1) % idx.Count];
                    Vector2 a = ring[ia], b = ring[ib], c = ring[ic];
                    float cross = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
                    if (cross <= 1e-7f) continue;   // vértice cóncavo
                    bool empty = true;
                    for (int m = 0; m < idx.Count && empty; m++)
                    {
                        int im = idx[m];
                        if (im == ia || im == ib || im == ic) continue;
                        if (InTri(ring[im], a, b, c)) empty = false;
                    }
                    if (!empty) continue;
                    result.Add(ia); result.Add(ib); result.Add(ic);
                    idx.RemoveAt(k);
                    cut = true;
                    break;
                }
                if (!cut) break;   // polígono que se cruza: lo que queda se rellena en abanico
            }
            for (int k = 1; k + 1 < idx.Count; k++) { result.Add(idx[0]); result.Add(idx[k]); result.Add(idx[k + 1]); }
            return result;
        }

        static bool InTri(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
            float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
            float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
            bool neg = d1 < 0f || d2 < 0f || d3 < 0f, pos = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(neg && pos);
        }
    }
}
