using ZooZoo.Core.Behavior;
using ZooZoo.Core.Behavior.Behaviors;
using ZooZoo.Core.Geometry;
using ZooZoo.Core.Pets;
using ZooZoo.Core.World;

namespace ZooZoo.Core.Interactions;

/// <summary>Holds a pose in place for a while, facing the viewer, with per-second and completion effects.</summary>
public sealed class PoseRunner : InteractionRunner
{
    private readonly string _animation;
    private readonly float _seconds;
    private readonly Action<Pet, float>? _tick;
    private readonly Action<Pet, DateTime>? _complete;
    private readonly string? _say;
    private float _t;

    public PoseRunner(string animation, float seconds, Action<Pet, float>? tick = null, Action<Pet, DateTime>? complete = null, string? say = null)
    {
        _animation = animation;
        _seconds = seconds;
        _tick = tick;
        _complete = complete;
        _say = say;
    }

    public override void Start(BehaviorContext ctx)
    {
        _t = 0f;
        ctx.Pet.TargetHeading = 90f;
        ctx.Pet.SetAnimation(_animation);
        if (_say is not null) ctx.Pet.Say(_say);
    }

    public override bool Update(BehaviorContext ctx, float dt)
    {
        _t += dt;
        Progress = System.Math.Clamp(_t / _seconds, 0f, 1f);
        _tick?.Invoke(ctx.Pet, dt);
        return _t >= _seconds;
    }

    public override void Stop(BehaviorContext ctx, bool completed)
    {
        if (completed) _complete?.Invoke(ctx.Pet, ctx.NowUtc);
        ctx.Pet.SetAnimation("idle");
    }
}

/// <summary>Turns away with a little bubble: the Sims "refuses" reaction.</summary>
public sealed class RefuseRunner : InteractionRunner
{
    public const float Seconds = 1.6f;
    private float _t;

    public override void Start(BehaviorContext ctx)
    {
        _t = 0f;
        var pet = ctx.Pet;
        pet.TargetHeading = Simulation.NormalizeDeg(pet.Heading + 150f);
        pet.SetAnimation("idle");
        pet.Say(pet.Species == PetSpecies.Cat ? "흥!" : "낑…");
    }

    public override bool Update(BehaviorContext ctx, float dt)
    {
        _t += dt;
        Progress = System.Math.Clamp(_t / Seconds, 0f, 1f);
        return _t >= Seconds;
    }
}

/// <summary>Wraps an existing autonomous behavior (eat, sleep) so it can run from the queue.</summary>
public sealed class BehaviorRunner : InteractionRunner
{
    private readonly Behavior.Behavior _inner;
    private readonly Action<BehaviorContext>? _before;
    private readonly float _expectedSeconds;
    private float _t;

    public BehaviorRunner(Behavior.Behavior inner, float expectedSeconds, Action<BehaviorContext>? before = null)
    {
        _inner = inner;
        _expectedSeconds = expectedSeconds;
        _before = before;
    }

    public override void Start(BehaviorContext ctx)
    {
        _t = 0f;
        _before?.Invoke(ctx);
        _inner.Start(ctx);
    }

    public override bool Update(BehaviorContext ctx, float dt)
    {
        _t += dt;
        Progress = System.Math.Clamp(_t / _expectedSeconds, 0f, 0.98f);
        return _inner.Update(ctx, dt);
    }

    public override void Stop(BehaviorContext ctx, bool completed) => _inner.Stop(ctx, interrupted: !completed);
}

/// <summary>
/// 공놀이: the ball flies off, the pet runs after it. Dogs bring it back to where they started;
/// cats bat it once more and chase it again.
/// </summary>
public sealed class FetchRunner : InteractionRunner
{
    private enum Phase { Throw, Chase, Return, Bat, Chase2, Done }

    private const float ThrowSeconds = 0.8f;
    private Phase _phase;
    private Vec2 _start, _from, _to;
    private float _t;
    private Ball? _ball;
    private Cell? _cell;

    public override void Start(BehaviorContext ctx)
    {
        var pet = ctx.Pet;
        _cell = ctx.Cell;
        _start = pet.Position;
        _from = pet.Position;
        _to = RandomSpot(ctx, pet.Position, 220f, 380f);
        _ball = new Ball { Position = _from, Height = 20f };
        ctx.Cell.Ball = _ball;
        _phase = Phase.Throw;
        _t = 0f;
        pet.FaceToward(_to - pet.Position);
        pet.SetAnimation("idle");
        pet.Say(pet.Species == PetSpecies.Dog ? "왈!" : "!");
    }

    public override bool Update(BehaviorContext ctx, float dt)
    {
        var pet = ctx.Pet;
        var ball = _ball!;
        _t += dt;

        switch (_phase)
        {
            case Phase.Throw:
            {
                var k = System.Math.Min(1f, _t / ThrowSeconds);
                ball.Position = Vec2.Lerp(_from, _to, k);
                ball.Height = 20f + MathF.Sin(k * MathF.PI) * 70f;
                if (k >= 1f) Next(Phase.Chase, pet);
                Progress = 0.1f;
                return false;
            }
            case Phase.Chase:
            case Phase.Chase2:
                if (RunTo(ctx, ball.Position, dt, 0.9f))
                {
                    if (pet.Species == PetSpecies.Dog) { ball.CarriedBy = pet.Id; Next(Phase.Return, pet); }
                    else if (_phase == Phase.Chase) Next(Phase.Bat, pet);
                    else Next(Phase.Done, pet);
                }
                Progress = _phase == Phase.Chase ? 0.4f : 0.85f;
                return false;
            case Phase.Return:
                ball.Position = pet.Position;
                ball.Height = 14f;
                if (RunTo(ctx, _start, dt, 1f))
                {
                    ball.CarriedBy = null;
                    ball.Height = 0f;
                    Next(Phase.Done, pet);
                }
                Progress = 0.75f;
                return false;
            case Phase.Bat:
                pet.SetAnimation("dig");
                if (_t >= 0.7f)
                {
                    _from = ball.Position;
                    _to = RandomSpot(ctx, ball.Position, 120f, 200f);
                    _t = 0f;
                    _phase = Phase.Chase2;
                    pet.SetAnimation("run");
                }
                return false;
            default:
                Progress = 1f;
                return true;
        }
    }

    public override void Stop(BehaviorContext ctx, bool completed)
    {
        var pet = ctx.Pet;
        if (completed)
        {
            pet.Needs.Play(40f);
            pet.Needs.Energy = System.Math.Max(0f, pet.Needs.Energy - 8f);
            pet.Bond.Play(ctx.NowUtc);
        }
        if (_cell is not null && ReferenceEquals(_cell.Ball, _ball)) _cell.Ball = null;
        pet.SetAnimation("idle");
    }

    private void Next(Phase p, Pet pet)
    {
        _phase = p;
        _t = 0f;
        if (p is Phase.Chase or Phase.Return) pet.SetAnimation("run");
    }

    /// <summary>Ball rolling while the cat chases it a second time.</summary>
    private bool RunTo(BehaviorContext ctx, Vec2 target, float dt, float stopFactor)
    {
        var pet = ctx.Pet;
        var map = ctx.Cell.Map;
        if (_phase == Phase.Chase2 && _ball is not null && _t < 0.8f)
        {
            _ball.Position = Vec2.Lerp(_from, _to, _t / 0.8f);
            target = _ball.Position;
        }

        var depth = map.DepthScale(pet.Position.Y);
        var reach = 18f * depth * stopFactor;
        var speed = pet.Species.WalkSpeed() * pet.Species.RunMultiplier() * depth;
        var delta = target - pet.Position;
        if (delta.Length <= reach) return true;
        var step = System.Math.Min(speed * dt, delta.Length - reach * 0.9f);
        pet.Position = map.ClampToRoam(pet.Position + delta.Normalized * step);
        pet.FaceToward(delta);
        pet.SetAnimation("run");
        return false;
    }

    private static Vec2 RandomSpot(BehaviorContext ctx, Vec2 around, float min, float max)
    {
        var map = ctx.Cell.Map;
        for (var i = 0; i < 12; i++)
        {
            var a = ctx.RandomRange(0f, MathF.PI * 2f);
            var r = ctx.RandomRange(min, max);
            var p = map.ClampToRoam(around + new Vec2(MathF.Cos(a) * r, MathF.Sin(a) * r * 0.6f));
            if (Vec2.Distance(p, around) >= min * 0.7f) return p;
        }
        return map.ClampToRoam(around + new Vec2(min, 0));
    }
}
