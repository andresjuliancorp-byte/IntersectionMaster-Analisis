using UnityEngine;

namespace IntersectionMaster.Core
{
    /// <summary>
    /// Entrada unificada de puntero (primer dedo o ratón). Usa la clase Input clásica:
    /// en Unity 6 hay que activar Player Settings > Active Input Handling = "Both" o "Input Manager (Old)".
    /// </summary>
    public static class PointerInput
    {
        static int frame = -1;
        static bool down, held, up, multi;
        static Vector2 position;

        public static bool Down { get { Poll(); return down; } }
        public static bool Held { get { Poll(); return held; } }
        public static bool Up { get { Poll(); return up; } }
        public static bool MultiTouch { get { Poll(); return multi; } }
        public static Vector2 Position { get { Poll(); return position; } }

        static void Poll()
        {
            if (frame == Time.frameCount) return;
            frame = Time.frameCount;
            down = held = up = false;
            multi = Input.touchCount >= 2;

            if (Input.touchCount > 0)
            {
                Touch t = Input.GetTouch(0);
                position = t.position;
                down = t.phase == TouchPhase.Began;
                up = t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled;
                held = !up;
            }
            else
            {
                position = Input.mousePosition;
                down = Input.GetMouseButtonDown(0);
                up = Input.GetMouseButtonUp(0);
                held = Input.GetMouseButton(0);
            }
        }
    }
}
