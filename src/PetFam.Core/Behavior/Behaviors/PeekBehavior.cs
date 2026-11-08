using PetFam.Core.Geometry;
using PetFam.Core.Pets;
using PetFam.Core.Platform;

namespace PetFam.Core.Behavior.Behaviors;

/// <summary>
/// Window peek: the pet walks behind the foreground window (the window hides it), pokes its head out from one of
/// its edges for a few seconds looking at you (plan §5.2 watching), then walks back out from behind the window.
/// Retreats if the window moves.
/// </summary>
public sealed class PeekBehavior : Behavior
{
    public const float MinRoom = 76f;          // pixels of free space needed outside the edge
    public const float EmergeSeconds = 0.5f;
    public const float RetreatSeconds = 0.35f;

    private enum Phase { Sneak, Emerge, Hold, Retreat, Return }

    /// <summary>How much of the window must cover the pet's body for it to count as hidden (pixels at depth 1).</summary>
    private const float SideCover = 70f, TopCover = 110f, BottomCover = 12f;

    private Phase _phase;
    private float _hold;
    private float _cooldown;
    private PeekState? _state;
    private RectI _window;
    private IntPtr _handle;
    private Vec2 _hide;
    private Vec2? _out;

    public override string Name => _phase switch { Phase.Sneak => "peek:sneak", Phase.Return => "peek:return", _ => "peek" };

    public override bool Interruptible => false;

    public override float Score(BehaviorContext ctx)
    {
        if (_cooldown > 0f || !ctx.Visible) return 0f;
        var fg = ctx.User.Foreground;
        if (fg is null || ctx.User.DoNotDisturb) return 0f;
        if (!ctx.Debug.AllowPeekAlways && ctx.Pet.Bond.Stage < BondStage.Curious) return 0f;
        if (!HasRoom(fg.Bounds, ctx.Cell.Bounds)) return 0f;
        // It has to be able to slip behind the window first (walking on the floor), or it would vanish on the spot.
        if (HideSpot(ctx, fg.Bounds) is null) return 0f;

        var p = ctx.Pet;
        var dogBonus = p.Species == PetSpecies.Dog ? 0.1f : 0f;
        return 0.15f + p.Needs.Curiosity / 100f * 0.6f + dogBonus - p.Personality.Shyness * 0.2f;
    }

    public override void Start(BehaviorContext ctx)
    {
        var fg = ctx.User.Foreground!;
        _window = fg.Bounds;
        _handle = fg.Handle;
        _state = null;
        _out = null;
        _hide = HideSpot(ctx, _window) ?? ctx.Pet.Position;
        _phase = Phase.Sneak;
        ctx.Pet.SetAnimation("walk");
    }

    public override bool Update(BehaviorContext ctx, float dt)
    {
        var pet = ctx.Pet;
        var fg = ctx.User.Foreground;
        var windowGone = fg is null || fg.Handle != _handle || Moved(fg.Bounds, _window);

        switch (_phase)
        {
            case Phase.Sneak:
                // Walk behind the window on the floor: the window hides the pet as it goes.
                if (windowGone) return true;
                if (!PlaceSeeker.WalkTo(ctx, _hide, dt, speedMul: 1.3f)) return false;
                BeginPeek(ctx);
                return false;

            case Phase.Emerge:
            case Phase.Hold:
                if (windowGone) { _phase = Phase.Retreat; return false; }
                if (_phase == Phase.Emerge)
                {
                    _state!.Progress = System.Math.Min(1f, _state.Progress + dt / EmergeSeconds);
                    if (_state.Progress >= 1f) _phase = Phase.Hold;
                }
                else
                {
                    _hold -= dt;
                    if (_hold <= 0f) _phase = Phase.Retreat;
                }
                return false;

            case Phase.Retreat:
                _state!.Progress = System.Math.Max(0f, _state.Progress - dt / RetreatSeconds);
                if (_state.Progress > 0f) return false;
                // Back behind the window, on the floor, and out from behind it.
                EndPeek(ctx);
                _out = WayOut(ctx, _window);
                _phase = Phase.Return;
                pet.SetAnimation("walk");
                return _out is null;

            default:
                return PlaceSeeker.WalkTo(ctx, _out!.Value, dt);
        }
    }

    private void BeginPeek(BehaviorContext ctx)
    {
        var edges = CandidateEdges(_window, ctx.Cell.Bounds);
        var edge = edges.Count > 0 ? edges[ctx.Rng.Next(edges.Count)] : PeekEdge.Top;
        _state = new PeekState
        {
            Edge = edge,
            Anchor = AnchorOn(edge, _window, ctx),
            WindowRect = _window,
            WindowHandle = _handle,
            Progress = 0f,
        };
        _phase = Phase.Emerge;
        _hold = ctx.RandomRange(2f, 6f);

        var pet = ctx.Pet;
        pet.Peek = _state;
        pet.Layer = PetLayer.WindowEdge;
        pet.SetAnimation("peek");
        // Face the user: it peeks out to look at you.
        pet.Heading = pet.TargetHeading = 90f;
    }

    private void EndPeek(BehaviorContext ctx)
    {
        var pet = ctx.Pet;
        if (pet.Peek is null) return;
        pet.Peek = null;
        pet.Layer = PetLayer.Backyard;
        pet.Position = _hide;
        pet.Needs.SpendCuriosity(40f);
    }

    public override void Stop(BehaviorContext ctx, bool interrupted)
    {
        EndPeek(ctx);
        ctx.Pet.SetAnimation("idle");
        _cooldown = ctx.RandomRange(20f, 60f);
        _state = null;
    }

    /// <summary>Is a pet standing here (feet) covered by the window?</summary>
    private static bool Covered(RectI win, Vec2 feet, float depth) =>
        feet.X >= win.Left + SideCover * depth && feet.X <= win.Right - SideCover * depth &&
        feet.Y >= win.Top + TopCover * depth && feet.Y <= win.Bottom - BottomCover * depth;

    /// <summary>The nearest floor spot behind the window where the pet is out of sight. Null if there is none.</summary>
    public static Vec2? HideSpot(BehaviorContext ctx, RectI win)
    {
        var map = ctx.Cell.Map;
        var from = ctx.Pet.Position;
        Vec2? best = null;
        var bestD = float.MaxValue;
        const int N = 12;
        for (var i = 0; i <= N; i++)
        for (var j = 0; j <= N; j++)
        {
            var p = new Vec2(win.Left + win.Width * i / (float)N, win.Top + win.Height * j / (float)N);
            if (!map.CanRoam(p) || !Covered(win, p, map.DepthScale(p.Y))) continue;
            var d = Vec2.Distance(p, from);
            if (d < bestD) { bestD = d; best = p; }
        }
        return best;
    }

    /// <summary>The nearest floor spot where the pet is fully out from behind the window again. Null if the window covers the whole floor.</summary>
    public static Vec2? WayOut(BehaviorContext ctx, RectI win)
    {
        var map = ctx.Cell.Map;
        var r = map.RoamArea;
        var from = ctx.Pet.Position;
        Vec2? best = null;
        var bestD = float.MaxValue;
        const int N = 24;
        for (var i = 0; i <= N; i++)
        for (var j = 0; j <= N; j++)
        {
            var p = new Vec2(r.Left + r.Width * i / (float)N, r.Top + r.Height * j / (float)N);
            if (!map.CanRoam(p)) continue;
            var depth = map.DepthScale(p.Y);
            var clear = p.X < win.Left - SideCover * depth || p.X > win.Right + SideCover * depth ||
                        p.Y < win.Top - BottomCover * depth || p.Y > win.Bottom + BottomCover * depth + 10f;
            if (!clear) continue;
            var d = Vec2.Distance(p, from);
            if (d < bestD) { bestD = d; best = p; }
        }
        return best;
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
