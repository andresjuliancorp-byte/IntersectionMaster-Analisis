using System;
using System.Collections.Generic;
using UnityEngine;
using IntersectionMaster.Data;

namespace IntersectionMaster.Systems
{
    /// <summary>
    /// Serialización versionada de mapas (JSON). Al cargar valida y sanea los datos, de modo que un archivo
    /// importado malformado no puede romper el juego.
    /// </summary>
    public static class MapSerializationSystem
    {
        public const int MaxJsonChars = 8000000;   // mapas grandes importados (miles de objetos)

        public static string ToJson(MapData map, bool pretty = false)
        {
            map.version = MapData.CurrentVersion;
            return JsonUtility.ToJson(map, pretty);
        }

        public static bool TryFromJson(string json, out MapData map, out string error)
        {
            map = null;
            error = "";
            if (string.IsNullOrWhiteSpace(json)) { error = "El archivo está vacío."; return false; }
            if (json.Length > MaxJsonChars) { error = "El archivo es demasiado grande."; return false; }

            try
            {
                map = JsonUtility.FromJson<MapData>(json);
            }
            catch (Exception ex)
            {
                map = null;
                error = "Formato no válido: " + ex.Message;
                return false;
            }
            if (map == null) { error = "Formato no válido."; return false; }
            if (map.version < MapData.CurrentVersion)
            {
                error = "Este mapa es de una versión anterior (cuadrícula) y no se puede abrir con el editor de nodos.";
                map = null;
                return false;
            }
            if (map.version > MapData.CurrentVersion)
            {
                error = "Versión de mapa no compatible (" + map.version + ").";
                map = null;
                return false;
            }

            try { Sanitize(map); }
            catch (Exception ex)
            {
                map = null;
                error = "Datos no válidos: " + ex.GetType().Name + " " + ex.Message;
                return false;
            }
            return true;
        }

        static void Sanitize(MapData m)
        {
            // Los mapas por carriles son grandes (200 × 200, como el original): no se recortan a 60 (si no, al cargarlos
            // todos los nodos quedaban amontonados en una esquina).
            float max = m.laneFormat >= MapData.LaneFormatCurrent ? Mathf.Max(MapData.MaxSize, MapData.BigSize) : MapData.MaxSize;
            m.width = Mathf.Clamp(m.width, MapData.MinSize, max);
            m.height = Mathf.Clamp(m.height, MapData.MinSize, max);
            m.name = Trim(m.name, 40, "Mapa sin nombre");
            m.author = Trim(m.author, 30, "Anónimo");
            m.description = Trim(m.description, 200, "");
            m.difficulty = Mathf.Clamp(m.difficulty, 1, 5);
            if (string.IsNullOrEmpty(m.id) || m.id.Length > 40) m.id = MapData.NewId();
            if (m.thumbnailBase64 == null) m.thumbnailBase64 = "";

            // Nodos, calles, señales, pasos de peatones y decoración.
            m.Normalize();

            if (m.traffic == null) m.traffic = new TrafficSettings();
            var t = m.traffic;
            t.spawnPerMinute = Mathf.Clamp(t.spawnPerMinute, 1f, 120f);
            t.pedestriansPerMinute = Mathf.Clamp(t.pedestriansPerMinute, 0f, 60f);
            t.speedFactor = Mathf.Clamp(t.speedFactor, 0.5f, 2f);
            t.emergencyChance = Mathf.Clamp01(t.emergencyChance);
            t.taxiChance = Mathf.Clamp01(t.taxiChance);
            t.busChance = Mathf.Clamp01(t.busChance);
            t.truckChance = Mathf.Clamp01(t.truckChance);
            t.motorbikeChance = Mathf.Clamp01(t.motorbikeChance);
            t.vanChance = Mathf.Clamp01(t.vanChance);
            t.greenSeconds = Mathf.Clamp(t.greenSeconds, 3f, 30f);

            if (m.objective == null) m.objective = new ObjectiveData();
            m.objective.targetDelivered = Mathf.Clamp(m.objective.targetDelivered, 0, 100000);
            m.objective.timeLimitSeconds = Mathf.Clamp(m.objective.timeLimitSeconds, 0f, 36000f);
            m.objective.maxAccidents = Mathf.Clamp(m.objective.maxAccidents, 0, 100);
        }

        static string Trim(string s, int max, string fallback)
        {
            if (string.IsNullOrWhiteSpace(s)) return fallback;
            s = s.Trim();
            return s.Length > max ? s.Substring(0, max) : s;
        }

        // ---------------- Miniatura ----------------

        public static string MakeThumbnail(MapData m)
        {
            float ppu = Mathf.Max(1.5f, Mathf.Min(120f / m.width, 80f / m.height));
            int w = Mathf.Max(8, Mathf.CeilToInt(m.width * ppu)), h = Mathf.Max(8, Mathf.CeilToInt(m.height * ppu));
            var pixels = new Color32[w * h];
            var grass = new Color32(125, 180, 108, 255);
            var side = new Color32(200, 200, 196, 255);
            var road = new Color32(59, 64, 72, 255);
            var build = new Color32(196, 170, 140, 255);
            var tree = new Color32(60, 130, 70, 255);
            var park = new Color32(150, 205, 120, 255);
            var entry = new Color32(63, 185, 80, 255);
            var exit = new Color32(214, 40, 40, 255);
            var both = new Color32(76, 141, 246, 255);

            for (int i = 0; i < pixels.Length; i++) pixels[i] = grass;
            foreach (var d in m.decor)
                Disc(pixels, w, h, d.x * ppu, d.y * ppu, (d.type == DecorType.Building ? 0.6f : (d.type == DecorType.Tree ? 0.4f : 0.7f)) * ppu,
                    d.type == DecorType.Building ? build : (d.type == DecorType.Tree ? tree : park));

            // Aceras y asfalto de las calles.
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (var r in m.roads)
                {
                    float hw = (r.lanesAB + r.lanesBA) * 0.18f + (pass == 0 ? 0.2f : 0f);
                    var pl = m.CenterOf(r);
                    int steps = Mathf.Max(2, Mathf.CeilToInt(pl.Length * ppu / 1.2f));
                    for (int i = 0; i <= steps; i++)
                    {
                        Vector2 p = pl.PointAt(pl.Length * i / steps);
                        Disc(pixels, w, h, p.x * ppu, p.y * ppu, Mathf.Max(1f, hw * ppu), pass == 0 ? side : road);
                    }
                }
                foreach (var n in m.nodes)
                {
                    if (!n.roundabout) continue;
                    Disc(pixels, w, h, n.x * ppu, n.y * ppu, (n.radius + 0.4f + (pass == 0 ? 0.2f : 0f)) * ppu, pass == 0 ? side : road);
                    if (pass == 1) Disc(pixels, w, h, n.x * ppu, n.y * ppu, Mathf.Max(1f, (n.radius - 0.25f) * ppu), park);
                }
            }
            foreach (var n in m.nodes)
            {
                if (!n.entry && !n.exit) continue;
                Disc(pixels, w, h, n.x * ppu, n.y * ppu, Mathf.Max(2f, 0.45f * ppu), n.entry && n.exit ? both : (n.entry ? entry : exit));
            }

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels32(pixels);
            tex.Apply();
            byte[] png = tex.EncodeToPNG();
            UnityEngine.Object.Destroy(tex);
            return Convert.ToBase64String(png);
        }

        static void Disc(Color32[] pixels, int w, int h, float cx, float cy, float r, Color32 color)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - r)), x1 = Mathf.Min(w - 1, Mathf.CeilToInt(cx + r));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - r)), y1 = Mathf.Min(h - 1, Mathf.CeilToInt(cy + r));
            float r2 = r * r;
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                    if (dx * dx + dy * dy <= r2) pixels[y * w + x] = color;
                }
            }
        }

        public static Texture2D ThumbnailTexture(string base64)
        {
            if (string.IsNullOrEmpty(base64)) return null;
            try
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (tex.LoadImage(Convert.FromBase64String(base64)))
                {
                    tex.filterMode = FilterMode.Point;
                    return tex;
                }
            }
            catch (Exception) { }
            return null;
        }
    }
}
