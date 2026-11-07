using ZooZoo.Core.Geometry;

namespace ZooZoo.Core.World;

/// <summary>
/// One cell = (virtual desktop, monitor). Plan §6.4.1. The spike has a single cell: the primary monitor
/// on the current desktop. Multi-cell comes with the virtual desktop work.
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

    /// <summary>Food bowls placed by the user in this cell (plan §7.1 먹이주기).</summary>
    public List<FoodBowl> Bowls { get; } = new();

    /// <summary>The ball during a game of fetch, if any.</summary>
    public Ball? Ball { get; set; }

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
    public Vec2 Position { get; set; }

    /// <summary>Height above the ground in pixels (depth 1), for the throw arc.</summary>
    public float Height { get; set; }

    public Guid? CarriedBy { get; set; }
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
}
