namespace ZooZoo.Core.Behavior.Behaviors;

public sealed class IdleBehavior : Behavior
{
    private float _remaining;

    public override string Name => "idle";

    public override float Score(BehaviorContext ctx) => 0.2f;

    public override void Start(BehaviorContext ctx)
    {
        _remaining = ctx.RandomRange(1.5f, 4f);
        ctx.Pet.SetAnimation("idle");
    }

    public override bool Update(BehaviorContext ctx, float dt)
    {
        _remaining -= dt;
        return _remaining <= 0f;
    }
}
