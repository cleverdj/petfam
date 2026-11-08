using PetFam.Core.Geometry;

namespace PetFam.Core.World;

/// <summary>
/// Grid over one monitor saying what each spot of the wallpaper is (plan §6.2 ③ automatic habitat recognition):
/// floor kinds pets can stand on (ground, mud, sand, sun, shelter), water they can swim in, and everything else.
/// Cell (0,0) is the top-left of <see cref="Bounds"/>.
/// </summary>
public sealed class WalkMask
{
    public int Cols { get; }
    public int Rows { get; }
    public RectI Bounds { get; }
    public float CellW => Bounds.Width / (float)Cols;
    public float CellH => Bounds.Height / (float)Rows;

    private readonly ZoneType[] _kinds;

    public WalkMask(int cols, int rows, RectI bounds, bool[] cells)
        : this(cols, rows, bounds, cells.Select(b => b ? ZoneType.Ground : ZoneType.Blocked).ToArray())
    {
    }

    public WalkMask(int cols, int rows, RectI bounds, ZoneType[] kinds)
    {
        if (kinds.Length != cols * rows) throw new ArgumentException("kinds length must be cols*rows");
        Cols = cols;
        Rows = rows;
        Bounds = bounds;
        _kinds = kinds;
    }

    /// <summary>Kinds a pet can stand and walk on.</summary>
    public static bool Walkable(ZoneType k) => k is ZoneType.Ground or ZoneType.Mud or ZoneType.Sand or ZoneType.Sun or ZoneType.Hide or ZoneType.House;

    public ZoneType Kind(int c, int r) =>
        c >= 0 && r >= 0 && c < Cols && r < Rows ? _kinds[r * Cols + c] : ZoneType.Blocked;

    public bool this[int c, int r] => Walkable(Kind(c, r));

    public ZoneType KindAt(Vec2 p)
    {
        var (c, r) = CellOf(p);
        return Kind(c, r);
    }

    /// <summary>Number of walkable cells.</summary>
    public int Count => _kinds.Count(Walkable);

    public int CountOf(ZoneType kind) => _kinds.Count(k => k == kind);

    public (int C, int R) CellOf(Vec2 p) =>
        ((int)MathF.Floor((p.X - Bounds.X) / CellW), (int)MathF.Floor((p.Y - Bounds.Y) / CellH));

    public Vec2 CellCenter(int c, int r) => new(Bounds.X + (c + 0.5f) * CellW, Bounds.Y + (r + 0.5f) * CellH);

    public RectI CellRect(Vec2 p)
    {
        var (c, r) = CellOf(p);
        return new RectI((int)(Bounds.X + c * CellW), (int)(Bounds.Y + r * CellH), (int)MathF.Ceiling(CellW), (int)MathF.Ceiling(CellH));
    }

    public bool IsWalkable(Vec2 p) => Walkable(KindAt(p));

    /// <summary>Bounding box of the walkable cells' centres, in screen pixels (empty if none).</summary>
    public RectI WalkableBounds()
    {
        int minC = Cols, minR = Rows, maxC = -1, maxR = -1;
        for (var r = 0; r < Rows; r++)
        for (var c = 0; c < Cols; c++)
        {
            if (!this[c, r]) continue;
            minC = Math.Min(minC, c); maxC = Math.Max(maxC, c);
            minR = Math.Min(minR, r); maxR = Math.Max(maxR, r);
        }
        if (maxC < 0) return RectI.Empty;
        var a = CellCenter(minC, minR);
        var b = CellCenter(maxC, maxR);
        return RectI.FromLTRB((int)a.X, (int)a.Y, (int)b.X + 1, (int)b.Y + 1);
    }

    /// <summary>Centre of the closest walkable cell (p itself if already walkable).</summary>
    public Vec2 Nearest(Vec2 p) => NearestWhere(p, Walkable);

    public Vec2 NearestWhere(Vec2 p, Func<ZoneType, bool> pred)
    {
        var (pc, pr) = CellOf(p);
        pc = Math.Clamp(pc, 0, Cols - 1);
        pr = Math.Clamp(pr, 0, Rows - 1);
        if (pred(Kind(pc, pr))) return p;

        var maxRing = Math.Max(Cols, Rows);
        for (var ring = 1; ring <= maxRing; ring++)
        {
            var best = (-1, -1);
            var bestD = float.MaxValue;
            for (var dr = -ring; dr <= ring; dr++)
            for (var dc = -ring; dc <= ring; dc++)
            {
                if (Math.Abs(dr) != ring && Math.Abs(dc) != ring) continue;
                var c = pc + dc;
                var r = pr + dr;
                if (c < 0 || r < 0 || c >= Cols || r >= Rows || !pred(Kind(c, r))) continue;
                var d = Vec2.Distance(CellCenter(c, r), p);
                if (d < bestD) { bestD = d; best = (c, r); }
            }
            if (best.Item1 >= 0) return CellCenter(best.Item1, best.Item2);
        }
        return p;
    }

    public Vec2 RandomWalkable(Random rng) => RandomWhere(rng, Walkable);

    public Vec2 RandomWhere(Random rng, Func<ZoneType, bool> pred)
    {
        var total = _kinds.Count(pred);
        if (total == 0) return Bounds.Center;
        var pick = rng.Next(total);
        for (var i = 0; i < _kinds.Length; i++)
        {
            if (!pred(_kinds[i])) continue;
            if (pick-- == 0)
            {
                var center = CellCenter(i % Cols, i / Cols);
                var jx = ((float)rng.NextDouble() - 0.5f) * CellW * 0.8f;
                var jy = ((float)rng.NextDouble() - 0.5f) * CellH * 0.8f;
                return new Vec2(center.X + jx, center.Y + jy);
            }
        }
        return Bounds.Center;
    }

    /// <summary>True when every sample along the segment is walkable.</summary>
    public bool LineWalkable(Vec2 a, Vec2 b) => LineWhere(a, b, Walkable);

    public bool LineWhere(Vec2 a, Vec2 b, Func<ZoneType, bool> pred)
    {
        var step = Math.Max(2f, Math.Min(CellW, CellH) * 0.5f);
        var n = Math.Max(1, (int)(Vec2.Distance(a, b) / step));
        for (var i = 0; i <= n; i++)
            if (!pred(KindAt(Vec2.Lerp(a, b, i / (float)n)))) return false;
        return true;
    }
}
