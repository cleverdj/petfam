using PetFam.Core.Behavior;
using PetFam.Core.Behavior.Behaviors;
using PetFam.Core.Geometry;
using PetFam.Core.Pets;
using PetFam.Core.World;

namespace PetFam.Core.Interactions;

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
        pet.Say(pet.Species == PetSpecies.Cat ? L.S("Hmph!", "흥!") : L.S("Whimper…", "낑…"));
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
/// Fetch: the pet drops the ball at its feet and waits for the person to throw it: grab the ball with the
/// mouse and flick it. The ball flies, bounces and rolls; the pet chases it. Dogs bring it back to where it
/// was thrown from and wait for the next throw; cats bat it about once. A pet left waiting at the start nudges
/// the ball itself; after a few rounds, or when nobody throws again, the game ends.
/// </summary>
public sealed class FetchRunner : InteractionRunner
{
    private enum Phase { Wait, Chase, Return, Bat, Done }

    public const int MaxRounds = 5;
    public const float FirstWaitSeconds = 12f;
    public const float NextWaitSeconds = 9f;

    private Phase _phase;
    private float _t;
    private int _rounds, _seenThrows;
    private bool _batted;
    private Ball? _ball;
    private Cell? _cell;
    private Vec2 _home;

    public int Rounds => _rounds;

    public override void Start(BehaviorContext ctx)
    {
        var pet = ctx.Pet;
        _cell = ctx.Cell;
        _home = pet.Position;
        var depth = ctx.Cell.Map.DepthScale(pet.Position.Y);
        _ball = new Ball { Position = ctx.Cell.Map.ClampToRoam(pet.Position + Ahead(pet, 30f * depth)) };
        ctx.Cell.Ball = _ball;
        _seenThrows = 0;
        Next(Phase.Wait, pet);
        pet.Say(pet.Species == PetSpecies.Dog ? L.S("Woof! Throw it!", "왈! 던져줘!") : "!");
    }

    public override bool Update(BehaviorContext ctx, float dt)
    {
        var pet = ctx.Pet;
        var ball = _ball!;
        if (!ReferenceEquals(ctx.Cell.Ball, ball)) return true;
        _t += dt;
        var map = ctx.Cell.Map;
        var depth = map.DepthScale(pet.Position.Y);

        // A new throw by the person: chase it, and bring it back to where it was thrown from.
        if (ball.Throws != _seenThrows)
        {
            _seenThrows = ball.Throws;
            _home = map.ClampToRoam(ball.ThrownFrom);
            _batted = false;
            Next(Phase.Chase, pet);
            if (_rounds == 0) pet.Say(pet.Species == PetSpecies.Dog ? L.S("Woof!", "왈!") : "!");
        }

        Progress = System.Math.Min(0.95f, (_rounds + (_phase == Phase.Wait ? 0f : 0.5f)) / MaxRounds);
        switch (_phase)
        {
            case Phase.Wait:
            {
                // Eyes on the ball, bouncing on the front paws.
                pet.LookAt = ball.Position - new Vec2(0, ball.Height * depth);
                var toBall = ball.Position - pet.Position;
                if (toBall.Length > 12f * depth) pet.FaceToward(toBall);
                var eager = ball.HeldByUser || _t % 3f < 1.6f;
                pet.SetAnimation(pet.Species == PetSpecies.Dog
                    ? (eager ? "playbow" : "idle")
                    : (ball.HeldByUser ? "wiggle" : "sit"));

                if (ball.HeldByUser) { _t = 0f; return false; }
                if (_rounds == 0 && _t > FirstWaitSeconds)
                {
                    // Nobody threw it: nose it away and chase it anyway.
                    var dir = Ahead(pet, 1f);
                    ball.Velocity = dir * 420f + new Vec2(ctx.RandomRange(-80f, 80f), ctx.RandomRange(-60f, 60f));
                    ball.VerticalSpeed = 160f;
                    _home = pet.Position;
                    Next(Phase.Chase, pet);
                }
                else if (_rounds > 0 && _t > NextWaitSeconds)
                {
                    Next(Phase.Done, pet);
                }
                return false;
            }
            case Phase.Chase:
            {
                pet.LookAt = null;
                if (ball.HeldByUser) { Next(Phase.Wait, pet); return false; }
                var reach = 20f * depth;
                var near = Vec2.Distance(pet.Position, ball.Position) <= reach;
                if (near && ball.Height < 14f && ball.Velocity.Length < 450f)
                {
                    if (pet.Species == PetSpecies.Dog)
                    {
                        ball.CarriedBy = pet.Id;
                        ball.Velocity = Vec2.Zero;
                        ball.VerticalSpeed = 0f;
                        Next(Phase.Return, pet);
                    }
                    else if (!_batted)
                    {
                        Next(Phase.Bat, pet);
                    }
                    else
                    {
                        _rounds++;
                        Next(_rounds >= MaxRounds ? Phase.Done : Phase.Wait, pet);
                    }
                    return false;
                }
                // Run to where the ball is going, not where it is.
                if (RunTo(ctx, ball.Position + ball.Velocity * 0.25f, dt, reach * 0.8f))
                {
                    pet.FaceToward(ball.Position - pet.Position);
                    pet.SetAnimation("idle");
                }
                return false;
            }
            case Phase.Return:
            {
                ball.Position = map.ClampToRoam(pet.Position + Ahead(pet, 16f * depth));
                ball.Height = 14f;
                if (RunTo(ctx, _home, dt, 22f * depth))
                {
                    ball.CarriedBy = null;
                    ball.Height = 0f;
                    ball.Position = map.ClampToRoam(pet.Position + Ahead(pet, 26f * depth));
                    _rounds++;
                    pet.Needs.Play(6f);
                    if (_rounds >= MaxRounds)
                    {
                        Next(Phase.Done, pet);
                    }
                    else
                    {
                        Next(Phase.Wait, pet);
                        pet.Say(L.S("Throw it again!", "또 던져줘!"));
                    }
                }
                return false;
            }
            case Phase.Bat:
                pet.SetAnimation("bat");
                pet.FaceToward(ball.Position - pet.Position);
                if (_t >= 0.35f)
                {
                    var a = ctx.RandomRange(-0.9f, 0.9f);
                    var f = Ahead(pet, 1f);
                    var dir = new Vec2(f.X * MathF.Cos(a) - f.Y * MathF.Sin(a), f.X * MathF.Sin(a) + f.Y * MathF.Cos(a));
                    ball.Velocity = dir * 320f;
                    ball.VerticalSpeed = 90f;
                    _batted = true;
                    Next(Phase.Chase, pet);
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
        pet.LookAt = null;
        pet.SetAnimation("idle");
    }

    private void Next(Phase p, Pet pet)
    {
        _phase = p;
        _t = 0f;
        if (p is Phase.Chase or Phase.Return) pet.SetAnimation("run");
    }

    /// <summary>Screen direction the pet faces, flattened for the ground plane, times length.</summary>
    private static Vec2 Ahead(Pet pet, float length)
    {
        var h = pet.Heading * MathF.PI / 180f;
        return new Vec2(MathF.Cos(h), MathF.Sin(h) * 0.6f).Normalized * length;
    }

    private static bool RunTo(BehaviorContext ctx, Vec2 target, float dt, float reach)
    {
        var pet = ctx.Pet;
        var map = ctx.Cell.Map;
        var depth = map.DepthScale(pet.Position.Y);
        var speed = pet.WalkSpeed() * pet.RunMultiplier() * depth;
        var delta = target - pet.Position;
        if (delta.Length <= reach) return true;
        var step = pet.Motion.Step(pet, delta * ((delta.Length - reach * 0.9f) / delta.Length), speed, dt);
        pet.Position = map.ClampToRoam(pet.Position + delta.Normalized * step);
        pet.FaceToward(delta);
        pet.SetAnimation("run");
        return false;
    }
}
