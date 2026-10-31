using UnityEngine;

namespace IntersectionMaster.Core
{
    /// <summary>Lluvia o nieve ligera que cae sobre la vista de la cámara (solo decoración).</summary>
    public class WeatherFx : MonoBehaviour
    {
        const int Count = 90;

        Transform[] drops;
        Vector2[] vel;
        int kind;
        Camera cam;

        /// <summary>0 = despejado, 1 = lluvia, 2 = nieve.</summary>
        public void Setup(int weather)
        {
            kind = weather;
            if (kind == 0) { enabled = false; return; }
            drops = new Transform[Count];
            vel = new Vector2[Count];
            for (int i = 0; i < Count; i++)
            {
                SpriteRenderer sr;
                if (kind == 1)
                    sr = SpriteFactory.Make("Drop", transform, SpriteFactory.Square, new Color(0.85f, 0.92f, 1f, 0.45f), Vector2.zero, new Vector2(0.015f, 0.16f), 45, 8f);
                else
                    sr = SpriteFactory.Make("Flake", transform, SpriteFactory.Circle, new Color(1f, 1f, 1f, 0.8f), Vector2.zero, Vector2.one * Random.Range(0.04f, 0.09f), 45);
                drops[i] = sr.transform;
                vel[i] = kind == 1 ? new Vector2(-1.2f, -Random.Range(9f, 13f)) : new Vector2(Random.Range(-0.4f, 0.4f), -Random.Range(0.8f, 1.6f));
                drops[i].position = new Vector3(0f, -1000f, 0f);
            }
        }

        void Update()
        {
            if (drops == null) return;
            if (cam == null) cam = Camera.main;
            if (cam == null) return;
            float h = cam.orthographicSize;
            float w = h * cam.aspect;
            Vector3 c = cam.transform.position;
            float dt = Time.deltaTime;
            for (int i = 0; i < drops.Length; i++)
            {
                Vector3 p = drops[i].position;
                if (p.y < -900f || p.y < c.y - h - 0.3f || Mathf.Abs(p.x - c.x) > w + 0.5f)
                {
                    p = new Vector3(c.x + Random.Range(-w, w), c.y + Random.Range(-h, h + 0.5f), 0f);
                }
                else
                {
                    p.x += vel[i].x * dt;
                    p.y += vel[i].y * dt;
                    if (kind == 2) p.x += Mathf.Sin(Time.time * 1.5f + i) * 0.3f * dt;
                }
                drops[i].position = p;
            }
        }
    }
}
