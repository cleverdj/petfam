namespace PetFam.Core.Behavior.Behaviors;

/// <summary>Big stretch right after waking up.</summary>
public sealed class StretchBehavior : Behavior
{
    public const float Duration = 2.4f;

    private float _remaining;

    public override string Name => "stretch";

    public override bool Interruptible => false;

    public override float Score(BehaviorContext ctx) => ctx.Pet.JustWokeUp ? 5f : 0f;

    public override void Start(BehaviorContext ctx)
    {
        _remaining = Duration;
        ctx.Pet.SetAnimation("stretch");
    }

    public override bool Update(BehaviorContext ctx, float dt)
    {
        _remaining -= dt;
        return _remaining <= 0f;
    }

    public override void Stop(BehaviorContext ctx, bool interrupted)
    {
        ctx.Pet.JustWokeUp = false;
        ctx.Pet.SetAnimation("idle");
    }
}
