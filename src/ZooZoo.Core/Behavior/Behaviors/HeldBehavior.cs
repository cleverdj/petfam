using ZooZoo.Core.Pets;

namespace ZooZoo.Core.Behavior.Behaviors;

/// <summary>
/// Runs while the user touches the pet: carried (dangling), petted (mouse held down), or enjoying a single
/// pat for a moment after a click. The pet stops whatever it was doing, including walking, until it ends.
/// </summary>
public sealed class HeldBehavior : Behavior
{
    public override string Name => "held";

    public override bool Interruptible => false;

    public static bool IsTouched(Pet pet) => pet.Hold != HoldState.None || pet.PatTimer > 0f;

    public override float Score(BehaviorContext ctx) => IsTouched(ctx.Pet) ? 10f : 0f;

    public override void Start(BehaviorContext ctx) => Apply(ctx.Pet);

    public override bool Update(BehaviorContext ctx, float dt)
    {
        var pet = ctx.Pet;
        pet.PatTimer = System.Math.Max(0f, pet.PatTimer - dt);
        if (!IsTouched(pet)) return true;
        Apply(pet);
        return false;
    }

    public override void Stop(BehaviorContext ctx, bool interrupted)
    {
        ctx.Pet.PatTimer = 0f;
        ctx.Pet.SetAnimation("idle");
    }

    private static void Apply(Pet pet)
    {
        switch (pet.Hold)
        {
            case HoldState.Carried:
                pet.SetAnimation("carried");
                break;
            case HoldState.Attention:
                pet.TargetHeading = 90f;   // turn to the person
                pet.SetAnimation("idle");
                break;
            default:
                pet.SetAnimation("petted");
                break;
        }
    }
}
