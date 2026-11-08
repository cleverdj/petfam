using PetFam.Core.Geometry;
using PetFam.Core.Pets;
using PetFam.Core.World;

namespace PetFam.Core.Behavior.Behaviors;

/// <summary>A behavior that rests for a while after it ran, so the pet does not repeat itself.</summary>
public abstract class CooldownBehavior : Behavior
{
    protected float Cooldown;

    public void TickCooldown(float dt) => Cooldown = System.Math.Max(0f, Cooldown - dt);

    /// <summary>Youth bonus for lively behaviors: babies and juniors do it more, seniors much less.</summary>
    protected static float Youth(Pet pet) => pet.Stage switch
    {
        LifeStage.Baby => 0.15f,
        LifeStage.Junior => 0.3f,
        LifeStage.Senior => -0.35f,
        _ => 0f,
    };

    protected static float Rand(BehaviorContext ctx, float min, float max) => ctx.RandomRange(min, max);
}

/// <summary>
/// Zoomies: out of nowhere the pet tears around the floor in a zigzag at full speed, then sits panting with its
/// tongue out. Young, active, playful pets do it most; cats get their "midnight crazies" after dark.
/// </summary>
public sealed class ZoomiesBehavior : CooldownBehavior
{
    private readonly List<Vec2> _route = new();
    private float _pant;

    public ZoomiesBehavior() => Cooldown = 60f;

    public override string Name => _route.Count > 0 ? "zoomies" : "zoomies:pant";

    public override bool Interruptible => false;

    public override float Score(BehaviorContext ctx)
    {
        var p = ctx.Pet;
        if (Cooldown > 0f || p.Needs.Energy < 50f || p.Hidden) return 0f;
        var funDeficit = (100f - p.Needs.Fun) / 100f;
        var nightCat = p.Species == PetSpecies.Cat && !ctx.IsDaytime ? 0.3f : 0f;
        return 0.15f + p.Personality.Activity * 0.35f + p.Personality.Playfulness * 0.2f + funDeficit * 0.25f + Youth(p) + nightCat;
    }

    public override void Start(BehaviorContext ctx)
    {
        var map = ctx.Cell.Map;
        _route.Clear();
        var from = ctx.Pet.Position;
        // A zigzag of far-apart points: each leg crosses a good part of the floor.
        for (var i = 0; i < 6 && _route.Count < 5; i++)
        {
            for (var tries = 0; tries < 20; tries++)
            {
                var t = map.RandomPoint(ctx.Rng);
                if (!map.CanRoam(t) || Vec2.Distance(t, from) < 260f * map.DepthScale(t.Y)) continue;
                _route.Add(t);
                from = t;
                break;
            }
        }
        _pant = Rand(ctx, 2.5f, 4f);
        if (ctx.Visible && ctx.Rng.NextDouble() < 0.5) ctx.Pet.Say(L.S("Zoom!", "우다다다!"), 1.2f);
    }

    public override bool Update(BehaviorContext ctx, float dt)
    {
        var pet = ctx.Pet;
        if (_route.Count > 0)
        {
            if (PlaceSeeker.WalkTo(ctx, _route[0], dt, speedMul: pet.RunMultiplier() * 1.3f, animation: "run")) _route.RemoveAt(0);
            pet.Needs.Energy = System.Math.Max(0f, pet.Needs.Energy - dt * 1.5f);
            pet.Needs.Fun = System.Math.Min(100f, pet.Needs.Fun + dt * 3f);
            if (_route.Count == 0)
            {
                pet.SetAnimation("pant");
                if (ctx.Visible && ctx.Rng.NextDouble() < 0.6) pet.Say(pet.Species == PetSpecies.Dog ? L.S("Huff, huff!", "헥헥!") : L.S("…what was that?", "…방금 뭐였지?"), 1.8f);
            }
            return false;
        }
        _pant -= dt;
        return _pant <= 0f;
    }

    public override void Stop(BehaviorContext ctx, bool interrupted)
    {
        _route.Clear();
        ctx.Pet.SetAnimation("idle");
        Cooldown = Rand(ctx, 150f, 360f);
    }
}

/// <summary>Chasing its own tail (dogs): spins in a tight circle, then sits down, dizzy.</summary>
public sealed class ChaseTailBehavior : CooldownBehavior
{
    private Vec2 _center;
    private float _angle, _left, _dizzy;

    public ChaseTailBehavior() => Cooldown = 90f;

    public override string Name => _left > 0 ? "chase-tail" : "chase-tail:dizzy";

    public override bool Interruptible => false;

    /// <summary>Degrees turned so far (for tests).</summary>
    public float Turned { get; private set; }

    public override float Score(BehaviorContext ctx)
    {
        var p = ctx.Pet;
        if (p.Species != PetSpecies.Dog || Cooldown > 0f || p.Needs.Energy < 40f || p.Hidden) return 0f;
        var funDeficit = (100f - p.Needs.Fun) / 100f;
        return 0.1f + p.Personality.Playfulness * 0.35f + funDeficit * 0.2f + Youth(p);
    }

    public override void Start(BehaviorContext ctx)
    {
        var pet = ctx.Pet;
        _center = pet.Position;
        _angle = pet.Heading;
        _left = Rand(ctx, 2.5f, 4f);
        _dizzy = 1.6f;
        Turned = 0f;
        pet.SetAnimation("run");
    }

    public override bool Update(BehaviorContext ctx, float dt)
    {
        var pet = ctx.Pet;
        if (_left > 0f)
        {
            _left -= dt;
            const float DegPerSec = 420f;
            _angle += DegPerSec * dt;
            Turned += DegPerSec * dt;
            var depth = ctx.Cell.Map.DepthScale(_center.Y);
            var rad = _angle * System.MathF.PI / 180f;
            // Running round a small circle: the body faces along it, chasing the tail.
            var radius = 16f * depth;
            var next = _center + new Vec2(System.MathF.Sin(rad) * radius, -System.MathF.Cos(rad) * radius * 0.5f);
            pet.Motion.Step(pet, next - pet.Position, pet.WalkSpeed() * 1.6f * depth, dt);
            pet.Position = next;
            pet.Heading = pet.TargetHeading = _angle;
            if (_left <= 0f)
            {
                pet.Position = _center;
                pet.SetAnimation("sit");
                if (ctx.Visible) pet.Say(L.S("Dizzy… @_@", "어지러워… @_@"), 1.6f);
            }
            return false;
        }
        _dizzy -= dt;
        return _dizzy <= 0f;
    }

    public override void Stop(BehaviorContext ctx, bool interrupted)
    {
        ctx.Pet.Position = ctx.Cell.Map.ClampToRoam(ctx.Pet.Position);
        ctx.Pet.SetAnimation("idle");
        Cooldown = Rand(ctx, 240f, 480f);
    }
}

/// <summary>Sniffing around (dogs): nose down, from one interesting spot to the next. Hounds and foodies love it.</summary>
public sealed class SniffBehavior : CooldownBehavior
{
    private readonly List<Vec2> _spots = new();
    private float _sniff;

    public SniffBehavior() => Cooldown = 30f;

    public override string Name => _sniff > 0 ? "sniff" : "sniff:walk";

    public override float Score(BehaviorContext ctx)
    {
        var p = ctx.Pet;
        if (p.Species != PetSpecies.Dog || Cooldown > 0f || p.Hidden) return 0f;
        var nose = System.Math.Max(0f, p.Breed.Appetite - 1f) * 0.6f;   // a Beagle or a Corgi follows its nose
        return 0.2f + p.Needs.Curiosity / 100f * 0.35f + p.Personality.Activity * 0.1f + nose;
    }

    public override void Start(BehaviorContext ctx)
    {
        var map = ctx.Cell.Map;
        var pet = ctx.Pet;
        _spots.Clear();
        var from = pet.Position;
        var count = ctx.Rng.Next(3, 5);
        for (var tries = 0; tries < 40 && _spots.Count < count; tries++)
        {
            var d = map.DepthScale(from.Y);
            var t = map.ClampToRoam(from + new Vec2(Rand(ctx, -220f, 220f) * d, Rand(ctx, -70f, 70f) * d));
            if (!map.CanRoam(t) || Vec2.Distance(t, from) < 50f * d) continue;
            _spots.Add(t);
            from = t;
        }
        _sniff = 0f;
    }

    public override bool Update(BehaviorContext ctx, float dt)
    {
        var pet = ctx.Pet;
        if (_sniff > 0f)
        {
            _sniff -= dt;
            pet.Needs.Curiosity = System.Math.Max(0f, pet.Needs.Curiosity - dt * 2f);
            return _sniff <= 0f && _spots.Count == 0;
        }
        if (_spots.Count == 0) return true;
        if (!PlaceSeeker.WalkTo(ctx, _spots[0], dt, speedMul: 0.55f)) return false;
        _spots.RemoveAt(0);
        _sniff = Rand(ctx, 1.2f, 2.2f);
        pet.SetAnimation("sniff");
        if (ctx.Visible && ctx.Rng.NextDouble() < 0.3)
            pet.Say(_spots.Count == 0 && ctx.Rng.NextDouble() < 0.5 ? L.S("Found something!", "뭔가 찾았다!") : L.S("Sniff sniff", "킁킁"), 1.2f);
        return false;
    }

    public override void Stop(BehaviorContext ctx, bool interrupted)
    {
        _spots.Clear();
        _sniff = 0f;
        ctx.Pet.SetAnimation("idle");
        Cooldown = Rand(ctx, 90f, 200f);
    }
}

/// <summary>
/// Bird watching (cats): sits still and stares at something up in the sky, tail twitching, now and then chattering
/// at it ("ek-ek-ek").
/// </summary>
public sealed class WatchBehavior : CooldownBehavior
{
    private float _left;
    private bool _chattered;

    public WatchBehavior() => Cooldown = 40f;

    public override string Name => "watch";

    public override float Score(BehaviorContext ctx)
    {
        var p = ctx.Pet;
        if (p.Species != PetSpecies.Cat || Cooldown > 0f || p.Hidden) return 0f;
        return 0.2f + p.Needs.Curiosity / 100f * 0.35f + p.Personality.Homebody * 0.15f;
    }

    public override void Start(BehaviorContext ctx)
    {
        var pet = ctx.Pet;
        var map = ctx.Cell.Map;
        var b = ctx.Cell.Bounds;
        // Something up there: above the floor, a little to one side.
        var y = System.Math.Clamp(map.RoamArea.Top - Rand(ctx, 120f, 420f), b.Top + 40f, map.RoamArea.Top - 20f);
        var x = System.Math.Clamp(pet.Position.X + Rand(ctx, -350f, 350f), b.Left + 40f, b.Right - 40f);
        pet.Gaze = new Vec2(x, y);
        pet.FaceToward(pet.Gaze.Value - pet.Position);
        pet.SetAnimation("sit");
        _left = Rand(ctx, 6f, 11f);
        _chattered = false;
    }

    public override bool Update(BehaviorContext ctx, float dt)
    {
        var pet = ctx.Pet;
        _left -= dt;
        pet.Needs.Curiosity = System.Math.Max(0f, pet.Needs.Curiosity - dt * 1.5f);
        if (!_chattered && _left < 4f)
        {
            _chattered = true;
            if (ctx.Visible && ctx.Rng.NextDouble() < 0.6) pet.Say(L.S("Ek-ek-ek!", "깍깍깍!"), 1.4f);
        }
        return _left <= 0f;
    }

    public override void Stop(BehaviorContext ctx, bool interrupted)
    {
        ctx.Pet.Gaze = null;
        ctx.Pet.SetAnimation("idle");
        Cooldown = Rand(ctx, 90f, 220f);
    }
}

/// <summary>Begging (dogs): when peckish and there is no food out, comes over under the pointer, sits and looks up.</summary>
public sealed class BegBehavior : CooldownBehavior
{
    private Vec2 _spot;
    private float _wait;
    private bool _sitting;

    public BegBehavior() => Cooldown = 20f;

    public override string Name => _sitting ? "beg" : "beg:approach";

    public override float Score(BehaviorContext ctx)
    {
        var p = ctx.Pet;
        if (p.Species != PetSpecies.Dog || Cooldown > 0f || !ctx.Visible || p.Hidden) return 0f;
        if (p.Needs.Hunger > 50f || !ctx.User.IsActive) return 0f;
        if (ctx.Cell.Bowls.Any(b => b.Amount > 0f)) return 0f;                 // there is food: it goes and eats
        return 0.35f + (50f - p.Needs.Hunger) / 50f * 0.5f + System.Math.Max(0f, p.Breed.Appetite - 1f) * 0.4f;
    }

    public override void Start(BehaviorContext ctx)
    {
        var target = HuntCursorBehavior.Prey(ctx);
        var d = ctx.Cell.Map.DepthScale(target.Y);
        _spot = ctx.Cell.Map.ClampToRoam(target + new Vec2(ctx.Pet.Position.X < target.X ? -50f * d : 50f * d, 10f * d));
        _sitting = false;
        _wait = Rand(ctx, 6f, 9f);
    }

    public override bool Update(BehaviorContext ctx, float dt)
    {
        var pet = ctx.Pet;
        if (ctx.Cell.Bowls.Any(b => b.Amount > 0f)) return true;              // fed: off to the bowl
        if (!_sitting)
        {
            if (!PlaceSeeker.WalkTo(ctx, _spot, dt, speedMul: 1.2f)) return false;
            _sitting = true;
            pet.SetAnimation("sit");
            pet.Gaze = ctx.User.Cursor;
            pet.SayAs(v => v.Hungry, ctx.Rng, 2.4f);
            return false;
        }
        pet.Gaze = ctx.User.Cursor;
        _wait -= dt;
        return _wait <= 0f;
    }

    public override void Stop(BehaviorContext ctx, bool interrupted)
    {
        ctx.Pet.Gaze = null;
        ctx.Pet.SetAnimation("idle");
        Cooldown = Rand(ctx, 120f, 240f);
    }
}

/// <summary>Following the owner (dogs that trust you): trots along the floor after a moving pointer.</summary>
public sealed class FollowCursorBehavior : CooldownBehavior
{
    private float _left;

    public FollowCursorBehavior() => Cooldown = 15f;

    public override string Name => "follow-cursor";

    public override float Score(BehaviorContext ctx)
    {
        var p = ctx.Pet;
        var u = ctx.User;
        if (p.Species != PetSpecies.Dog || Cooldown > 0f || !ctx.Visible || p.Hidden) return 0f;
        if (!u.CursorOnDesktop || u.CursorSpeed < 120f || p.Bond.Value < 40f || p.Needs.Energy < 30f) return 0f;
        return 0.35f + p.Bond.Value / 100f * 0.4f + (p.Travel == TravelMode.Follow ? 0.1f : 0f);
    }

    public override void Start(BehaviorContext ctx)
    {
        _left = Rand(ctx, 7f, 12f);
        if (ctx.Rng.NextDouble() < 0.4) ctx.Pet.Say(L.S("Wait for me!", "같이 가!"), 1.4f);
    }

    public override bool Update(BehaviorContext ctx, float dt)
    {
        var pet = ctx.Pet;
        var u = ctx.User;
        _left -= dt;
        if (_left <= 0f || !u.CursorOnDesktop || u.CursorStill > 2.5f) return true;
        var target = HuntCursorBehavior.Prey(ctx);
        var d = ctx.Cell.Map.DepthScale(pet.Position.Y);
        var gap = Vec2.Distance(target, pet.Position);
        if (gap < 70f * d)
        {
            pet.SetAnimation("idle");
            pet.FaceToward(target - pet.Position);
            return false;
        }
        var run = gap > 260f * d;
        var keep = target + (pet.Position - target).Normalized * (60f * d);   // stay a step behind
        PlaceSeeker.WalkTo(ctx, ctx.Cell.Map.ClampToRoam(keep), dt, speedMul: run ? pet.RunMultiplier() : 1.1f, animation: run ? "run" : "walk");
        return false;
    }

    public override void Stop(BehaviorContext ctx, bool interrupted)
    {
        ctx.Pet.SetAnimation("idle");
        Cooldown = Rand(ctx, 30f, 90f);
    }
}

/// <summary>A big yawn when getting tired.</summary>
public sealed class YawnBehavior : CooldownBehavior
{
    private float _left;

    public YawnBehavior() => Cooldown = 30f;

    public override string Name => "yawn";

    public override float Score(BehaviorContext ctx)
    {
        var p = ctx.Pet;
        if (Cooldown > 0f || p.Needs.Energy > 70f || p.Hidden) return 0f;
        return 0.15f + (70f - p.Needs.Energy) / 70f * 0.45f;
    }

    public override void Start(BehaviorContext ctx)
    {
        _left = 1.8f;
        ctx.Pet.SetAnimation("yawn");
        if (ctx.Visible && ctx.Rng.NextDouble() < 0.4) ctx.Pet.Say(L.S("*yaaawn*", "*하아암*"), 1.4f);
    }

    public override bool Update(BehaviorContext ctx, float dt)
    {
        _left -= dt;
        return _left <= 0f;
    }

    public override void Stop(BehaviorContext ctx, bool interrupted)
    {
        ctx.Pet.SetAnimation("idle");
        Cooldown = Rand(ctx, 90f, 200f);
    }
}
