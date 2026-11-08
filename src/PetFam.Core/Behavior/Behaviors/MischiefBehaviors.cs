using PetFam.Core.Geometry;
using PetFam.Core.Pets;
using PetFam.Core.Platform;
using PetFam.Core.World;

namespace PetFam.Core.Behavior.Behaviors;

/// <summary>
/// Knocking the bowl (cats): walks up beside the food bowl, stares right at you for a moment… and swats it away.
/// </summary>
public sealed class KnockBowlBehavior : CooldownBehavior
{
    private enum Phase { Approach, Stare, Swat }

    private Phase _phase;
    private FoodBowl? _bowl;
    private Vec2 _spot, _from, _to;
    private float _t;
    private int _side;

    public KnockBowlBehavior() => Cooldown = 120f;

    public override string Name => "knock-bowl";

    public override bool Interruptible => false;

    public override float Score(BehaviorContext ctx)
    {
        var p = ctx.Pet;
        if (p.Species != PetSpecies.Cat || Cooldown > 0f || p.Hidden || p.Needs.Hunger < 60f) return 0f;
        if (!ctx.Cell.Bowls.Any(b => b.Amount > 0f)) return 0f;
        return 0.2f + p.Personality.Playfulness * 0.35f;
    }

    public override void Start(BehaviorContext ctx)
    {
        var pet = ctx.Pet;
        _bowl = ctx.Cell.Bowls.Where(b => b.Amount > 0f).OrderBy(b => Vec2.Distance(b.Position, pet.Position)).First();
        _side = pet.Position.X < _bowl.Position.X ? -1 : 1;
        var d = ctx.Cell.Map.DepthScale(_bowl.Position.Y);
        _spot = ctx.Cell.Map.ClampToRoam(_bowl.Position + new Vec2(_side * 42f * d, 4f * d));
        _phase = Phase.Approach;
    }

    public override bool Update(BehaviorContext ctx, float dt)
    {
        var pet = ctx.Pet;
        if (_bowl is null || !ctx.Cell.Bowls.Contains(_bowl)) return true;
        switch (_phase)
        {
            case Phase.Approach:
                if (!PlaceSeeker.WalkTo(ctx, _spot, dt)) return false;
                // Turn to you and hold your gaze. You know what's coming.
                pet.Heading = pet.TargetHeading = 90f;
                pet.SetAnimation("sit");
                _phase = Phase.Stare;
                _t = Rand(ctx, 1.2f, 2f);
                return false;
            case Phase.Stare:
                _t -= dt;
                if (_t > 0f) return false;
                pet.FaceToward(_bowl.Position - pet.Position);
                pet.SetAnimation("bat");
                var d = ctx.Cell.Map.DepthScale(_bowl.Position.Y);
                _from = _bowl.Position;
                _to = ctx.Cell.Map.ClampToRoam(_bowl.Position + new Vec2(-_side * Rand(ctx, 60f, 95f) * d, Rand(ctx, -8f, 12f) * d));
                _phase = Phase.Swat;
                _t = 0f;
                return false;
            default:
                _t += dt;
                var k = System.Math.Clamp((_t - 0.15f) / 0.4f, 0f, 1f);
                var ease = 1f - (1f - k) * (1f - k);
                _bowl.Position = _from + (_to - _from) * ease;
                if (k >= 1f && _t > 0.9f)
                {
                    if (ctx.Visible && ctx.Rng.NextDouble() < 0.7) pet.Say(L.S("Oops~", "앗, 실수~"), 1.6f);
                    return true;
                }
                return false;
        }
    }

    public override void Stop(BehaviorContext ctx, bool interrupted)
    {
        ctx.Pet.SetAnimation("idle");
        Cooldown = Rand(ctx, 240f, 480f);
    }
}

/// <summary>
/// Sitting on top of your window (cats): slips behind the window you are using, hops up onto its top edge and sits
/// there with its tail hanging down, then hops back down behind it and walks out. Drawn on the window-edge layer.
/// </summary>
public sealed class WindowPerchBehavior : CooldownBehavior
{
    private enum Phase { Sneak, Up, Sit, Down, Return }

    public const float MinRoomAbove = 150f;

    private Phase _phase;
    private PerchState? _state;
    private RectI _window;
    private IntPtr _handle;
    private Vec2 _hide;
    private Vec2? _out;
    private float _sit;

    public WindowPerchBehavior() => Cooldown = 90f;

    public override string Name => "window-perch";

    public override bool Interruptible => false;

    public override float Score(BehaviorContext ctx)
    {
        var p = ctx.Pet;
        var fg = ctx.User.Foreground;
        if (p.Species != PetSpecies.Cat || Cooldown > 0f || !ctx.Visible || p.Hidden || fg is null || ctx.User.DoNotDisturb) return 0f;
        if (fg.Bounds.Top - ctx.Cell.Bounds.Top < MinRoomAbove || fg.Bounds.Width < 260) return 0f;
        if (PeekBehavior.HideSpot(ctx, fg.Bounds) is null) return 0f;
        return 0.15f + p.Needs.Curiosity / 100f * 0.3f + p.Personality.Playfulness * 0.15f + p.Personality.Activity * 0.1f;
    }

    public override void Start(BehaviorContext ctx)
    {
        var fg = ctx.User.Foreground!;
        _window = fg.Bounds;
        _handle = fg.Handle;
        _hide = PeekBehavior.HideSpot(ctx, _window) ?? ctx.Pet.Position;
        _phase = Phase.Sneak;
        _out = null;
        ctx.Pet.SetAnimation("walk");
    }

    public override bool Update(BehaviorContext ctx, float dt)
    {
        var pet = ctx.Pet;
        var fg = ctx.User.Foreground;
        var gone = fg is null || fg.Handle != _handle || System.Math.Abs(fg.Bounds.X - _window.X) > 4 || System.Math.Abs(fg.Bounds.Y - _window.Y) > 4
                   || System.Math.Abs(fg.Bounds.Width - _window.Width) > 4;
        switch (_phase)
        {
            case Phase.Sneak:
                if (gone) return true;
                if (!PlaceSeeker.WalkTo(ctx, _hide, dt, speedMul: 1.3f)) return false;
                _state = new PerchState
                {
                    WindowRect = _window,
                    WindowHandle = _handle,
                    AnchorX = _window.Left + _window.Width * Rand(ctx, 0.2f, 0.8f),
                };
                pet.Perch = _state;
                pet.Layer = PetLayer.WindowEdge;
                pet.Heading = pet.TargetHeading = ctx.Rng.NextDouble() < 0.5 ? 90f : (ctx.Rng.NextDouble() < 0.5 ? 20f : 160f);
                pet.SetAnimation("sit");
                _phase = Phase.Up;
                return false;
            case Phase.Up:
                if (gone) { _phase = Phase.Down; return false; }
                _state!.Rise = System.Math.Min(1f, _state.Rise + dt / 0.45f);
                if (_state.Rise >= 1f)
                {
                    _phase = Phase.Sit;
                    _sit = Rand(ctx, 8f, 18f);
                    if (ctx.Rng.NextDouble() < 0.5) pet.SetAnimation("lie");
                    if (ctx.Rng.NextDouble() < 0.4) pet.Say(L.S("Nice view from up here", "여기 경치 좋다"), 1.8f);
                }
                return false;
            case Phase.Sit:
                _sit -= dt;
                if (gone || _sit <= 0f) _phase = Phase.Down;
                return false;
            case Phase.Down:
                if (_state is not null)
                {
                    _state.Rise = System.Math.Max(0f, _state.Rise - dt / 0.35f);
                    if (_state.Rise > 0f) return false;
                }
                LeavePerch(pet);
                pet.SetAnimation("walk");
                _out = PeekBehavior.WayOut(ctx, _window);
                _phase = Phase.Return;
                return _out is null;
            default:
                return PlaceSeeker.WalkTo(ctx, _out!.Value, dt);
        }
    }

    private void LeavePerch(Pet pet)
    {
        if (pet.Perch is null) return;
        pet.Perch = null;
        pet.Layer = PetLayer.Backyard;
        pet.Position = _hide;
    }

    public override void Stop(BehaviorContext ctx, bool interrupted)
    {
        LeavePerch(ctx.Pet);
        _state = null;
        ctx.Pet.SetAnimation("idle");
        Cooldown = Rand(ctx, 300f, 600f);
    }
}

/// <summary>
/// Up on the roof (cats love high places): walks to a house, hops up onto the roof and sits on the ridge for a while,
/// then hops down.
/// </summary>
public sealed class RoofPerchBehavior : CooldownBehavior
{
    private enum Phase { Approach, Up, Sit, Down }

    private Phase _phase;
    private PetHouse? _house;
    private Vec2 _ground, _roof, _housePos;
    private float _t, _sit;

    public RoofPerchBehavior() => Cooldown = 60f;

    public override string Name => "roof-perch";

    public override bool Interruptible => false;

    /// <summary>Height of the roof ridge above the house's base (house units at depth 1).</summary>
    public static float RoofHeight(HouseShape shape) => shape == HouseShape.Round ? 106f : 112f;

    public override float Score(BehaviorContext ctx)
    {
        var p = ctx.Pet;
        if (p.Species != PetSpecies.Cat || Cooldown > 0f || p.Hidden || ctx.Weather.Raining || ctx.Weather.Snowing) return 0f;
        if (!ctx.Cell.Houses.Any(h => h.Shape != HouseShape.Tent)) return 0f;
        return 0.2f + p.Personality.Activity * 0.2f + p.Needs.Curiosity / 100f * 0.2f;
    }

    public override void Start(BehaviorContext ctx)
    {
        var pet = ctx.Pet;
        var map = ctx.Cell.Map;
        _house = ctx.Cell.Houses.Where(h => h.Shape != HouseShape.Tent).OrderBy(h => Vec2.Distance(h.Position, pet.Position)).First();
        _housePos = _house.Position;
        var d = map.DepthScale(_house.Position.Y);
        var side = pet.Position.X < _house.Position.X ? -1 : 1;
        _ground = map.ClampToRoam(_house.Position + new Vec2(side * 70f * d, 8f * d));
        _roof = _house.Position + new Vec2(side * 8f * d, -RoofHeight(_house.Shape) * d);
        _phase = Phase.Approach;
    }

    public override bool Update(BehaviorContext ctx, float dt)
    {
        var pet = ctx.Pet;
        var houseGone = _house is null || !ctx.Cell.Houses.Contains(_house) || Vec2.Distance(_house.Position, _housePos) > 2f;
        switch (_phase)
        {
            case Phase.Approach:
                if (houseGone) return true;
                if (!PlaceSeeker.WalkTo(ctx, _ground, dt)) return false;
                pet.FaceToward(_roof - pet.Position);
                pet.SetAnimation("pounce");
                _phase = Phase.Up;
                _t = 0f;
                return false;
            case Phase.Up:
            case Phase.Down:
            {
                _t += dt / 0.55f;
                var k = System.Math.Min(1f, _t);
                var (a, b) = _phase == Phase.Up ? (_ground, _roof) : (_roof, _ground);
                // A hop: along the line with an arc on top, drawn in front of the house while up there.
                var arc = System.MathF.Sin(k * System.MathF.PI) * 30f * ctx.Cell.Map.DepthScale(_housePos.Y);
                pet.Position = a + (b - a) * k - new Vec2(0f, arc);
                pet.ZBoost = _housePos.Y + 2f - pet.Position.Y;
                if (k < 1f) return false;
                if (_phase == Phase.Down) return true;
                pet.SetAnimation(ctx.Rng.NextDouble() < 0.5 ? "sit" : "lie");
                pet.Heading = pet.TargetHeading = 90f;
                _phase = Phase.Sit;
                _sit = Rand(ctx, 10f, 25f);
                if (ctx.Visible && ctx.Rng.NextDouble() < 0.4) pet.Say(L.S("King of the roof", "지붕 위가 내 자리"), 1.8f);
                return false;
            }
            default:
                _sit -= dt;
                if (houseGone)
                {
                    pet.ZBoost = 0f;
                    pet.Position = ctx.Cell.Map.ClampToRoam(_ground);
                    return true;
                }
                if (_sit > 0f && !ctx.Weather.Raining) return false;
                pet.SetAnimation("pounce");
                _phase = Phase.Down;
                _t = 0f;
                return false;
        }
    }

    public override void Stop(BehaviorContext ctx, bool interrupted)
    {
        var pet = ctx.Pet;
        if (pet.ZBoost != 0f || _phase is Phase.Up or Phase.Sit or Phase.Down)
            pet.Position = ctx.Cell.Map.ClampToRoam(_ground);
        pet.ZBoost = 0f;
        pet.SetAnimation("idle");
        Cooldown = Rand(ctx, 240f, 500f);
    }
}

/// <summary>Inviting you to play (dogs): trots up near the pointer, drops into a play bow and barks, bouncing.</summary>
public sealed class InvitePlayBehavior : CooldownBehavior
{
    private Vec2 _spot;
    private bool _bowing;
    private float _t;

    public InvitePlayBehavior() => Cooldown = 60f;

    public override string Name => "invite-play";

    public override float Score(BehaviorContext ctx)
    {
        var p = ctx.Pet;
        var u = ctx.User;
        if (p.Species != PetSpecies.Dog || Cooldown > 0f || !ctx.Visible || p.Hidden || !u.CursorOnDesktop) return 0f;
        if (p.Needs.Fun > 70f || p.Needs.Energy < 40f || p.Bond.Value < 20f || u.CursorStill > 6f) return 0f;
        if (Vec2.Distance(HuntCursorBehavior.Prey(ctx), p.Position) > 500f) return 0f;
        return 0.3f + p.Personality.Playfulness * 0.35f + (70f - p.Needs.Fun) / 70f * 0.3f;
    }

    public override void Start(BehaviorContext ctx)
    {
        var target = HuntCursorBehavior.Prey(ctx);
        var d = ctx.Cell.Map.DepthScale(target.Y);
        _spot = ctx.Cell.Map.ClampToRoam(target + new Vec2(ctx.Pet.Position.X < target.X ? -90f * d : 90f * d, 0f));
        _bowing = false;
        _t = 0f;
    }

    public override bool Update(BehaviorContext ctx, float dt)
    {
        var pet = ctx.Pet;
        if (!_bowing)
        {
            if (!PlaceSeeker.WalkTo(ctx, _spot, dt, speedMul: 1.3f, animation: "run")) return false;
            _bowing = true;
            pet.FaceToward(HuntCursorBehavior.Prey(ctx) - pet.Position);
            pet.SetAnimation("playbow");
            pet.Say(L.S("Play with me!", "놀아 줘!"), 1.8f);
            return false;
        }
        _t += dt;
        // Bounce out of the bow twice, then back down into it.
        var hop = _t % 1.2f;
        pet.Lift = hop is > 0.75f and < 1.15f ? System.MathF.Sin((hop - 0.75f) / 0.4f * System.MathF.PI) * 14f : 0f;
        pet.Needs.Fun = System.Math.Min(100f, pet.Needs.Fun + dt * 0.5f);
        return _t >= 3.6f;
    }

    public override void Stop(BehaviorContext ctx, bool interrupted)
    {
        ctx.Pet.Lift = 0f;
        ctx.Pet.SetAnimation("idle");
        Cooldown = Rand(ctx, 180f, 360f);
    }
}

/// <summary>
/// On guard (dogs): a new window popped up — some dogs (the wary ones above all) turn to it, growl and bark.
/// The simulation raises <see cref="Pet.Alert"/>; this answers it.
/// </summary>
public sealed class AlertBehavior : CooldownBehavior
{
    private float _t;

    public override string Name => "alert";

    public override float Score(BehaviorContext ctx)
    {
        var p = ctx.Pet;
        if (p.Species != PetSpecies.Dog || Cooldown > 0f || p.Alert is null || p.Hidden) return 0f;
        return 1.5f;
    }

    public override void Start(BehaviorContext ctx)
    {
        var pet = ctx.Pet;
        if (pet.Alert is { } where) pet.FaceToward(where - pet.Position);
        pet.Gaze = pet.Alert;
        pet.SetAnimation("hiss");
        pet.Say(L.S("Woof! Who's there?!", "왈! 누구야?!"), 1.6f);
        _t = 0f;
    }

    public override bool Update(BehaviorContext ctx, float dt)
    {
        _t += dt;
        if (_t > 1.2f && ctx.Pet.Animation == "hiss") ctx.Pet.SetAnimation("idle");
        return _t >= 2.2f;
    }

    public override void Stop(BehaviorContext ctx, bool interrupted)
    {
        ctx.Pet.Alert = null;
        ctx.Pet.Gaze = null;
        ctx.Pet.SetAnimation("idle");
        Cooldown = Rand(ctx, 60f, 150f);
    }
}

/// <summary>
/// Chasing a butterfly: one flutters in, the pet watches it, stalks it and pounces — and it always gets away.
/// The butterfly's position is on <see cref="Pet.Butterfly"/> for the renderer.
/// </summary>
public sealed class ButterflyBehavior : CooldownBehavior
{
    private enum Phase { Arrive, Chase, Pounce, Leave }

    private Phase _phase;
    private Vec2 _home, _ground;
    private float _height, _t, _total;
    private int _tries;

    public ButterflyBehavior() => Cooldown = 120f;

    public override string Name => "butterfly";

    public override bool Interruptible => false;

    public override float Score(BehaviorContext ctx)
    {
        var p = ctx.Pet;
        if (Cooldown > 0f || !ctx.Visible || p.Hidden || !ctx.IsDaytime || ctx.Weather.Raining || ctx.Weather.Snowing || p.Needs.Energy < 30f) return 0f;
        return 0.15f + p.Personality.Playfulness * 0.3f + p.Needs.Curiosity / 100f * 0.25f + (p.Species == PetSpecies.Cat ? 0.1f : 0f);
    }

    private Vec2 Screen(BehaviorContext ctx) => _ground - new Vec2(0f, _height * ctx.Cell.Map.DepthScale(_ground.Y));

    public override void Start(BehaviorContext ctx)
    {
        var pet = ctx.Pet;
        var d = ctx.Cell.Map.DepthScale(pet.Position.Y);
        _home = pet.Position;
        var side = ctx.Rng.NextDouble() < 0.5 ? -1 : 1;
        _ground = ctx.Cell.Map.ClampToRoam(pet.Position + new Vec2(side * 95f * d, -20f * d));
        _height = 120f;
        _phase = Phase.Arrive;
        _t = 0f;
        _total = 0f;
        _tries = ctx.Rng.Next(2, 4);
        pet.Butterfly = Screen(ctx);
        pet.SetAnimation(pet.Species == PetSpecies.Cat ? "sit" : "idle");
    }

    public override bool Update(BehaviorContext ctx, float dt)
    {
        var pet = ctx.Pet;
        var map = ctx.Cell.Map;
        var d = map.DepthScale(pet.Position.Y);
        _t += dt;
        _total += dt;
        // Fluttering: a lazy figure of eight around where it is heading, bobbing up and down.
        var flutter = new Vec2(System.MathF.Sin(_total * 2.3f) * 18f * d, System.MathF.Sin(_total * 4.1f) * 8f * d);

        switch (_phase)
        {
            case Phase.Arrive:
                _height = 120f - 60f * System.Math.Min(1f, _t / 1.5f);
                pet.Gaze = Screen(ctx) + flutter;
                if (_t >= 1.5f)
                {
                    _phase = Phase.Chase;
                    _t = 0f;
                    if (ctx.Rng.NextDouble() < 0.5) pet.Say(L.S("Ooh!", "오!"), 1f);
                }
                break;
            case Phase.Chase:
            {
                // It drifts around near the pet; the pet creeps after the spot under it.
                var drift = new Vec2(System.MathF.Sin(_total * 0.7f + 1f) * 90f * d, System.MathF.Cos(_total * 0.5f) * 30f * d);
                _ground = map.ClampToRoam(_home + drift);
                _height = 45f + 20f * System.MathF.Sin(_total * 1.3f);
                pet.Gaze = Screen(ctx) + flutter;
                var under = map.ClampToRoam(new Vec2(_ground.X - System.Math.Sign(_ground.X - pet.Position.X) * 30f * d, _ground.Y + 6f * d));
                var anim = pet.Species == PetSpecies.Cat ? "stalk" : "walk";
                if (Vec2.Distance(under, pet.Position) > 12f * d) PlaceSeeker.WalkTo(ctx, under, dt, speedMul: 0.6f, animation: anim);
                if (_t > 2.2f && Vec2.Distance(_ground, pet.Position) < 70f * d)
                {
                    _phase = Phase.Pounce;
                    _t = 0f;
                    pet.SetAnimation(pet.Species == PetSpecies.Cat ? "pounce" : "playbow");
                }
                break;
            }
            case Phase.Pounce:
                pet.Lift = System.MathF.Sin(System.Math.Min(1f, _t / 0.5f) * System.MathF.PI) * (pet.Species == PetSpecies.Cat ? 34f : 20f);
                _height += dt * 140f;                                           // it flutters up out of reach
                pet.Gaze = Screen(ctx) + flutter;
                if (_t >= 0.5f)
                {
                    pet.Lift = 0f;
                    _tries--;
                    _t = 0f;
                    if (_tries <= 0)
                    {
                        _phase = Phase.Leave;
                        pet.SetAnimation("sit");
                        if (ctx.Visible && ctx.Rng.NextDouble() < 0.7) pet.Say(L.S("Missed!", "놓쳤다!"), 1.4f);
                    }
                    else
                    {
                        _phase = Phase.Chase;
                        _home = map.ClampToRoam(_home + new Vec2(Rand(ctx, -60f, 60f) * d, 0f));
                    }
                }
                break;
            default:
                // Away it goes, up and off to the side.
                _height += dt * 90f;
                _ground = _ground + new Vec2(System.Math.Sign(_ground.X - pet.Position.X + 0.1f) * 70f * d * dt, 0f);
                pet.Gaze = Screen(ctx) + flutter;
                if (_t >= 1.6f) return true;
                break;
        }
        pet.Butterfly = Screen(ctx) + flutter;
        return false;
    }

    public override void Stop(BehaviorContext ctx, bool interrupted)
    {
        var pet = ctx.Pet;
        pet.Butterfly = null;
        pet.Gaze = null;
        pet.Lift = 0f;
        pet.SetAnimation("idle");
        Cooldown = Rand(ctx, 240f, 480f);
    }
}

/// <summary>Kneading (cats): "making biscuits" with the front paws, purring, eyes half shut.</summary>
public sealed class KneadBehavior : CooldownBehavior
{
    private float _t;

    public KneadBehavior() => Cooldown = 60f;

    public override string Name => "knead";

    public override float Score(BehaviorContext ctx)
    {
        var p = ctx.Pet;
        if (p.Species != PetSpecies.Cat || Cooldown > 0f || p.Hidden) return 0f;
        var cosy = p.Needs.Energy < 65f ? 0.2f : 0f;                       // more before a nap
        return 0.12f + p.Personality.Homebody * 0.2f + p.Bond.Value / 100f * 0.15f + cosy;
    }

    public override void Start(BehaviorContext ctx)
    {
        _t = Rand(ctx, 4f, 7f);
        ctx.Pet.SetAnimation("knead");
        if (ctx.Visible && ctx.Rng.NextDouble() < 0.5) ctx.Pet.Say(L.S("Purr… knead knead", "골골… 꾹꾹"), 1.8f);
    }

    public override bool Update(BehaviorContext ctx, float dt)
    {
        _t -= dt;
        ctx.Pet.Needs.ReceiveAffection(dt * 4f);
        return _t <= 0f;
    }

    public override void Stop(BehaviorContext ctx, bool interrupted)
    {
        ctx.Pet.SetAnimation("idle");
        Cooldown = Rand(ctx, 150f, 360f);
    }
}

/// <summary>A good scratch behind the ear with a hind foot (dogs; muddy ones itch more).</summary>
public sealed class ScratchBehavior : CooldownBehavior
{
    private float _t;

    public ScratchBehavior() => Cooldown = 45f;

    public override string Name => "scratch";

    public override float Score(BehaviorContext ctx)
    {
        var p = ctx.Pet;
        if (p.Species != PetSpecies.Dog || Cooldown > 0f || p.Hidden) return 0f;
        return 0.12f + p.Appearance.Mud * 0.4f + (1f - p.Personality.Tidiness) * 0.15f;
    }

    public override void Start(BehaviorContext ctx)
    {
        _t = Rand(ctx, 1.8f, 3f);
        ctx.Pet.SetAnimation("scratch");
        if (ctx.Visible && ctx.Rng.NextDouble() < 0.4) ctx.Pet.Say(L.S("Scritch scratch", "벅벅"), 1.2f);
    }

    public override bool Update(BehaviorContext ctx, float dt)
    {
        _t -= dt;
        return _t <= 0f;
    }

    public override void Stop(BehaviorContext ctx, bool interrupted)
    {
        ctx.Pet.SetAnimation("idle");
        Cooldown = Rand(ctx, 150f, 360f);
    }
}
