using PetFam.Core.Geometry;

namespace PetFam.Core.Behavior.Behaviors;

/// <summary>Lying down: sphinx pose, awake, watching the room. Walks to a favourite spot first (sun for cats, shelter for dogs).</summary>
public sealed class LieBehavior : Behavior
{
    private float _remaining;
    private bool _approaching;
    private Vec2 _spot;

    public override string Name => _approaching ? "lie:approach" : "lie";

    public bool Approaching => _approaching;

    public override float Score(BehaviorContext ctx)
    {
        var p = ctx.Pet;
        var tired = (100f - p.Needs.Energy) / 100f;
        return 0.18f + tired * 0.25f + p.Personality.Homebody * 0.1f - p.Personality.Activity * 0.1f;
    }

    public override void Start(BehaviorContext ctx)
    {
        _remaining = ctx.RandomRange(6f, 14f);
        _approaching = PlaceSeeker.TryPickAny(ctx, PlaceSeeker.RestPreference(ctx.Pet.Species), 900f, out _spot);
        ctx.Pet.SetAnimation(_approaching ? "walk" : "lie");
    }

    public override bool Update(BehaviorContext ctx, float dt)
    {
        if (_approaching)
        {
            if (!PlaceSeeker.WalkTo(ctx, _spot, dt)) return false;
            _approaching = false;
            ctx.Pet.SetAnimation("lie");
        }

        _remaining -= dt;
        ctx.Pet.Needs.Energy = System.Math.Min(100f, ctx.Pet.Needs.Energy + dt * 0.2f);
        return _remaining <= 0f;
    }

    public override void Stop(BehaviorContext ctx, bool interrupted) => ctx.Pet.SetAnimation("idle");
}
