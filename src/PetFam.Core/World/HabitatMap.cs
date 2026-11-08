using PetFam.Core.Geometry;

namespace PetFam.Core.World;

/// <summary>Zone types from plan §6.1.</summary>
public enum ZoneType
{
    Ground,
    Water,
    House,
    Play,
    Perch,
    Hide,
    Sky,
    Blocked,
    Mud,
    Sand,
    Sun,
    Snow,
}

public enum SurfaceKind
{
    Dirt,
    Grass,
    Floor,
    Stone,
}

/// <summary>A zone. Spike uses rectangles; polygons come with the zone editor.</summary>
public sealed class Zone
{
    public ZoneType Type { get; init; }
    public RectI Bounds { get; init; }
    public bool Walkable { get; init; } = true;
}

/// <summary>
/// What a wallpaper means to a pet (plan §6.1). Bounds are in screen pixels of the cell it belongs to.
/// </summary>
public sealed class HabitatMap
{
    public string WallpaperHash { get; init; } = "";
    public RectI Bounds { get; init; }

    /// <summary>
    /// Area pets may roam freely (feet positions), in screen pixels. The wallpaper is treated as a floor seen
    /// at a slight angle: higher on screen = further away. Zones (water, house, ...) refine this later.
    /// </summary>
    public RectI RoamArea { get; init; }

    /// <summary>Y of the nearest walking line (bottom of the roam area).</summary>
    public int GravityLine => RoamArea.Bottom;

    /// <summary>Sprite scale at the far edge (top of the roam area), relative to the near edge.</summary>
    public float FarScale { get; init; } = 0.55f;

    /// <summary>Overall pet size multiplier. Everything that uses DepthScale (sprites, hit boxes, speeds, reach) follows it.</summary>
    public float PetScale { get; init; } = 1.6f;

    public SurfaceKind Surface { get; init; } = SurfaceKind.Floor;
    public List<Zone> Zones { get; init; } = new();

    /// <summary>Recognised floor of the wallpaper, if any. Without it the whole roam rectangle is floor.</summary>
    public WalkMask? Mask { get; init; }

    public IEnumerable<Zone> ZonesOf(ZoneType type) => Zones.Where(z => z.Type == type);

    public Zone? ZoneAt(Vec2 p, ZoneType type)
    {
        // Recognised places live in the mask; hand-made zones (official wallpapers, editor) in the list.
        if (Mask is not null && type is ZoneType.Water or ZoneType.Mud or ZoneType.Sand or ZoneType.Sun or ZoneType.Hide or ZoneType.House)
        {
            if (Mask.KindAt(p) == type) return new Zone { Type = type, Bounds = Mask.CellRect(p) };
        }
        return Zones.FirstOrDefault(z => z.Type == type && z.Bounds.Contains(p));
    }

    /// <summary>Does this wallpaper have at least one place of this kind?</summary>
    public bool Has(ZoneType type) => (Mask?.CountOf(type) ?? 0) > 0 || Zones.Any(z => z.Type == type);

    /// <summary>Perspective scale for a feet position: PetScale × (FarScale at the top of the roam area … 1 at the bottom).</summary>
    public float DepthScale(float y)
    {
        if (RoamArea.Height <= 0) return PetScale;
        var t = System.Math.Clamp((y - RoamArea.Top) / (float)RoamArea.Height, 0f, 1f);
        return PetScale * (FarScale + (1f - FarScale) * t);
    }

    /// <summary>Clamp a feet position into the roam area, then onto the recognised floor (nearest floor cell).</summary>
    public Vec2 ClampToRoam(Vec2 p)
    {
        var q = new Vec2(
            System.Math.Clamp(p.X, RoamArea.Left, System.Math.Max(RoamArea.Left, RoamArea.Right - 1)),
            System.Math.Clamp(p.Y, RoamArea.Top, System.Math.Max(RoamArea.Top, RoamArea.Bottom - 1)));
        if (Mask is null || Mask.IsWalkable(q)) return q;
        var n = Mask.Nearest(q);
        return new Vec2(
            System.Math.Clamp(n.X, RoamArea.Left, System.Math.Max(RoamArea.Left, RoamArea.Right - 1)),
            System.Math.Clamp(n.Y, RoamArea.Top, System.Math.Max(RoamArea.Top, RoamArea.Bottom - 1)));
    }

    public bool CanRoam(Vec2 p) =>
        RoamArea.Contains(p) && (Mask is null || Mask.IsWalkable(p)) && ZoneAt(p, ZoneType.Blocked) is null;

    /// <summary>A random standing spot: on the recognised floor if there is one.</summary>
    public Vec2 RandomPoint(Random rng)
    {
        if (Mask is not null)
        {
            for (var i = 0; i < 8; i++)
            {
                var p = Mask.RandomWalkable(rng);
                if (RoamArea.Contains(p)) return p;
            }
        }
        return new Vec2(
            RoamArea.Left + (float)rng.NextDouble() * RoamArea.Width,
            RoamArea.Top + (float)rng.NextDouble() * RoamArea.Height);
    }

    /// <summary>Can a pet walk straight from a to b without leaving the floor?</summary>
    public bool LineWalkable(Vec2 a, Vec2 b) => Mask is null || Mask.LineWalkable(a, b);

    /// <summary>
    /// Map from a recognised floor: the roam area is the floor's extent (kept inside the working-area margins),
    /// so the top of the floor is the horizon used for perspective.
    /// </summary>
    public static HabitatMap FromMask(RectI bounds, RectI workingArea, WalkMask mask, string wallpaperHash = "")
    {
        var flat = Flat(bounds, workingArea, wallpaperHash);
        var floor = mask.WalkableBounds();
        var roam = RectI.FromLTRB(
            System.Math.Max(flat.RoamArea.Left, floor.Left),
            System.Math.Max(workingArea.Top + 60, floor.Top),
            System.Math.Min(flat.RoamArea.Right, floor.Right),
            flat.RoamArea.Bottom);
        if (roam.Width < 100 || roam.Height < 40) return flat;
        return new HabitatMap
        {
            WallpaperHash = wallpaperHash,
            Bounds = bounds,
            RoamArea = roam,
            Surface = SurfaceKind.Floor,
            Mask = mask,
            Zones = { new Zone { Type = ZoneType.Ground, Bounds = roam } },
        };
    }

    /// <summary>
    /// Fallback map: the whole working area is walkable floor, nothing else. Used when a wallpaper is unknown.
    /// Margins keep sprites fully on screen; the top margin leaves room for the body above the feet.
    /// </summary>
    public static HabitatMap Flat(RectI bounds, RectI workingArea, string wallpaperHash = "")
    {
        var roam = RectI.FromLTRB(workingArea.Left + 60, workingArea.Top + 200, workingArea.Right - 60, workingArea.Bottom - 6);
        return new HabitatMap
        {
            WallpaperHash = wallpaperHash,
            Bounds = bounds,
            RoamArea = roam,
            Surface = SurfaceKind.Floor,
            Zones = { new Zone { Type = ZoneType.Ground, Bounds = roam } },
        };
    }
}
