using System.Collections.Generic;
using UnityEngine;

namespace IntersectionMaster.Systems
{
    /// <summary>Tema visual del mapa (colores). Se desbloquea con estrellas.</summary>
    public class ThemeDef
    {
        public string id, title;
        public int stars;
        public string outside, ground, sidewalk, asphalt, junction, tree1, tree2, park;
        public Color overlay;   // capa translúcida sobre el mapa (atardecer, noche)
    }

    /// <summary>Paleta de colores de los coches particulares. Se desbloquea con estrellas.</summary>
    public class LiveryDef
    {
        public string id, title;
        public int stars;
        public string[] colors;
    }

    public class RewardInfo
    {
        public string kind;      // "Tema" o "Colores"
        public string id, title, description;
        public int stars;
        public bool unlocked, selected;
    }

    /// <summary>
    /// Recompensas desbloqueables: temas del mapa y colores de coches, ligados al total de estrellas de la
    /// campaña y los desafíos. La selección se guarda en el progreso del jugador.
    /// </summary>
    public static class RewardSystem
    {
        public static readonly ThemeDef[] Themes =
        {
            new ThemeDef { id = "dia", title = "Día", stars = 0,
                outside = "#5F8F52", ground = "#7DB46C", sidewalk = "#CFCBBF", asphalt = "#3B4048", junction = "#444A53",
                tree1 = "#3F8F4F", tree2 = "#58AB66", park = "#92CF7E", overlay = new Color(0, 0, 0, 0) },
            new ThemeDef { id = "atardecer", title = "Atardecer", stars = 6,
                outside = "#6E8A4A", ground = "#8DAA62", sidewalk = "#D8C2A4", asphalt = "#3E3F48", junction = "#474852",
                tree1 = "#4F8240", tree2 = "#6C9B4A", park = "#A7B96D", overlay = new Color(1f, 0.55f, 0.2f, 0.16f) },
            new ThemeDef { id = "noche", title = "Noche", stars = 12,
                outside = "#2F5340", ground = "#3E6B52", sidewalk = "#8E92A0", asphalt = "#25282E", junction = "#2C3036",
                tree1 = "#24603A", tree2 = "#2F7A4B", park = "#3E7C5B", overlay = new Color(0.03f, 0.05f, 0.22f, 0.42f) },
            new ThemeDef { id = "otono", title = "Otoño", stars = 18,
                outside = "#8C8546", ground = "#A9A15A", sidewalk = "#D9CBB0", asphalt = "#3B4048", junction = "#444A53",
                tree1 = "#C2692B", tree2 = "#E2993A", park = "#C9B35E", overlay = new Color(0, 0, 0, 0) },
            new ThemeDef { id = "nieve", title = "Nieve", stars = 26,
                outside = "#CBD6DE", ground = "#E4EBF0", sidewalk = "#F3F6F8", asphalt = "#4B525B", junction = "#545B65",
                tree1 = "#5E8F7A", tree2 = "#84B5A0", park = "#F1F6F9", overlay = new Color(0, 0, 0, 0) },
        };

        public static readonly LiveryDef[] Liveries =
        {
            new LiveryDef { id = "clasico", title = "Clásicos", stars = 0,
                colors = new[] { "#E5534B", "#4C8DF6", "#F2F2F2", "#2E2E38", "#8C6BD9", "#3FB950", "#E08A2B" } },
            new LiveryDef { id = "neon", title = "Neón", stars = 9,
                colors = new[] { "#00E5FF", "#FF2FA5", "#B6FF00", "#FFEA00", "#9D4DFF", "#FF6A00" } },
            new LiveryDef { id = "retro", title = "Retro", stars = 15,
                colors = new[] { "#C85A3C", "#E3B04B", "#4F7C82", "#F2E3C0", "#7A4E3A", "#8DA05B" } },
            new LiveryDef { id = "flota", title = "Flota gris", stars = 22,
                colors = new[] { "#F2F2F2", "#2E2E38", "#9AA3AF", "#C9CED6" } },
        };

        public static int TotalStars()
        {
            int total = 0;
            foreach (var r in SaveSystem.Progress.levels)
            {
                if (r.id != null && r.id.StartsWith("daily_")) continue;   // el reto del día no cuenta
                total += r.stars;
            }
            return total;
        }

        public static ThemeDef Theme()
        {
            var p = SaveSystem.Progress;
            int stars = TotalStars();
            foreach (var t in Themes) if (t.id == p.theme && t.stars <= stars) return t;
            return Themes[0];
        }

        public static string[] CarPalette()
        {
            var p = SaveSystem.Progress;
            int stars = TotalStars();
            foreach (var l in Liveries) if (l.id == p.livery && l.stars <= stars) return l.colors;
            return Liveries[0].colors;
        }

        public static List<RewardInfo> All()
        {
            int stars = TotalStars();
            var list = new List<RewardInfo>();
            var activeTheme = Theme();
            foreach (var t in Themes)
            {
                list.Add(new RewardInfo
                {
                    kind = "Tema", id = t.id, title = "Tema: " + t.title, stars = t.stars,
                    description = t.stars == 0 ? "Incluido desde el principio." : "Se desbloquea con " + t.stars + " estrellas.",
                    unlocked = t.stars <= stars, selected = t.id == activeTheme.id
                });
            }
            string livery = ActiveLiveryId();
            foreach (var l in Liveries)
            {
                list.Add(new RewardInfo
                {
                    kind = "Colores", id = l.id, title = "Coches: " + l.title, stars = l.stars,
                    description = l.stars == 0 ? "Incluido desde el principio." : "Se desbloquea con " + l.stars + " estrellas.",
                    unlocked = l.stars <= stars, selected = l.id == livery
                });
            }
            return list;
        }

        static string ActiveLiveryId()
        {
            var p = SaveSystem.Progress;
            int stars = TotalStars();
            foreach (var l in Liveries) if (l.id == p.livery && l.stars <= stars) return l.id;
            return Liveries[0].id;
        }

        public static bool Select(RewardInfo r)
        {
            if (r == null || !r.unlocked) return false;
            var p = SaveSystem.Progress;
            if (r.kind == "Tema") p.theme = r.id;
            else p.livery = r.id;
            SaveSystem.SaveProgress();
            return true;
        }

        /// <summary>Nombres de las recompensas que se desbloquean al pasar de starsBefore a starsAfter estrellas.</summary>
        public static List<string> NewlyUnlocked(int starsBefore, int starsAfter)
        {
            var list = new List<string>();
            foreach (var t in Themes) if (t.stars > starsBefore && t.stars <= starsAfter) list.Add("Tema " + t.title);
            foreach (var l in Liveries) if (l.stars > starsBefore && l.stars <= starsAfter) list.Add("Coches " + l.title);
            return list;
        }
    }
}
