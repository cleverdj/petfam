using ZooZoo.Core.Geometry;
using ZooZoo.Core.World;

namespace ZooZoo.Core.Behavior.Behaviors;

/// <summary>Sleep. Goes to a favourite spot first when there is one (sun for cats, shelter for dogs).</summary>
public sealed class SleepBehavior : Behavior
{
    private float _remaining;
    private bool _approaching;
    private Vec2 _spot;

    public override string Name => _approaching ? "sleep:approach" : "sleep";

    public override bool Interruptible => false;

    public override float Score(BehaviorContext ctx)
    {
        var energy = ctx.Pet.Needs.Energy;
        if (energy > 60f) return 0f;
        var nightBonus = ctx.IsDaytime ? 0f : 0.15f;
        return (100f - energy) / 100f * 0.9f + nightBonus;
    }

    public override void Start(BehaviorContext ctx)
    {
        // Spike timing: short naps so the cycle is observable. Real pacing comes with needs tuning.
        _remaining = ctx.RandomRange(25f, 60f);
        // A house beats everything: go in and sleep inside (hidden). Otherwise the favourite resting place.
        _approaching = PlaceSeeker.TryPick(ctx, ZoneType.House, 1400f, out _spot)
                       || PlaceSeeker.TryPickAny(ctx, PlaceSeeker.RestPreference(ctx.Pet.Species), 900f, out _spot);
        ctx.Pet.SetAnimation(_approaching ? "walk" : "sleep");
    }

    public override bool Update(BehaviorContext ctx, float dt)
    {
        if (_approaching)
        {
            if (!PlaceSeeker.WalkTo(ctx, _spot, dt)) return false;
            _approaching = false;
            ctx.Pet.SetAnimation("sleep");
            if (ctx.Cell.Map.ZoneAt(ctx.Pet.Position, ZoneType.House) is not null) ctx.Pet.Hidden = true;
        }

        _remaining -= dt;
        // Fast regen while napping so the spike shows wake-up; Needs.Tick also regenerates slowly.
        ctx.Pet.Needs.Energy = System.Math.Min(100f, ctx.Pet.Needs.Energy + dt * 1.2f);
        return _remaining <= 0f || ctx.Pet.Needs.Energy >= 100f;
    }

    public override void Stop(BehaviorContext ctx, bool interrupted)
    {
        ctx.Pet.Hidden = false;   // comes out of the house
        ctx.Pet.JustWokeUp = !interrupted && !_approaching;
        ctx.Pet.SetAnimation("idle");
    }
}
