using ZooZoo.Core.Geometry;
using ZooZoo.Core.Pets;
using ZooZoo.Core.World;

namespace ZooZoo.Core.Social;

public sealed class SocialContext
{
    public required Cell Cell { get; init; }
    public required SocialState Social { get; init; }
    public required Random Rng { get; init; }
    public DateTime NowUtc { get; init; }

    public float Rand(float min, float max) => min + (float)Rng.NextDouble() * (max - min);
}

/// <summary>
/// A scripted two-pet encounter (play, chase, wrestling, a hiss). The session drives both pets directly; while it
/// runs each pet's behavior engine sits in SocialBehavior. A mouse hold or a user command aborts it.
/// </summary>
public abstract class SocialSession
{
    public Pet Initiator { get; }
    public Pet Partner { get; }
    public bool Done { get; private set; }
    public abstract string Name { get; }

    /// <summary>Seconds in the current phase.</summary>
    protected float T;

    /// <summary>Whole session time; sessions end themselves well before this.</summary>
    protected float Total;

    public const float MaxSeconds = 40f;

    protected SocialSession(Pet initiator, Pet partner)
    {
        Initiator = initiator;
        Partner = partner;
    }

    public void Begin()
    {
        Initiator.Session = this;
        Partner.Session = this;
    }

    public void Update(SocialContext ctx, float dt)
    {
        if (Done) return;
        T += dt;
        Total += dt;
        if (Total > MaxSeconds) { End(); return; }
        Step(ctx, dt);
    }

    protected abstract void Step(SocialContext ctx, float dt);

    public void Abort() => End();

    protected void End()
    {
        if (Done) return;
        Done = true;
        foreach (var p in new[] { Initiator, Partner })
        {
            if (!ReferenceEquals(p.Session, this)) continue;
            p.Session = null;
            p.Lift = 0f;
            if (p.Hold == HoldState.None) p.SetAnimation("idle");
        }
    }

    protected void Next<TPhase>(ref TPhase phase, TPhase next)
    {
        phase = next;
        T = 0f;
    }

    // ---------------------------------------------------------------- movement helpers

    /// <summary>Moves a pet toward target over the floor at speedMul × its walk speed. True on arrival.</summary>
    protected static bool MoveTo(SocialContext ctx, Pet pet, Vec2 target, float dt, float speedMul, string animation, float arrive = 6f)
    {
        var map = ctx.Cell.Map;
        var delta = target - pet.Position;
        var dist = delta.Length;
        if (dist <= arrive) return true;
        var speed = pet.Species.WalkSpeed() * map.DepthScale(pet.Position.Y) * speedMul;
        pet.FaceToward(delta);
        pet.SetAnimation(animation);
        pet.Position = map.ClampToRoam(pet.Position + delta.Normalized * System.Math.Min(dist, speed * dt));
        return Vec2.Distance(pet.Position, target) <= arrive;
    }

    protected static void Face(Pet pet, Pet other) => pet.FaceToward(other.Position - pet.Position);

    /// <summary>A floor spot at about distance from target, on the side facing from.</summary>
    protected static Vec2 Beside(SocialContext ctx, Pet target, Vec2 from, float distance)
    {
        var depth = ctx.Cell.Map.DepthScale(target.Position.Y);
        var dir = (from - target.Position).Normalized;
        if (dir.Length < 0.5f) dir = new Vec2(1, 0);
        return ctx.Cell.Map.ClampToRoam(target.Position + dir * (distance * depth));
    }

    /// <summary>A floor spot roughly distance away from 'from', moving away from it.</summary>
    protected static Vec2 AwayFrom(SocialContext ctx, Pet pet, Vec2 from, float distance)
    {
        var map = ctx.Cell.Map;
        var depth = map.DepthScale(pet.Position.Y);
        var dir = (pet.Position - from).Normalized;
        if (dir.Length < 0.5f) dir = new Vec2(ctx.Rng.Next(2) == 0 ? -1 : 1, 0);
        for (var i = 0; i < 10; i++)
        {
            var a = MathF.Atan2(dir.Y, dir.X) + ctx.Rand(-0.9f, 0.9f);
            var p = map.ClampToRoam(pet.Position + new Vec2(MathF.Cos(a), MathF.Sin(a) * 0.6f) * (distance * depth));
            if (Vec2.Distance(p, from) > Vec2.Distance(pet.Position, from) + distance * depth * 0.4f) return p;
        }
        return map.ClampToRoam(pet.Position + dir * (distance * depth));
    }

    /// <summary>Is this pet in a state to accept play? Tired, grumpy or unfriendly pets say no.</summary>
    protected static bool Accepts(SocialContext ctx, Pet pet, Pet other, float bias = 0f)
    {
        var friend = ctx.Social.Friendship(pet, other) / 100f;
        var chance = 0.2f + 0.5f * friend + 0.3f * pet.Personality.Playfulness + bias;
        if (pet.Needs.Energy < 30f) chance -= 0.3f;
        if (MoodCalc.Level(pet) == MoodLevel.Bad) chance -= 0.25f;
        return ctx.Rng.NextDouble() < System.Math.Clamp(chance, 0.05f, 0.95f);
    }

    /// <summary>Who wins a tussle: mostly the higher-ranked pet, sometimes the other.</summary>
    protected static Pet Winner(SocialContext ctx, Pet a, Pet b)
    {
        var edge = ctx.Social.DominanceOf(a, b);
        return ctx.Rng.NextDouble() < 0.5 + edge * 0.35 ? a : b;
    }

    /// <summary>Parabolic hop height for jumps and pounces.</summary>
    protected static float Hop(float k, float height) => MathF.Sin(System.Math.Clamp(k, 0f, 1f) * MathF.PI) * height;
}
