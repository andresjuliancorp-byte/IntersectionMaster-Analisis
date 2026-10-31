using System.Collections.Generic;
using UnityEngine;
using IntersectionMaster.Data;

namespace IntersectionMaster.Core
{
    /// <summary>
    /// Noche y sombras que se mueven con el sol (ver DayNight). Las sombras se registran al dibujarlas: un punto (la mancha
    /// de un edificio, un árbol, una señal), un poste (desde su base) o un brazo (paralelo a su posición en alto). Cada
    /// fotograma en que cambia la hora se recolocan según la dirección y el largo de la sombra del momento. De noche una capa
    /// azul oscura cubre el mapa y por encima brillan las luces registradas (farolas, faros, semáforos).
    /// </summary>
    public partial class MapRenderer
    {
        class ShadowEntry
        {
            public Transform t;
            public SpriteRenderer sr;
            public int kind;          // 0 mancha, 1 poste (de a hacia la sombra), 2 brazo (de a a b, desplazado)
            public Vector2 a, b;
            public float len0, alpha0, w;
        }

        readonly List<ShadowEntry> sunShadows = new List<ShadowEntry>();
        readonly List<SpriteRenderer> nightGlows = new List<SpriteRenderer>();
        readonly List<float> nightGlowAlpha = new List<float>();
        SpriteRenderer nightOverlay;
        float lastHour = -100f;
        bool lastActive;

        public const int NightOverlayOrder = 34;   // por encima de calles y vehículos; las luces van más arriba

        /// <summary>Luces que parpadean (emergencia e intermitentes de los coches aparcados).</summary>
        readonly List<SpriteRenderer> blinkers = new List<SpriteRenderer>();
        readonly List<float> blinkPeriods = new List<float>();

        void AddBlinker(SpriteRenderer sr, float period)
        {
            if (sr == null) return;
            blinkers.Add(sr);
            blinkPeriods.Add(Mathf.Max(0.1f, period));
        }

        void UpdateBlinkers()
        {
            for (int i = blinkers.Count - 1; i >= 0; i--)
            {
                if (blinkers[i] == null) { blinkers.RemoveAt(i); blinkPeriods.RemoveAt(i); continue; }
                blinkers[i].enabled = Mathf.Repeat(Time.time / blinkPeriods[i], 1f) < 0.5f;
            }
        }

        void ClearDayNight()
        {
            blinkers.Clear();
            blinkPeriods.Clear();
            sunShadows.Clear();
            nightGlows.Clear();
            nightGlowAlpha.Clear();
            lastHour = -100f;
        }

        /// <summary>Sombra en forma de mancha junto a un objeto que está en «anchor».</summary>
        SpriteRenderer RegShadow(SpriteRenderer sr, Vector2 anchor)
        {
            if (sr == null) return sr;
            Vector2 pos = sr.transform.localPosition;
            sunShadows.Add(new ShadowEntry { t = sr.transform, sr = sr, kind = 0, a = anchor, len0 = (pos - anchor).magnitude, alpha0 = sr.color.a });
            return sr;
        }

        /// <summary>Sombra de un poste: va de su base «a» hasta «b» (en el dibujo de diseño).</summary>
        SpriteRenderer RegPoleShadow(SpriteRenderer sr, Vector2 a, Vector2 b)
        {
            if (sr == null) return sr;
            sunShadows.Add(new ShadowEntry { t = sr.transform, sr = sr, kind = 1, a = a, len0 = (b - a).magnitude, alpha0 = sr.color.a, w = sr.transform.localScale.y });
            return sr;
        }

        /// <summary>Sombra de un brazo en alto que va de «a» a «b»: es el brazo desplazado tanto como la sombra de su poste (len0).</summary>
        SpriteRenderer RegArmShadow(SpriteRenderer sr, Vector2 a, Vector2 b, float len0)
        {
            if (sr == null) return sr;
            sunShadows.Add(new ShadowEntry { t = sr.transform, sr = sr, kind = 2, a = a, b = b, len0 = len0, alpha0 = sr.color.a, w = sr.transform.localScale.y });
            return sr;
        }

        /// <summary>Luz que solo se ve de noche (farolas, faros…): su opacidad va de 0 (día) a maxAlpha (noche).</summary>
        SpriteRenderer RegNightGlow(SpriteRenderer sr, float maxAlpha)
        {
            if (sr == null) return sr;
            nightGlows.Add(sr);
            nightGlowAlpha.Add(maxAlpha);
            var c = sr.color; c.a = maxAlpha * DayNight.Darkness; sr.color = c;
            return sr;
        }

        /// <summary>Colores de la luz de la farola (como el original): cálida, fría, blanca y amarilla (sodio).</summary>
        /// <summary>Alcance de la luz de una farola (radio del charco en el suelo).</summary>
        public const float LampReachBase = 1.3f;
        public static float LampReach(DecorItem d) { return LampReachBase * Mathf.Clamp(d.light, 0.3f, 3f) * Mathf.Clamp(d.size, 0.4f, 3f); }

        public static readonly string[] LampColorNames = { "Blanca", "Cálida", "Fría", "Amarilla" };
        static readonly Color[] LampColors = { new Color(0.86f, 0.94f, 1f), new Color(1f, 0.86f, 0.58f), new Color(0.7f, 0.82f, 1f), new Color(1f, 0.74f, 0.3f) };   // blanca (como el original), cálida, fría, amarilla

        /// <summary>
        /// Farola realista vista desde arriba (3D falso): base en el suelo, poste que sube, brazo hacia la calle (o dos, de
        /// doble sentido) y luminaria con su cristal. Sombras de poste y brazo que se mueven con el sol. De noche ilumina:
        /// un charco de luz en el suelo bajo la luminaria y el brillo del cristal; de día no alumbra.
        /// </summary>
        void DrawStreetLamp(Transform parent, DecorItem d, MapData map, Vector2 p)
        {
            float k = Mathf.Clamp(d.size, 0.4f, 3f);
            Vector2 dir;
            if (d.rot != 0f) dir = new Vector2(Mathf.Cos(d.rot * Mathf.Deg2Rad), Mathf.Sin(d.rot * Mathf.Deg2Rad));
            else
            {
                // Automático: el brazo apunta a la calle más cercana.
                float t;
                var r = map.NearestRoad(p, 4f, out t);
                Vector2 q = r != null ? map.PointOnRoad(r, t) : p + Vector2.right;
                dir = (q - p).sqrMagnitude > 1e-6f ? (q - p).normalized : Vector2.right;
            }
            bool twin = (d.variant % 2) == 1;
            Color light = LampColors[Mathf.Abs(d.color) % LampColors.Length];
            float Z = 0.32f * k, arm = 0.3f * k, shadowLen = 0.45f * k;
            Vector2 up = new Vector2(0f, Z);
            var metal = new Color(0.36f, 0.38f, 0.42f, 1f);
            var shadow = new Color(0f, 0f, 0f, 0.24f);
            Vector2 sDesign = new Vector2(-shadowLen, 0f);
            RegPoleShadow(Bar(parent, "LampPoleShadow", p, p + sDesign, 0.02f * k, shadow, 3), p, p + sDesign);
            SpriteFactory.Make("LampBase", parent, SpriteFactory.Circle, new Color(0.22f, 0.23f, 0.26f, 1f), p, Vector2.one * 0.06f * k, 7);
            Bar(parent, "LampPole", p, p + up, 0.03f * k, metal, 8);
            for (int side = 0; side < (twin ? 2 : 1); side++)
            {
                Vector2 ad = side == 0 ? dir : -dir;
                Vector2 headG = p + ad * arm;                       // bajo la luminaria, en el suelo
                RegArmShadow(Bar(parent, "LampArmShadow", p + sDesign, headG + sDesign, 0.018f * k, shadow, 3), p, headG, shadowLen);
                Bar(parent, "LampArm", p + up, headG + up + ad * 0.01f * k, 0.022f * k, metal, 8);
                float ang = Mathf.Atan2(ad.y, ad.x) * Mathf.Rad2Deg;
                Vector2 head = headG + up;
                SpriteFactory.Make("LampHousing", parent, SpriteFactory.Square, new Color(0.2f, 0.21f, 0.24f, 1f), head, new Vector2(0.17f, 0.075f) * k, 9, ang);
                SpriteFactory.Make("LampLens", parent, SpriteFactory.Square, Color.Lerp(light, Color.white, 0.25f), head + ad * 0.01f * k, new Vector2(0.11f, 0.045f) * k, 10, ang);
                // De noche, como en la vida real: el cristal de la luminaria se enciende (sin destellos) y la luz cae al
                // suelo en un charco suave bajo la lámpara, algo adelantado hacia la calle; su tamaño es el alcance.
                float R = LampReach(d);
                float inten = Mathf.Clamp(0.8f + 0.1f * d.light, 0.8f, 1.1f);
                RegNightGlow(SpriteFactory.Make("LampPool", parent, SpriteFactory.Soft, new Color(light.r, light.g, light.b, 0f), headG + ad * (R * 0.1f), new Vector2(1.9f * R, 1.5f * R), NightOverlayOrder + 1, ang), 0.38f * inten);
                RegNightGlow(SpriteFactory.Make("LampPoolCore", parent, SpriteFactory.Soft, new Color(light.r, light.g, light.b, 0f), headG + ad * (R * 0.05f), new Vector2(0.9f * R, 0.7f * R), NightOverlayOrder + 1, ang), 0.3f * inten);
                RegNightGlow(SpriteFactory.Make("LampLensOn", parent, SpriteFactory.Square, new Color(Mathf.Lerp(light.r, 1f, 0.5f), Mathf.Lerp(light.g, 1f, 0.5f), Mathf.Lerp(light.b, 1f, 0.5f), 0f), head + ad * 0.01f * k, new Vector2(0.11f, 0.045f) * k, NightOverlayOrder + 3, ang), 1f);
                RegNightGlow(SpriteFactory.Make("LampGlow", parent, SpriteFactory.Soft, new Color(light.r, light.g, light.b, 0f), head + ad * 0.01f * k, new Vector2(0.26f, 0.16f) * k, NightOverlayOrder + 2, ang), 0.45f);
            }
        }

        /// <summary>
        /// Luz de inundación como la del original: poste con «Altura» (1 a 10 niveles) que sube en pantalla y arriba un aro
        /// con una cruz y ocho focos alrededor. Sombra del poste según la altura; de noche alumbra un círculo grande.
        /// </summary>
        public static int FloodLevels(DecorItem d) { return d.height <= 1 ? 6 : Mathf.Clamp(d.height, 1, 10); }   // los antiguos (1) se ven medianos

        void DrawRoundLamp(Transform parent, DecorItem d, Vector2 p)
        {
            float k = Mathf.Clamp(d.size, 0.4f, 3f);
            Color light = LampColors[Mathf.Abs(d.color) % LampColors.Length];
            var metal = new Color(0.4f, 0.42f, 0.46f, 1f);
            int lv = FloodLevels(d);
            float Z = 0.075f * lv * k, R = 0.16f * k;
            Vector2 top = p + new Vector2(0f, Z), sh = new Vector2(-0.6f * Z, 0f);
            RegPoleShadow(Bar(parent, "LampPoleShadow", p, p + sh, 0.03f * k, new Color(0f, 0f, 0f, 0.22f), 3), p, p + sh);
            RegShadow(SpriteFactory.Make("RingShadow", parent, SpriteFactory.Ring, new Color(0f, 0f, 0f, 0.18f), p + sh, Vector2.one * R * 2.1f, 3), p);
            SpriteFactory.Make("LampBase", parent, SpriteFactory.Circle, new Color(0.25f, 0.26f, 0.29f, 1f), p, Vector2.one * 0.08f * k, 7);
            Bar(parent, "LampPole", p, top, 0.04f * k, metal, 8);
            SpriteFactory.Make("FloodRing", parent, SpriteFactory.Ring, metal, top, Vector2.one * R * 2.1f, 9);
            Bar(parent, "FloodCrossA", top - new Vector2(R, 0f), top + new Vector2(R, 0f), 0.035f * k, metal, 9);
            Bar(parent, "FloodCrossB", top - new Vector2(0f, R), top + new Vector2(0f, R), 0.035f * k, metal, 9);
            for (int i = 0; i < 8; i++)
            {
                float a = (22.5f + 45f * i) * Mathf.Deg2Rad;
                Vector2 g = top + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * R * 1.02f;
                SpriteFactory.Make("FloodBulb", parent, SpriteFactory.Circle, Color.Lerp(light, Color.white, 0.6f), g, Vector2.one * 0.045f * k, 10);
                RegNightGlow(SpriteFactory.Make("FloodBulbOn", parent, SpriteFactory.Soft, new Color(light.r, light.g, light.b, 0f), g, Vector2.one * 0.16f * k, NightOverlayOrder + 2), 0.5f);
            }
            float reach = LampReach(d) * (0.6f + 0.08f * lv);
            RegNightGlow(SpriteFactory.Make("LampPool", parent, SpriteFactory.Soft, new Color(light.r, light.g, light.b, 0f), p, Vector2.one * 1.6f * reach, NightOverlayOrder + 1), 0.36f);
        }

        void UpdateDayNight()
        {
            bool active = DayNight.Active && !curEditor;
            if (!active && !lastActive) { if (nightOverlay != null) nightOverlay.enabled = false; return; }
            if (active == lastActive && Mathf.Abs(Mathf.DeltaAngle(DayNight.Hour * 15f, lastHour * 15f)) < 0.3f) return;
            lastActive = active;
            lastHour = DayNight.Hour;
            float dark = active ? DayNight.Darkness : 0f;

            // Capa de noche.
            if (nightOverlay == null)
            {
                nightOverlay = SpriteFactory.Make("Night", transform, SpriteFactory.Square, new Color(0.03f, 0.06f, 0.16f, 0f), new Vector2(100f, 100f), Vector2.one * 3000f, NightOverlayOrder);
            }
            nightOverlay.enabled = dark > 0.01f;
            nightOverlay.color = new Color(0.02f, 0.22f, 0.3f, 0.6f * dark);

            // Sombras del sol.
            Vector2 dir = active ? DayNight.ShadowDir : new Vector2(-1f, -0.12f).normalized;
            float L = active ? DayNight.ShadowLength : 1f, sa = active ? DayNight.ShadowAlpha : 1f;
            for (int i = sunShadows.Count - 1; i >= 0; i--)
            {
                var e = sunShadows[i];
                if (e.t == null) { sunShadows.RemoveAt(i); continue; }
                Vector2 off = dir * (e.len0 * L);
                if (e.kind == 0) e.t.localPosition = new Vector3(e.a.x + off.x, e.a.y + off.y, 0f);
                else
                {
                    Vector2 p1 = e.kind == 1 ? e.a : e.a + off, p2 = e.kind == 1 ? e.a + off : e.b + off;
                    Vector2 v = p2 - p1;
                    e.t.localPosition = new Vector3((p1.x + p2.x) * 0.5f, (p1.y + p2.y) * 0.5f, 0f);
                    e.t.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg);
                    e.t.localScale = new Vector3(Mathf.Max(0.001f, v.magnitude), e.w, 1f);
                }
                var c = e.sr.color; c.a = e.alpha0 * sa; e.sr.color = c;
            }

            // Luces de noche.
            for (int i = nightGlows.Count - 1; i >= 0; i--)
            {
                var g = nightGlows[i];
                if (g == null) { nightGlows.RemoveAt(i); nightGlowAlpha.RemoveAt(i); continue; }
                var c = g.color; c.a = nightGlowAlpha[i] * dark; g.color = c;
            }
        }
    }
}
