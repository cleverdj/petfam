using ZooZoo.Core.Pets;

namespace ZooZoo.Core.Behavior.Behaviors;

/// <summary>Cats groom: sit and lick. Removes mud much faster than passive grooming.</summary>
public sealed class GroomBehavior : Behavior
{
    private float _remaining;

    public override string Name => "groom";

    public override float Score(BehaviorContext ctx)
    {
        var p = ctx.Pet;
        if (p.Species != PetSpecies.Cat) return 0f;
        return 0.08f + p.Personality.Tidiness * 0.15f + p.Appearance.Mud * 0.6f + p.Appearance.Wet * 0.3f;
    }

    public override void Start(BehaviorContext ctx)
    {
        _remaining = ctx.RandomRange(5f, 10f);
        ctx.Pet.SetAnimation("groom");
    }

    public override bool Update(BehaviorContext ctx, float dt)
    {
        _remaining -= dt;
        ctx.Pet.Appearance.Groom(dt * 240f);   // a 10 s session removes most of the mud
        ctx.Pet.Appearance.Shake();            // licking also dries, a little
        return _remaining <= 0f;
    }

    public override void Stop(BehaviorContext ctx, bool interrupted) => ctx.Pet.SetAnimation("idle");
}
