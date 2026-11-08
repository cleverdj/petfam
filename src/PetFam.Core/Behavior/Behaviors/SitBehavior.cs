using PetFam.Core.Pets;

namespace PetFam.Core.Behavior.Behaviors;

public sealed class SitBehavior : Behavior
{
    private float _remaining;

    public override string Name => "sit";

    public override float Score(BehaviorContext ctx)
    {
        var p = ctx.Pet;
        var catBonus = p.Species == PetSpecies.Cat ? 0.1f : 0f;
        var tired = (100f - p.Needs.Energy) / 100f * 0.2f;
        return 0.22f + catBonus + tired - p.Personality.Activity * 0.1f;
    }

    public override void Start(BehaviorContext ctx)
    {
        _remaining = ctx.RandomRange(3f, 8f);
        ctx.Pet.SetAnimation("sit");
    }

    public override bool Update(BehaviorContext ctx, float dt)
    {
        _remaining -= dt;
        return _remaining <= 0f;
    }

    public override void Stop(BehaviorContext ctx, bool interrupted) => ctx.Pet.SetAnimation("idle");
}
