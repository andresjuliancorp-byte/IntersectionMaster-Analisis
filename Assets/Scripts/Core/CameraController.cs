using System;
using UnityEngine;

namespace IntersectionMaster.Core
{
    /// <summary>
    /// Cámara ortográfica con desplazamiento y zoom. Táctil: dos dedos = pellizco para zoom y arrastre;
    /// un dedo = arrastre sólo si AllowSingleFingerPan. Ratón (para pruebas en el editor de Unity):
    /// rueda = zoom, botón derecho/central = arrastre.
    /// </summary>
    public class CameraController : MonoBehaviour
    {
        public Camera cam;
        public bool InputEnabled = true;
        public bool AllowSingleFingerPan = true;
        public Func<Vector2, bool> IsOverUi;

        Rect bounds = new Rect(0f, 0f, 14f, 9f);
        float margin = 1f;
        bool dragFromUi;
        bool mouseDragging;
        Vector2 lastMouse;
        float prevPinchDist;

        /// <summary>Encuadra todo el mapa (editor).</summary>
        public void Fit(float w, float h)
        {
            FitRect(new Rect(0f, 0f, w, h), 1f);
        }

        /// <summary>Encuadra un área (la zona visible al jugar) y limita la cámara a ella.</summary>
        public void FitRect(Rect r, float clampMargin = 0f)
        {
            bounds = r;
            margin = clampMargin;
            float aspect = (float)Screen.width / Mathf.Max(1, Screen.height);
            float size = Mathf.Max(r.height / 2f + 1.2f, (r.width / 2f + 1.2f) / aspect);
            if (aspect < 1f) size *= 0.65f; // en vertical se empieza más cerca, como en la app de referencia
            cam.orthographicSize = size;
            cam.transform.position = new Vector3(r.center.x, r.center.y - 0.3f, -10f);
        }

        /// <summary>Encuadra r pero deja moverse por todo «limits» (editor de mapas grandes).</summary>
        public void FitView(Rect r, Rect limits)
        {
            FitRect(r, 1f);
            bounds = limits;
        }

        /// <summary>Centra la cámara en un punto del mundo (por ejemplo, el elemento de un aviso o el objeto seleccionado).</summary>
        public void CenterOn(Vector2 p)
        {
            cam.transform.position = new Vector3(p.x, p.y, cam.transform.position.z);
        }

        public Vector2 ScreenToWorld(Vector2 screen)
        {
            Vector3 p = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -cam.transform.position.z));
            return new Vector2(p.x, p.y);
        }

        void Update()
        {
            if (cam == null || !InputEnabled) return;

            if (Input.touchCount >= 2)
            {
                Touch a = Input.GetTouch(0), b = Input.GetTouch(1);
                Vector2 mid = (a.position + b.position) * 0.5f;
                Vector2 midPrev = ((a.position - a.deltaPosition) + (b.position - b.deltaPosition)) * 0.5f;
                float dist = Vector2.Distance(a.position, b.position);
                float distPrev = Vector2.Distance(a.position - a.deltaPosition, b.position - b.deltaPosition);
                if (distPrev > 1f && dist > 1f) Zoom(distPrev / dist);
                PanPixels(mid - midPrev);
                dragFromUi = true; // evita que el dedo restante arrastre al soltar
            }
            else if (Input.touchCount == 1)
            {
                Touch t = Input.GetTouch(0);
                if (t.phase == TouchPhase.Began) dragFromUi = IsOverUi != null && IsOverUi(t.position);
                if (AllowSingleFingerPan && !dragFromUi && t.phase == TouchPhase.Moved) PanPixels(t.deltaPosition);
            }
            else
            {
                HandleMouse();
            }
            Clamp();
        }

        void HandleMouse()
        {
            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > 0.01f && (IsOverUi == null || !IsOverUi(Input.mousePosition))) Zoom(1f - wheel * 0.1f);

            Vector2 m = Input.mousePosition;
            if (Input.GetMouseButtonDown(0)) { dragFromUi = IsOverUi != null && IsOverUi(m); }
            bool panButton = Input.GetMouseButton(1) || Input.GetMouseButton(2) || (AllowSingleFingerPan && Input.GetMouseButton(0) && !dragFromUi);
            if (panButton)
            {
                if (mouseDragging) PanPixels(m - lastMouse);
                mouseDragging = true;
            }
            else mouseDragging = false;
            lastMouse = m;
        }

        void Zoom(float factor)
        {
            float maxSize = Mathf.Max(bounds.width, bounds.height) * 0.9f + 2f;
            cam.orthographicSize = Mathf.Clamp(cam.orthographicSize * factor, 0.85f, maxSize);
        }

        void PanPixels(Vector2 deltaPixels)
        {
            float worldPerPixel = 2f * cam.orthographicSize / Screen.height;
            var p = cam.transform.position;
            p.x -= deltaPixels.x * worldPerPixel;
            p.y -= deltaPixels.y * worldPerPixel;
            cam.transform.position = p;
        }

        void Clamp()
        {
            var p = cam.transform.position;
            p.x = Mathf.Clamp(p.x, bounds.xMin - margin, bounds.xMax + margin);
            p.y = Mathf.Clamp(p.y, bounds.yMin - margin, bounds.yMax + margin);
            cam.transform.position = p;
        }
    }
}
