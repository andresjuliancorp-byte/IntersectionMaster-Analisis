using UnityEngine;

namespace IntersectionMaster.Core
{
    /// <summary>Genera sprites básicos por código para no depender de arte externo.</summary>
    public static class SpriteFactory
    {
        static Sprite square, circle, ring;

        public static Sprite Square
        {
            get
            {
                if (square == null)
                {
                    var t = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                    var px = new Color32[16];
                    for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
                    t.SetPixels32(px);
                    t.filterMode = FilterMode.Bilinear;
                    t.Apply();
                    square = Sprite.Create(t, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
                }
                return square;
            }
        }

        public static Sprite Circle
        {
            get
            {
                if (circle == null) circle = MakeDisc(64, 0f);
                return circle;
            }
        }

        public static Sprite Ring
        {
            get
            {
                if (ring == null) ring = MakeDisc(64, 0.78f);
                return ring;
            }
        }

        static Sprite soft;

        /// <summary>Resplandor suave: blanco en el centro que se desvanece sin borde (brillo de los semáforos).</summary>
        public static Sprite Soft
        {
            get
            {
                if (soft == null)
                {
                    const int size = 64;
                    var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
                    var px = new Color32[size * size];
                    float r = size * 0.5f;
                    for (int y = 0; y < size; y++)
                        for (int x = 0; x < size; x++)
                        {
                            float dx = x + 0.5f - r, dy = y + 0.5f - r;
                            float d = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy) / r);
                            float a = (1f - d) * (1f - d) * (3f - 2f * (1f - d));   // caída suave, 0 en el borde
                            px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                        }
                    t.SetPixels32(px);
                    t.filterMode = FilterMode.Bilinear;
                    t.Apply();
                    soft = Sprite.Create(t, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
                }
                return soft;
            }
        }

        static Sprite cone;

        /// <summary>
        /// Haz de luz en abanico (faros de noche, como el original): nace en el borde izquierdo (pivote) y se abre y se
        /// desvanece hacia +X. Ocupa 1x1 unidad.
        /// </summary>
        public static Sprite Cone
        {
            get
            {
                if (cone == null)
                {
                    const int w = 64, h = 64;
                    var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
                    var px = new Color32[w * h];
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                        {
                            float u = (x + 0.5f) / w;                       // 0 en el faro, 1 al final del haz
                            float v = Mathf.Abs((y + 0.5f) / h - 0.5f) * 2f; // 0 en el centro, 1 en el borde
                            float spread = 0.12f + 0.88f * Mathf.Sqrt(u);    // se abre con la distancia desde el faro
                            float across = Mathf.Clamp01(1f - v / spread);
                            float along = Mathf.Clamp01(u * 14f) * Mathf.Pow(1f - u, 1.6f);
                            float a = across * (0.6f + 0.4f * across) * along * 1.25f;
                            px[y * w + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
                        }
                    t.SetPixels32(px);
                    t.filterMode = FilterMode.Bilinear;
                    t.wrapMode = TextureWrapMode.Clamp;
                    t.Apply();
                    cone = Sprite.Create(t, new Rect(0, 0, w, h), new Vector2(0f, 0.5f), w);
                }
                return cone;
            }
        }

        static Sprite triangle, octagon, quarter;

        /// <summary>Triángulo isósceles apuntando hacia +X (base a la izquierda), ocupa 1x1 unidad.</summary>
        public static Sprite Triangle
        {
            get
            {
                if (triangle == null)
                    triangle = MakeShape(64, (x, y) => Mathf.Abs(y - 0.5f) <= (1f - x) * 0.5f);
                return triangle;
            }
        }

        /// <summary>Octágono regular inscrito en 1x1 unidad (señal de stop).</summary>
        public static Sprite Octagon
        {
            get
            {
                if (octagon == null)
                    octagon = MakeShape(64, (x, y) =>
                    {
                        float dx = Mathf.Abs(x - 0.5f), dy = Mathf.Abs(y - 0.5f);
                        return dx <= 0.5f && dy <= 0.5f && dx + dy <= 0.5f * 1.4142f;
                    });
                return octagon;
            }
        }

        /// <summary>
        /// Cuarto de disco de radio 1 unidad con el centro en la esquina superior derecha del sprite.
        /// Sirve para las curvas: la esquina exterior de la calzada queda redondeada.
        /// </summary>
        public static Sprite QuarterDisc
        {
            get
            {
                if (quarter == null)
                    quarter = MakeShape(96, (x, y) => (1f - x) * (1f - x) + (1f - y) * (1f - y) <= 1f);
                return quarter;
            }
        }

        // Forma rellena a partir de una función (x,y en 0..1) -> dentro/fuera, con un suavizado simple (4 muestras).
        static Sprite MakeShape(int size, System.Func<float, float, bool> inside)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int hits = 0;
                    for (int sy = 0; sy < 2; sy++)
                        for (int sx = 0; sx < 2; sx++)
                            if (inside((x + 0.25f + 0.5f * sx) / size, (y + 0.25f + 0.5f * sy) / size)) hits++;
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(hits * 63));
                }
            }
            t.SetPixels32(px);
            t.filterMode = FilterMode.Bilinear;
            t.Apply();
            return Sprite.Create(t, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }

        // Disco de diámetro 1 unidad. innerRatio > 0 genera un anillo.
        static Sprite MakeDisc(int size, float innerRatio)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color32[size * size];
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - r, dy = y + 0.5f - r;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / r;
                    float a = Mathf.Clamp01((1f - d) * r);
                    if (innerRatio > 0f) a = Mathf.Min(a, Mathf.Clamp01((d - innerRatio) * r));
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            t.SetPixels32(px);
            t.filterMode = FilterMode.Bilinear;
            t.Apply();
            return Sprite.Create(t, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }

        public static SpriteRenderer Make(string name, Transform parent, Sprite sprite, Color color,
            Vector2 pos, Vector2 size, int order, float rotationDeg = 0f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(pos.x, pos.y, 0f);
            go.transform.localRotation = Quaternion.Euler(0f, 0f, rotationDeg);
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = order;
            return sr;
        }

        public static Color Hex(string hex, float alpha = 1f)
        {
            Color c;
            if (!ColorUtility.TryParseHtmlString(hex, out c)) c = Color.magenta;
            c.a = alpha;
            return c;
        }
    }
}
