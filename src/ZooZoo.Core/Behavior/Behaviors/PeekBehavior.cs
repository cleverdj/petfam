using ZooZoo.Core.Geometry;
using ZooZoo.Core.Pets;
using ZooZoo.Core.Platform;

namespace ZooZoo.Core.Behavior.Behaviors;

/// <summary>
/// 창가 빼꼼: the pet slips behind the foreground window and pokes its head out from one of its edges
/// for a few seconds, eyes following the cursor (plan §5.2 관찰). Aborts if the window moves.
/// </summary>
public sealed class PeekBehavior : Behavior
{
    public const float MinRoom = 76f;          // pixels of free space needed outside the edge
    public const float EmergeSeconds = 0.5f;
    public const float RetreatSeconds = 0.35f;

    private enum Phase { Emerge, Hold, Retreat }

    private Phase _phase;
    private float _hold;
    private float _cooldown;
    private PeekState? _state;

    public override string Name => "peek";

    public override bool Interruptible => false;

    public override float Score(BehaviorContext ctx)
    {
        if (_cooldown > 0f) return 0f;
        var fg = ctx.User.Foreground;
        if (fg is null || ctx.User.DoNotDisturb) return 0f;
        if (!ctx.Debug.AllowPeekAlways && ctx.Pet.Bond.Stage < BondStage.Curious) return 0f;
        if (!HasRoom(fg.Bounds, ctx.Cell.Bounds)) return 0f;

        var p = ctx.Pet;
        var dogBonus = p.Species == PetSpecies.Dog ? 0.1f : 0f;
        return 0.15f + p.Needs.Curiosity / 100f * 0.6f + dogBonus - p.Personality.Shyness * 0.2f;
    }

    public override void Start(BehaviorContext ctx)
    {
        var fg = ctx.User.Foreground!;
        var edges = CandidateEdges(fg.Bounds, ctx.Cell.Bounds);
        var edge = edges[ctx.Rng.Next(edges.Count)];
        var anchor = AnchorOn(edge, fg.Bounds, ctx);

        _state = new PeekState
        {
            Edge = edge,
            Anchor = anchor,
            WindowRect = fg.Bounds,
            WindowHandle = fg.Handle,
            Progress = 0f,
        };
        _phase = Phase.Emerge;
        _hold = ctx.RandomRange(2f, 6f);

        var pet = ctx.Pet;
        pet.Peek = _state;
        pet.Layer = PetLayer.WindowEdge;
        pet.SetAnimation("peek");
    }

    public override bool Update(BehaviorContext ctx, float dt)
    {
        var s = _state!;
        var fg = ctx.User.Foreground;

        // Window changed or moved: retreat now.
        if (_phase != Phase.Retreat && (fg is null || fg.Handle != s.WindowHandle || Moved(fg.Bounds, s.WindowRect)))
            _phase = Phase.Retreat;

        switch (_phase)
        {
            case Phase.Emerge:
                s.Progress = System.Math.Min(1f, s.Progress + dt / EmergeSeconds);
                if (s.Progress >= 1f) _phase = Phase.Hold;
                return false;

            case Phase.Hold:
                _hold -= dt;
                if (_hold <= 0f) _phase = Phase.Retreat;
                return false;

            default:
                s.Progress = System.Math.Max(0f, s.Progress - dt / RetreatSeconds);
                return s.Progress <= 0f;
        }
    }

    public override void Stop(BehaviorContext ctx, bool interrupted)
    {
        var pet = ctx.Pet;
        pet.Peek = null;
        pet.Layer = PetLayer.Backyard;

        // Reappear on the floor just outside the edge it was peeking from.
        if (_state is not null)
        {
            const float off = 70f;
            var a = _state.Anchor;
            var spot = _state.Edge switch
            {
                PeekEdge.Top => new Vec2(a.X, a.Y - off),
                PeekEdge.Bottom => new Vec2(a.X, a.Y + off),
                PeekEdge.Left => new Vec2(a.X - off, a.Y),
                _ => new Vec2(a.X + off, a.Y),
            };
            pet.Position = ctx.Cell.Map.ClampToRoam(spot);
        }
        pet.SetAnimation("idle");

        pet.Needs.SpendCuriosity(40f);
        _cooldown = ctx.RandomRange(20f, 60f);
        _state = null;
    }

    /// <summary>Cooldown ticks even while other behaviors run. Called by the simulation each frame.</summary>
    public void TickCooldown(float dt) => _cooldown = System.Math.Max(0f, _cooldown - dt);

    private static bool Moved(RectI a, RectI b) =>
        System.Math.Abs(a.X - b.X) > 4 || System.Math.Abs(a.Y - b.Y) > 4 ||
        System.Math.Abs(a.Width - b.Width) > 4 || System.Math.Abs(a.Height - b.Height) > 4;

    private static bool HasRoom(RectI win, RectI cell) => CandidateEdges(win, cell).Count > 0;

    private static List<PeekEdge> CandidateEdges(RectI win, RectI cell)
    {
        var list = new List<PeekEdge>(4);
        if (win.Top - cell.Top >= MinRoom) list.Add(PeekEdge.Top);
        if (cell.Bottom - win.Bottom >= MinRoom) list.Add(PeekEdge.Bottom);
        if (win.Left - cell.Left >= MinRoom) list.Add(PeekEdge.Left);
        if (cell.Right - win.Right >= MinRoom) list.Add(PeekEdge.Right);
        return list;
    }

    private static Vec2 AnchorOn(PeekEdge edge, RectI win, BehaviorContext ctx)
    {
        var t = ctx.RandomRange(0.15f, 0.85f);
        return edge switch
        {
            PeekEdge.Top => new Vec2(win.Left + win.Width * t, win.Top),
            PeekEdge.Bottom => new Vec2(win.Left + win.Width * t, win.Bottom),
            PeekEdge.Left => new Vec2(win.Left, win.Top + win.Height * t),
            _ => new Vec2(win.Right, win.Top + win.Height * t),
        };
    }
}
