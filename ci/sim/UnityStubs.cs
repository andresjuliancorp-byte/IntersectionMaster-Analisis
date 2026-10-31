// Sustitutos mínimos de UnityEngine para ejecutar la lógica del juego (mapas, red, tráfico) fuera de Unity.
// Solo los usa la simulación de comprobación de GitHub Actions; no forman parte del juego.
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Reflection;

namespace UnityEngine
{
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => new Vector2(0, 0);
        public static Vector2 one => new Vector2(1, 1);
        public static Vector2 right => new Vector2(1, 0);
        public static Vector2 left => new Vector2(-1, 0);
        public static Vector2 up => new Vector2(0, 1);
        public static Vector2 down => new Vector2(0, -1);
        public float sqrMagnitude => x * x + y * y;
        public float magnitude => (float)Math.Sqrt(x * x + y * y);
        public Vector2 normalized { get { float m = magnitude; return m > 1e-5f ? new Vector2(x / m, y / m) : zero; } }
        public void Normalize() { var n = normalized; x = n.x; y = n.y; }
        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, a.y - b.y);
        public static Vector2 operator -(Vector2 a) => new Vector2(-a.x, -a.y);
        public static Vector2 operator *(Vector2 a, float d) => new Vector2(a.x * d, a.y * d);
        public static Vector2 operator *(float d, Vector2 a) => new Vector2(a.x * d, a.y * d);
        public static Vector2 operator *(Vector2 a, Vector2 b) => new Vector2(a.x * b.x, a.y * b.y);
        public static Vector2 operator /(Vector2 a, float d) => new Vector2(a.x / d, a.y / d);
        public static bool operator ==(Vector2 a, Vector2 b) => (a - b).sqrMagnitude < 9.99999944E-11f;
        public static bool operator !=(Vector2 a, Vector2 b) => !(a == b);
        public override bool Equals(object o) => o is Vector2 v && v.x == x && v.y == y;
        public override int GetHashCode() => x.GetHashCode() ^ (y.GetHashCode() << 2);
        public override string ToString() => "(" + x.ToString("0.00") + ", " + y.ToString("0.00") + ")";
        public static float Distance(Vector2 a, Vector2 b) => (a - b).magnitude;
        public static float Dot(Vector2 a, Vector2 b) => a.x * b.x + a.y * b.y;
        public static Vector2 Lerp(Vector2 a, Vector2 b, float t) { t = Mathf.Clamp01(t); return new Vector2(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t); }
        public static float Angle(Vector2 from, Vector2 to)
        {
            float den = (float)Math.Sqrt(from.sqrMagnitude * to.sqrMagnitude);
            if (den < 1E-15f) return 0f;
            float dot = Mathf.Clamp(Dot(from, to) / den, -1f, 1f);
            return (float)Math.Acos(dot) * Mathf.Rad2Deg;
        }
        public static float SignedAngle(Vector2 from, Vector2 to) => Angle(from, to) * Math.Sign(from.x * to.y - from.y * to.x);
        public static Vector2 ClampMagnitude(Vector2 v, float max) { float m = v.magnitude; return m > max ? v / m * max : v; }
        public static Vector2 MoveTowards(Vector2 c, Vector2 t, float d) { var diff = t - c; float m = diff.magnitude; if (m <= d || m == 0f) return t; return c + diff / m * d; }
        public static implicit operator Vector3(Vector2 v) => new Vector3(v.x, v.y, 0f);
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 one => new Vector3(1, 1, 1);
        public static Vector3 zero => new Vector3(0, 0, 0);
        public static Vector3 operator *(Vector3 a, float d) => new Vector3(a.x * d, a.y * d, a.z * d);
        public static implicit operator Vector2(Vector3 v) => new Vector2(v.x, v.y);
    }

    public struct Vector4
    {
        public float x, y, z, w;
        public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
    }

    public struct Quaternion
    {
        public float x, y, z, w;
        public static Quaternion identity => new Quaternion { w = 1 };
        public static Quaternion Euler(float x, float y, float z) => new Quaternion { z = z, w = 1 };
    }

    public static class Mathf
    {
        public const float PI = (float)Math.PI;
        public const float Deg2Rad = PI / 180f;
        public const float Rad2Deg = 180f / PI;
        public const float Infinity = float.PositiveInfinity;
        public const float Epsilon = float.Epsilon;
        public static float Abs(float f) => Math.Abs(f);
        public static int Abs(int f) => Math.Abs(f);
        public static float Sqrt(float f) => (float)Math.Sqrt(f);
        public static float Sin(float f) => (float)Math.Sin(f);
        public static float Cos(float f) => (float)Math.Cos(f);
        public static float Atan2(float y, float x) => (float)Math.Atan2(y, x);
        public static float Pow(float a, float b) => (float)Math.Pow(a, b);
        public static float Log(float f) => (float)Math.Log(f);
        public static float Exp(float f) => (float)Math.Exp(f);
        public static float Sign(float f) => f >= 0f ? 1f : -1f;
        public static float Min(float a, float b) => a < b ? a : b;
        public static float Max(float a, float b) => a > b ? a : b;
        public static int Min(int a, int b) => a < b ? a : b;
        public static int Max(int a, int b) => a > b ? a : b;
        public static float Min(params float[] v) { float m = v[0]; foreach (var x in v) if (x < m) m = x; return m; }
        public static float Max(params float[] v) { float m = v[0]; foreach (var x in v) if (x > m) m = x; return m; }
        public static float Clamp(float v, float a, float b) => v < a ? a : (v > b ? b : v);
        public static int Clamp(int v, int a, int b) => v < a ? a : (v > b ? b : v);
        public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        public static float Round(float f) => (float)Math.Round(f);
        public static int RoundToInt(float f) => (int)Math.Round(f);
        public static int FloorToInt(float f) => (int)Math.Floor(f);
        public static int CeilToInt(float f) => (int)Math.Ceiling(f);
        public static float Floor(float f) => (float)Math.Floor(f);
        public static float Ceil(float f) => (float)Math.Ceiling(f);
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static bool Approximately(float a, float b) => Math.Abs(b - a) < Math.Max(1E-06f * Math.Max(Math.Abs(a), Math.Abs(b)), Epsilon * 8f);
        public static float Repeat(float t, float l) => Clamp(t - Floor(t / l) * l, 0f, l);
        public static float DeltaAngle(float c, float t) { float n = Repeat(t - c, 360f); if (n > 180f) n -= 360f; return n; }
        public static float MoveTowards(float c, float t, float d) => Math.Abs(t - c) <= d ? t : c + Math.Sign(t - c) * d;
        public static float MoveTowardsAngle(float c, float t, float d) { float delta = DeltaAngle(c, t); if (-d < delta && delta < d) return t; t = c + delta; return MoveTowards(c, t, d); }
    }

    public static class Random
    {
        public static System.Random rng = new System.Random(12345);
        public static float value => (float)rng.NextDouble();
        public static float Range(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        public static int Range(int a, int b) => b <= a ? a : rng.Next(a, b);
    }

    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float w, float h) { this.x = x; this.y = y; width = w; height = h; }
        public float xMin => x; public float yMin => y; public float xMax => x + width; public float yMax => y + height;
        public Vector2 center => new Vector2(x + width / 2f, y + height / 2f);
        public Vector2 size => new Vector2(width, height);
        public Vector2 min => new Vector2(xMin, yMin);
        public Vector2 max => new Vector2(xMax, yMax);
        public Vector2 position => new Vector2(x, y);
        public bool Overlaps(Rect o) => o.xMax > xMin && o.xMin < xMax && o.yMax > yMin && o.yMin < yMax;
        public bool Contains(Vector2 p) => p.x >= xMin && p.x < xMax && p.y >= yMin && p.y < yMax;
        public static Rect MinMaxRect(float x0, float y0, float x1, float y1) => new Rect(x0, y0, x1 - x0, y1 - y0);
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a = 1f) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white => new Color(1, 1, 1); public static Color black => new Color(0, 0, 0);
        public static Color red => new Color(1, 0, 0); public static Color green => new Color(0, 1, 0);
        public static Color blue => new Color(0, 0, 1); public static Color yellow => new Color(1, 0.92f, 0.016f);
        public static Color magenta => new Color(1, 0, 1); public static Color clear => new Color(0, 0, 0, 0);
        public static Color gray => new Color(0.5f, 0.5f, 0.5f);
        public static Color Lerp(Color x, Color y, float t) { t = Mathf.Clamp01(t); return new Color(x.r + (y.r - x.r) * t, x.g + (y.g - x.g) * t, x.b + (y.b - x.b) * t, x.a + (y.a - x.a) * t); }
        public static Color operator *(Color c, float d) => new Color(c.r * d, c.g * d, c.b * d, c.a * d);
        public static implicit operator Color(Color32 c) => new Color(c.r / 255f, c.g / 255f, c.b / 255f, c.a / 255f);
    }

    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static implicit operator Color32(Color c) => new Color32((byte)(Mathf.Clamp01(c.r) * 255), (byte)(Mathf.Clamp01(c.g) * 255), (byte)(Mathf.Clamp01(c.b) * 255), (byte)(Mathf.Clamp01(c.a) * 255));
    }

    public class Object { public string name; public static void Destroy(object o) { } }
    public class Component : Object { Transform tr; public Transform transform { get { if (tr == null) tr = this as Transform ?? new Transform(); return tr; } } public GameObject gameObject; }
    public class Transform : Component
    {
        public Vector3 position, localPosition, localScale;
        public Quaternion rotation, localRotation;
        public void SetParent(Transform p, bool worldStays) { }
        public void SetParent(Transform p) { }
    }
    public class Sprite : Object { public static Sprite Create(Texture2D t, Rect r, Vector2 pivot, float ppu) => new Sprite(); }
    public class SpriteRenderer : Component { public Color color; public Sprite sprite; public int sortingOrder; public bool enabled = true; }
    public class GameObject : Object
    {
        public Transform transform = new Transform();
        readonly List<Component> comps = new List<Component>();
        public GameObject(string n) { name = n; }
        public T AddComponent<T>() where T : Component, new() { var c = new T(); c.gameObject = this; comps.Add(c); return c; }
        public T[] GetComponentsInChildren<T>() where T : Component { return new T[0]; }
        public void SetActive(bool v) { }
    }
    public enum TextureFormat { RGBA32 }
    public enum FilterMode { Point, Bilinear }
    public class Texture2D : Object
    {
        public FilterMode filterMode;
        public Texture2D(int w, int h, TextureFormat f, bool mip) { }
        public void SetPixels32(Color32[] p) { }
        public void Apply() { }
        public byte[] EncodeToPNG() => new byte[0];
        public bool LoadImage(byte[] d) => false;
    }
    public static class Screen { public static int width = 1080, height = 2340; }
    public static class Application { public static string persistentDataPath = System.IO.Path.GetTempPath(); }
    public static class Debug { public static void Log(object o) { } public static void LogWarning(object o) { } public static void LogError(object o) { } }

    /// <summary>JSON como el de Unity: solo campos públicos (y los de clases [Serializable]), nada de propiedades.</summary>
    public static class JsonUtility
    {
        class FieldsOnly : DefaultContractResolver
        {
            protected override List<MemberInfo> GetSerializableMembers(Type t)
            {
                var list = new List<MemberInfo>();
                foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance)) if (!f.IsInitOnly) list.Add(f);
                return list;
            }
            protected override JsonProperty CreateProperty(MemberInfo m, MemberSerialization ms)
            {
                var p = base.CreateProperty(m, ms);
                p.Writable = true; p.Readable = true;
                return p;
            }
        }
        static readonly JsonSerializerSettings S = new JsonSerializerSettings
        {
            ContractResolver = new FieldsOnly(),
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            NullValueHandling = NullValueHandling.Ignore,
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore
        };
        public static string ToJson(object o, bool pretty = false) => JsonConvert.SerializeObject(o, pretty ? Formatting.Indented : Formatting.None, S);
        public static T FromJson<T>(string json) => JsonConvert.DeserializeObject<T>(json, S);
    }
}
