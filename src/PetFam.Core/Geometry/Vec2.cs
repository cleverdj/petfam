namespace PetFam.Core.Geometry;

public readonly record struct Vec2(float X, float Y)
{
    public static Vec2 Zero => new(0, 0);

    public float Length => MathF.Sqrt(X * X + Y * Y);

    public Vec2 Normalized => Length < 1e-5f ? Zero : this * (1f / Length);

    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator *(Vec2 a, float s) => new(a.X * s, a.Y * s);

    public static float Distance(Vec2 a, Vec2 b) => (a - b).Length;

    public static Vec2 Lerp(Vec2 a, Vec2 b, float t) => a + (b - a) * t;

    // Record structs print every public property; Normalized returns a Vec2, which would recurse forever.
    public override string ToString() => $"({X:F1}, {Y:F1})";
}

/// <summary>Integer rectangle in screen pixels.</summary>
public readonly record struct RectI(int X, int Y, int Width, int Height)
{
    public static RectI Empty => new(0, 0, 0, 0);

    public int Left => X;
    public int Top => Y;
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;
    public Vec2 Center => new(X + Width / 2f, Y + Height / 2f);

    public bool Contains(Vec2 p) => p.X >= Left && p.X < Right && p.Y >= Top && p.Y < Bottom;

    public bool Contains(RectI other) =>
        other.Left >= Left && other.Right <= Right && other.Top >= Top && other.Bottom <= Bottom;

    public RectI Intersect(RectI other)
    {
        var l = System.Math.Max(Left, other.Left);
        var t = System.Math.Max(Top, other.Top);
        var r = System.Math.Min(Right, other.Right);
        var b = System.Math.Min(Bottom, other.Bottom);
        return r <= l || b <= t ? Empty : new RectI(l, t, r - l, b - t);
    }

    public static RectI FromLTRB(int left, int top, int right, int bottom) =>
        new(left, top, right - left, bottom - top);

    public override string ToString() => $"[{X},{Y} {Width}x{Height}]";
}
