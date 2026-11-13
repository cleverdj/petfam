using PetFam.Core.Geometry;

namespace PetFam.Core.World;

/// <summary>
/// One cell = (virtual desktop, monitor). Plan §6.4.1. There is one cell per virtual desktop, all on the
/// primary monitor (multi-monitor comes later).
/// </summary>
public sealed class Cell
{
    public required string Id { get; init; }
    public required string DesktopId { get; init; }
    public required string MonitorId { get; init; }

    /// <summary>Monitor bounds in screen pixels.</summary>
    public required RectI Bounds { get; init; }

    /// <summary>Monitor working area (excludes taskbar/dock) in screen pixels.</summary>
    public required RectI WorkingArea { get; init; }

    public required HabitatMap Map { get; set; }

    /// <summary>Food bowls placed by the user in this cell (plan §7.1 feeding).</summary>
    public List<FoodBowl> Bowls { get; } = new();

    /// <summary>The ball during a game of fetch, if any.</summary>
    public Ball? Ball { get; set; }

    /// <summary>Pet houses built and decorated by the user in this cell.</summary>
    public List<PetHouse> Houses { get; } = new();

    public const int MaxHouses = 3;

    public FoodBowl? NearestBowl(Vec2 from)
    {
        FoodBowl? best = null;
        var bestDist = float.MaxValue;
        foreach (var b in Bowls)
        {
            if (b.Amount <= 0f) continue;
            var d = Vec2.Distance(from, b.Position);
            if (d < bestDist)
            {
                bestDist = d;
                best = b;
            }
        }
        return best;
    }

    public static string MakeId(string desktopId, string monitorId) => $"{desktopId}:{monitorId}";
}

public sealed class Ball
{
    /// <summary>Ground point under the ball, in screen pixels.</summary>
    public Vec2 Position { get; set; }

    /// <summary>Height above the ground in pixels (depth 1), for the throw arc.</summary>
    public float Height { get; set; }

    public Guid? CarriedBy { get; set; }

    /// <summary>Ground velocity in screen pixels per second while flying or rolling.</summary>
    public Vec2 Velocity { get; set; }

    /// <summary>Upward speed in pixels per second.</summary>
    public float VerticalSpeed { get; set; }

    /// <summary>The person is holding the ball with the mouse.</summary>
    public bool HeldByUser { get; set; }

    /// <summary>The ball is drawn in its own topmost window that takes mouse input.</summary>
    public bool InFront { get; set; }

    /// <summary>Counts throws by the person, so a game can notice a new one.</summary>
    public int Throws { get; private set; }

    /// <summary>Where the person let go of the ball (ground point), for the dog to bring it back to.</summary>
    public Vec2 ThrownFrom { get; private set; }

    public const float Gravity = 1100f;
    public const float RollFriction = 340f;
    public const float MaxThrowSpeed = 1600f;

    public bool IsMoving => Height > 0.5f || VerticalSpeed > 0f || Velocity.Length > 6f;

    /// <summary>Throw from the current position with a mouse flick (screen pixels per second).</summary>
    public void Throw(Vec2 flick)
    {
        var speed = flick.Length;
        if (speed > MaxThrowSpeed) flick = flick * (MaxThrowSpeed / speed);
        Velocity = flick;
        VerticalSpeed = System.Math.Min(480f, 140f + flick.Length * 0.22f);
        HeldByUser = false;
        CarriedBy = null;
        ThrownFrom = Position;
        Throws++;
    }

    /// <summary>Flight with gravity, bounces losing energy, then rolling to a stop inside the roam area.</summary>
    public void Step(float dt, HabitatMap map)
    {
        if (HeldByUser || CarriedBy is not null || dt <= 0f) return;

        if (Height > 0f || VerticalSpeed > 0f)
        {
            VerticalSpeed -= Gravity * dt;
            Height += VerticalSpeed * dt;
            if (Height <= 0f)
            {
                Height = 0f;
                if (VerticalSpeed < -120f)
                {
                    VerticalSpeed = -VerticalSpeed * 0.45f;
                    Velocity *= 0.78f;
                }
                else
                {
                    VerticalSpeed = 0f;
                }
            }
        }
        else
        {
            var speed = Velocity.Length;
            var slower = System.Math.Max(0f, speed - RollFriction * dt);
            Velocity = speed < 1e-3f ? Vec2.Zero : Velocity * (slower / speed);
        }

        var next = Position + Velocity * dt;
        var clamped = map.ClampToRoam(next);
        if (clamped != next)
        {
            Velocity = new Vec2(
                System.Math.Abs(clamped.X - next.X) > 0.01f ? -Velocity.X * 0.5f : Velocity.X,
                System.Math.Abs(clamped.Y - next.Y) > 0.01f ? -Velocity.Y * 0.5f : Velocity.Y);
        }
        Position = clamped;
    }
}

public enum HouseShape
{
    Gable,
    Round,
    Tent,
    Cabin,
}

/// <summary>
/// A pet house the user built: a ground position (the middle of its base, where the door is), a name on the sign,
/// a shape, colours from the app palettes and a set of decorations. Sleepy pets go inside to nap.
/// </summary>
public sealed class PetHouse
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Middle of the base (the doorstep) in screen pixels.</summary>
    public Vec2 Position { get; set; }

    public string Name { get; set; } = L.S("Our house", "우리 집");
    public HouseShape Shape { get; set; } = HouseShape.Gable;

    /// <summary>Palette indices (the app owns the colours).</summary>
    public int RoofColor { get; set; }
    public int WallColor { get; set; }

    /// <summary>Decoration ids: "flowers", "flags", "heart", "chimney", "lamp", "fence".</summary>
    public HashSet<string> Decorations { get; set; } = new() { "flowers" };

    public static readonly string[] AllDecorations = { "flowers", "flags", "heart", "chimney", "lamp", "fence" };

    /// <summary>House width at depth scale 1, in pixels.</summary>
    public const float BaseWidth = 120f;

    /// <summary>Where a pet stands to go in: just in front of the door.</summary>
    public Vec2 Doorstep(float depth) => new(Position.X, Position.Y + 4f * depth);

    /// <summary>Whose house it is. Null: everybody's (any pet may go in).</summary>
    public Guid? OwnerId { get; set; }

    /// <summary>May this pet go in? Its own house, or one that belongs to everybody.</summary>
    public bool Welcomes(Pets.Pet pet) => OwnerId is null || OwnerId == pet.Id;

    public PetHouse Clone() => new()
    {
        Id = Id, Position = Position, Name = Name, Shape = Shape, RoofColor = RoofColor, WallColor = WallColor,
        Decorations = new HashSet<string>(Decorations), OwnerId = OwnerId,
    };
}

public sealed class FoodBowl
{
    public Guid Id { get; } = Guid.NewGuid();
    public Vec2 Position { get; set; }

    /// <summary>1 = full, 0 = empty.</summary>
    public float Amount { get; set; } = 1f;
}

public sealed class World
{
    private readonly Dictionary<string, Cell> _cells = new();

    public IReadOnlyCollection<Cell> Cells => _cells.Values;

    public Cell? Find(string cellId) => _cells.GetValueOrDefault(cellId);

    public Cell Upsert(Cell cell)
    {
        _cells[cell.Id] = cell;
        return cell;
    }

    public bool Remove(string cellId) => _cells.Remove(cellId);

    public Cell? AnyCell() => _cells.Values.FirstOrDefault();

    // ---------------------------------------------------------------- virtual desktops (plan §6.4)

    /// <summary>Virtual desktop ids in the order the OS shows them (Ctrl+Win+←/→).</summary>
    public List<string> DesktopOrder { get; } = new();

    /// <summary>Names the user gave desktops (missing: unnamed).</summary>
    public Dictionary<string, string> DesktopNames { get; } = new();

    /// <summary>0-based position of the cell's desktop, or -1.</summary>
    public int DesktopIndex(Cell cell) => DesktopOrder.IndexOf(cell.DesktopId);

    /// <summary>The desktop's cell on the main monitor (the first one listed in <see cref="MonitorOrder"/>), or any of its cells.</summary>
    public Cell? CellForDesktop(string desktopId) =>
        (MonitorOrder.Count > 0 ? CellFor(desktopId, MonitorOrder[0]) : null) ?? _cells.Values.FirstOrDefault(c => c.DesktopId == desktopId);

    /// <summary>The cell of that desktop on that monitor.</summary>
    public Cell? CellFor(string desktopId, string monitorId) =>
        _cells.GetValueOrDefault(Cell.MakeId(desktopId, monitorId)) ??
        _cells.Values.FirstOrDefault(c => c.DesktopId == desktopId && c.MonitorId == monitorId);

    /// <summary>All cells of one desktop (one per monitor), main monitor first.</summary>
    public IEnumerable<Cell> CellsOfDesktop(string desktopId) =>
        _cells.Values.Where(c => c.DesktopId == desktopId).OrderBy(c => MonitorIndex(c) is var i && i < 0 ? int.MaxValue : i);

    /// <summary>The cell on the next (+1) or previous (-1) desktop, on the same monitor: where a pet walking off that edge comes in.</summary>
    public Cell? Neighbor(Cell cell, int direction)
    {
        var i = DesktopIndex(cell);
        if (i < 0) return null;
        var j = i + System.Math.Sign(direction);
        if (j < 0 || j >= DesktopOrder.Count) return null;
        return CellFor(DesktopOrder[j], cell.MonitorId) ?? CellForDesktop(DesktopOrder[j]);
    }

    // ---------------------------------------------------------------- monitors

    /// <summary>Monitor ids, main monitor first, then left to right.</summary>
    public List<string> MonitorOrder { get; } = new();

    public int MonitorIndex(Cell cell) => MonitorOrder.IndexOf(cell.MonitorId);

    /// <summary>
    /// The monitor next to this one on the same desktop, to the right (+1) or left (-1): the one whose screen sits
    /// beside this one and overlaps it vertically, nearest first. Null when there is none on that side.
    /// </summary>
    public Cell? MonitorNeighbor(Cell cell, int direction)
    {
        var b = cell.Bounds;
        var cx = b.X + b.Width / 2f;
        return _cells.Values
            .Where(c => c.DesktopId == cell.DesktopId && c.MonitorId != cell.MonitorId)
            .Where(c => System.Math.Min(c.Bounds.Bottom, b.Bottom) - System.Math.Max(c.Bounds.Top, b.Top) > 0)
            .Where(c => direction > 0 ? c.Bounds.X + c.Bounds.Width / 2f > cx : c.Bounds.X + c.Bounds.Width / 2f < cx)
            .OrderBy(c => System.Math.Abs(c.Bounds.X + c.Bounds.Width / 2f - cx))
            .FirstOrDefault();
    }

    /// <summary>"Monitor 2" (or "" with one monitor).</summary>
    public string DescribeMonitor(Cell cell)
    {
        if (MonitorOrder.Count < 2) return "";
        var i = MonitorIndex(cell);
        return i == 0 ? L.S("main monitor", "주 모니터") : L.S($"monitor {i + 1}", $"모니터 {i + 1}");
    }

    /// <summary>"Desktop 2", "monitor 2", or "Desktop 2 · monitor 2", depending on how many there are.</summary>
    public string DescribeCell(Cell cell)
    {
        var desk = DesktopOrder.Count > 1 ? DescribeDesktop(cell) : "";
        var mon = DescribeMonitor(cell);
        return desk.Length == 0 ? mon : mon.Length == 0 ? desk : $"{desk} · {mon}";
    }

    /// <summary>"Desktop 2" or "Desktop 2 (Beach)" for the tray.</summary>
    public string DescribeDesktop(Cell cell)
    {
        var i = DesktopIndex(cell);
        var label = i >= 0 ? L.S($"Desktop {i + 1}", $"데스크톱 {i + 1}") : L.S("Desktop", "데스크톱");
        return DesktopNames.TryGetValue(cell.DesktopId, out var name) && name.Length > 0 ? $"{label} ({name})" : label;
    }
}
