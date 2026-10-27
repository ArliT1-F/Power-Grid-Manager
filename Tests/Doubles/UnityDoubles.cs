// Minimal UnityEngine contracts used only by the offline verification harness.
// Signatures mirror the members the mod actually calls; this is NOT a substitute for
// compiling against the real Unity assemblies shipped with RimWorld.
using System;

namespace UnityEngine
{
    public struct Vector2
    {
        public float x;
        public float y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => new Vector2(0f, 0f);
        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.x + b.x, a.y + b.y);
    }

    public struct Rect
    {
        public float x;
        public float y;
        public float width;
        public float height;

        public Rect(float x, float y, float width, float height)
        {
            this.x = x;
            this.y = y;
            this.width = width;
            this.height = height;
        }

        public static Rect zero => new Rect(0f, 0f, 0f, 0f);

        public float xMin { get => x; set { width += x - value; x = value; } }
        public float yMin { get => y; set { height += y - value; y = value; } }
        public float xMax { get => x + width; set { width = value - x; } }
        public float yMax { get => y + height; set { height = value - y; } }
        public Vector2 center => new Vector2(x + width / 2f, y + height / 2f);

        public bool Contains(Vector2 point)
        {
            return point.x >= x && point.x <= xMax && point.y >= y && point.y <= yMax;
        }
    }

    public struct Color
    {
        public float r;
        public float g;
        public float b;
        public float a;

        public Color(float r, float g, float b, float a = 1f)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = a;
        }

        public static Color white => new Color(1f, 1f, 1f, 1f);
        public static Color black => new Color(0f, 0f, 0f, 1f);
        public static Color gray => new Color(0.5f, 0.5f, 0.5f, 1f);
        public static Color clear => new Color(0f, 0f, 0f, 0f);

        public static bool operator ==(Color lhs, Color rhs) =>
            Math.Abs(lhs.r - rhs.r) < 1e-5f &&
            Math.Abs(lhs.g - rhs.g) < 1e-5f &&
            Math.Abs(lhs.b - rhs.b) < 1e-5f &&
            Math.Abs(lhs.a - rhs.a) < 1e-5f;

        public static bool operator !=(Color lhs, Color rhs) => !(lhs == rhs);

        public override bool Equals(object obj) => obj is Color other && this == other;
        public override int GetHashCode() => r.GetHashCode() ^ (g.GetHashCode() << 2) ^ (b.GetHashCode() >> 2) ^ (a.GetHashCode() >> 1);
    }

    public class Texture2D { }

    public enum TextAnchor
    {
        UpperLeft,
        UpperCenter,
        UpperRight,
        MiddleLeft,
        MiddleCenter,
        MiddleRight,
        LowerLeft,
        LowerCenter,
        LowerRight
    }

    public enum GameFont
    {
        Tiny,
        Small,
        Medium,
        Large
    }

    public static class Mathf
    {
        public static float Min(float a, float b) => a < b ? a : b;
        public static int Min(int a, int b) => a < b ? a : b;
        public static float Max(float a, float b) => a > b ? a : b;
        public static int Max(int a, int b) => a > b ? a : b;
        public static float Abs(float value) => Math.Abs(value);
        public static int Abs(int value) => Math.Abs(value);
        public static float Clamp(float value, float min, float max) => value < min ? min : value > max ? max : value;
        public static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
        public static float Clamp01(float value) => Clamp(value, 0f, 1f);
        public static float Floor(float value) => (float)Math.Floor(value);
        public static int RoundToInt(float value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
        public static int FloorToInt(float value) => (int)Math.Floor(value);
        public static int CeilToInt(float value) => (int)Math.Ceiling(value);
        public static float Sqrt(float value) => (float)Math.Sqrt(value);
        public static float Atan2(float y, float x) => (float)Math.Atan2(y, x);
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
    }

    public static class GUI
    {
        public static Color color = Color.white;
        public static void Label(Rect rect, string text) { }
        public static void DrawTexture(Rect rect, Texture2D texture) { }
        public static void BeginGroup(Rect rect) { }
        public static void EndGroup() { }
    }
}
