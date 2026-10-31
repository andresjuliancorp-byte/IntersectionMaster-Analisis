// Sustitutos de las partes visuales del juego (solo para la simulación de comprobación).
using UnityEngine;

namespace IntersectionMaster.Core
{
    public static class SpriteFactory
    {
        public static Sprite Square = new Sprite(), Circle = new Sprite(), Ring = new Sprite(), Triangle = new Sprite(), Octagon = new Sprite(), Soft = new Sprite(), Cone = new Sprite();
        public static SpriteRenderer Make(string name, Transform parent, Sprite sprite, Color color, Vector2 pos, Vector2 size, int order, float rotationDeg = 0f)
        {
            return new SpriteRenderer { color = color, sprite = sprite, sortingOrder = order };
        }
        public static Color Hex(string hex, float alpha = 1f)
        {
            hex = hex.TrimStart('#');
            if (hex.Length < 6) return new Color(1, 0, 1, alpha);
            int v = System.Convert.ToInt32(hex.Substring(0, 6), 16);
            return new Color(((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f, alpha);
        }
    }
}

namespace IntersectionMaster.Systems
{
    public static class RewardSystem
    {
        public static string[] CarPalette() { return new[] { "#C9524B", "#4F7FCB", "#E8E8E8" }; }
    }
}
