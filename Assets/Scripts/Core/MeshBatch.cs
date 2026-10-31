using System.Collections.Generic;
using UnityEngine;

namespace IntersectionMaster.Core
{
    /// <summary>
    /// Acumula triángulos con color por vértice y los convierte en una única malla (un solo objeto y una sola
    /// llamada de dibujo). Se usa para calzadas, marcas viales, bordillos y flechas, que no caben en sprites.
    /// </summary>
    public class MeshBatch
    {
        static Material material;

        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Color> cols = new List<Color>();
        readonly List<int> tris = new List<int>();

        public int VertexCount { get { return verts.Count; } }

        static Material Mat
        {
            get
            {
                if (material == null)
                {
                    Shader sh = Shader.Find("Sprites/Default");
                    if (sh == null) sh = Shader.Find("UI/Default");
                    material = new Material(sh);
                }
                return material;
            }
        }

        int AddV(Vector2 p, Color c)
        {
            verts.Add(new Vector3(p.x, p.y, 0f));
            cols.Add(c);
            return verts.Count - 1;
        }

        public void Tri(Vector2 a, Vector2 b, Vector2 c, Color col)
        {
            int i = AddV(a, col);
            AddV(b, col);
            AddV(c, col);
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
        }

        public void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color col)
        {
            Tri(a, b, c, col);
            Tri(a, c, d, col);
        }

        /// <summary>Rectángulo orientado: centro, dirección de la longitud, largo y ancho.</summary>
        public void Rect(Vector2 center, Vector2 dir, float length, float width, Color col)
        {
            Vector2 f = dir.normalized * (length * 0.5f);
            Vector2 s = Geo.Right(dir.normalized) * (width * 0.5f);
            Quad(center - f - s, center + f - s, center + f + s, center - f + s, col);
        }

        /// <summary>Banda entre dos listas de puntos de igual tamaño (borde izquierdo y derecho).</summary>
        public void Strip(IList<Vector2> left, IList<Vector2> right, Color col)
        {
            int n = Mathf.Min(left.Count, right.Count);
            for (int i = 0; i < n - 1; i++) Quad(left[i], left[i + 1], right[i + 1], right[i], col);
        }

        /// <summary>Abanico desde center por los puntos del contorno (en orden); cierra el contorno.</summary>
        public void Fan(Vector2 center, IList<Vector2> ring, Color col)
        {
            int n = ring.Count;
            if (n < 2) return;
            for (int i = 0; i < n; i++) Tri(center, ring[i], ring[(i + 1) % n], col);
        }

        public void Disc(Vector2 c, float r, Color col, int seg = 32)
        {
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg;
                Tri(c, c + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * r, c + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * r, col);
            }
        }

        public void Annulus(Vector2 c, float rIn, float rOut, Color col, int seg = 48)
        {
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg;
                Vector2 d0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)), d1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));
                Quad(c + d0 * rIn, c + d0 * rOut, c + d1 * rOut, c + d1 * rIn, col);
            }
        }

        /// <summary>Línea gruesa que sigue una polilínea, con desplazamiento lateral (positivo = derecha) entre s0 y s1.</summary>
        public void Line(Polyline pl, float s0, float s1, float offset, float width, Color col)
        {
            if (s1 <= s0) return;
            int n = Mathf.Max(2, Mathf.CeilToInt((s1 - s0) / 0.1f) + 1);
            var l = new Vector2[n];
            var r = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                float s = Mathf.Lerp(s0, s1, i / (float)(n - 1));
                Vector2 p = pl.PointAt(s);
                Vector2 rt = Geo.Right(pl.TangentAt(s));
                l[i] = p + rt * (offset - width * 0.5f);
                r[i] = p + rt * (offset + width * 0.5f);
            }
            Strip(l, r, col);
        }

        public void Line(Polyline pl, float offset, float width, Color col)
        {
            Line(pl, 0f, pl.Length, offset, width, col);
        }

        /// <summary>Línea discontinua (trazos de longitud dash separados por gap).</summary>
        public void Dashes(Polyline pl, float s0, float s1, float offset, float width, float dash, float gap, Color col)
        {
            float s = s0;
            while (s < s1 - 0.01f)
            {
                float e = Mathf.Min(s + dash, s1);
                Line(pl, s, e, offset, width, col);
                s += dash + gap;
            }
        }

        static readonly Dictionary<Texture2D, Material> texMats = new Dictionary<Texture2D, Material>();

        /// <summary>
        /// Igual que Build, pero con una textura repetida según la posición en el mundo (uvScale repeticiones por unidad)
        /// y un único color (tinte con transparencia). Sirve para el grano del asfalto encima de la calzada.
        /// </summary>
        public GameObject BuildTextured(Transform parent, string name, int sortingOrder, Texture2D tex, float uvScale, Color tint)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            if (verts.Count == 0 || tex == null) return go;
            var mesh = new Mesh();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            var uv = new List<Vector2>(verts.Count);
            var tc = new List<Color>(verts.Count);
            for (int i = 0; i < verts.Count; i++) { uv.Add(new Vector2(verts[i].x * uvScale, verts[i].y * uvScale)); tc.Add(tint); }
            mesh.SetUVs(0, uv);
            mesh.SetColors(tc);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            Material m;
            if (!texMats.TryGetValue(tex, out m) || m == null)
            {
                m = new Material(Mat);
                m.mainTexture = tex;
                texMats[tex] = m;
            }
            mr.sharedMaterial = m;
            mr.sortingOrder = sortingOrder;
            return go;
        }

        public GameObject Build(Transform parent, string name, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            if (verts.Count == 0) return go;
            var mesh = new Mesh();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Mat;
            mr.sortingOrder = sortingOrder;
            return go;
        }
    }
}
