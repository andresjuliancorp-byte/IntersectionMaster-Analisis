using UnityEngine;

namespace IntersectionMaster.Core
{
    /// <summary>
    /// Día y noche al jugar: la hora avanza desde la «Hora de inicio» del mapa; de noche la escena se oscurece y se ven las
    /// luces (faros, semáforos, farolas); de día el sol mueve las sombras (largas a la izquierda por la mañana, cortas a
    /// mediodía, largas a la derecha por la tarde). En el editor no está activo: siempre es de día con la sombra de diseño.
    /// </summary>
    public static class DayNight
    {
        /// <summary>Horas de juego por segundo real a velocidad 1: 8 minutos por día como el original, más 1 minuto en el amanecer y 1 en el anochecer (10 min en total).</summary>
        public const float HoursPerSecond = 24f / 480f;

        public static bool Active { get; private set; }
        public static float Hour { get; private set; } = 12f;

        public static void Begin(float startHour) { Hour = Mathf.Repeat(startHour, 24f); Active = true; }
        public static void End() { Active = false; }
        /// <summary>Cambiar la hora mientras se juega (como el original: «el viaje en el tiempo existe»).</summary>
        public static void SetHour(float h) { if (Active) Hour = Mathf.Repeat(h, 24f); }
        /// <summary>Amanecer (5–7 h) y anochecer (17:30–19:30) van más despacio: cada uno dura 1 minuto más (100 s).</summary>
        public const float TransitionHoursPerSecond = 2f / 100f;

        static bool InTransition(float h) { return (h >= 5f && h < 7f) || (h >= 17.5f && h < 19.5f); }

        public static void Advance(float dt)
        {
            if (!Active) return;
            // En pasos pequeños para que el cambio de ritmo al entrar o salir de un amanecer/anochecer sea exacto.
            while (dt > 0f)
            {
                float step = Mathf.Min(dt, 0.25f);
                Hour = Mathf.Repeat(Hour + step * (InTransition(Hour) ? TransitionHoursPerSecond : HoursPerSecond), 24f);
                dt -= step;
            }
        }

        /// <summary>Oscuridad: 0 de día, 1 de noche cerrada (amanece de 5 a 7, anochece de 17:30 a 19:30).</summary>
        public static float Darkness
        {
            get
            {
                if (!Active) return 0f;
                float h = Hour;
                if (h >= 7f && h <= 17.5f) return 0f;
                if (h > 17.5f && h < 19.5f) return Smooth((h - 17.5f) / 2f);
                if (h >= 5f && h < 7f) return 1f - Smooth((h - 5f) / 2f);
                return 1f;
            }
        }

        static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        /// <summary>Ángulo del sol: 0 al salir (6 h), π al ponerse (18 h).</summary>
        static float SunAngle { get { return Mathf.Clamp((Hour - 6f) / 12f, 0.06f, 0.94f) * Mathf.PI; } }

        /// <summary>Dirección de las sombras en el suelo (unitaria). Sin día/noche: hacia la izquierda, la de diseño.</summary>
        public static Vector2 ShadowDir
        {
            get
            {
                if (!Active) return new Vector2(-1f, -0.12f).normalized;
                return new Vector2(-Mathf.Cos(SunAngle), -0.12f).normalized;
            }
        }

        /// <summary>Largo de las sombras respecto al de diseño (1): cortas a mediodía, largas al amanecer y al atardecer.</summary>
        public static float ShadowLength
        {
            get
            {
                if (!Active) return 1f;
                return Mathf.Clamp(0.55f / Mathf.Max(Mathf.Sin(SunAngle), 0.4f), 0.55f, 1.35f);
            }
        }

        /// <summary>Cuánto se ven las sombras del sol (de noche casi nada).</summary>
        public static float ShadowAlpha { get { return 1f - 0.8f * Darkness; } }

        /// <summary>Texto de la hora (HH:MM).</summary>
        public static string Clock
        {
            get
            {
                int hh = Mathf.FloorToInt(Hour), mm = Mathf.FloorToInt((Hour - hh) * 60f);
                return hh.ToString("00") + ":" + mm.ToString("00");
            }
        }
    }
}
