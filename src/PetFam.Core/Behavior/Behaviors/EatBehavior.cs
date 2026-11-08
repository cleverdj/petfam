using PetFam.Core.Geometry;
using PetFam.Core.Pets;
using PetFam.Core.World;

namespace PetFam.Core.Behavior.Behaviors;

/// <summary>
/// Walk to the nearest food bowl and eat. Hunger refills while eating, the bowl empties, and the bond
/// credit for feeding lands when the meal actually happens (plan §4.3 feeding +2).
/// </summary>
public sealed class EatBehavior : Behavior
{
    public const float HungerThreshold = 85f;
    public const float MealSeconds = 8f;

    private enum Phase { Approach, Eat }

    private FoodBowl? _bowl;
    private bool _waiting;
    private Vec2 _waitSpot;

    public FoodBowl? TargetBowl => _bowl;
    private Phase _phase;
    private Vec2 _standAt;
    private float _eatTimer;

    public override string Name => _waiting ? "wait-for-food" : _phase == Phase.Eat ? "eat" : "approach-food";

    public override bool Interruptible => false;

    public override float Score(BehaviorContext ctx)
    {
        var pet = ctx.Pet;
        if (pet.Needs.Hunger > HungerThreshold) return 0f;
        if (ctx.Cell.NearestBowl(pet.Position) is null) return 0f;
        return 0.45f + (100f - pet.Needs.Hunger) / 100f * 0.6f;
    }

    public override void Start(BehaviorContext ctx)
    {
        var pet = ctx.Pet;
        _bowl = ctx.Cell.NearestBowl(pet.Position);
        _phase = Phase.Approach;
        _eatTimer = 0f;
        if (_bowl is null) return;

        // Stand beside the bowl, facing it.
        var side = _bowl.Position.X >= pet.Position.X ? 1f : -1f;
        var reach = pet.Species.EatReach() * ctx.Cell.Map.DepthScale(_bowl.Position.Y);
        _standAt = ctx.Cell.Map.ClampToRoam(new Vec2(_bowl.Position.X - side * reach, _bowl.Position.Y));
        pet.FaceToward(_standAt - pet.Position);
        pet.SetAnimation("walk");
    }

    public override bool Update(BehaviorContext ctx, float dt)
    {
        var pet = ctx.Pet;
        if (_bowl is null || _bowl.Amount <= 0f) return true;

        // Pecking order: a lower-ranked pet waits beside the bowl while a higher-ranked one eats from it.
        if (OutrankedAtBowl(ctx, pet))
        {
            if (!_waiting)
            {
                // Pick a waiting spot once: beside the bowl, on this pet's side.
                var side = pet.Position.X >= _bowl.Position.X ? 1f : -1f;
                _waitSpot = ctx.Cell.Map.ClampToRoam(_bowl.Position + new Vec2(side * 130f, 25f));
                pet.Say("…", 1.5f);
                _waiting = true;
            }
            if (!PlaceSeeker.WalkTo(ctx, _waitSpot, dt)) return false;
            pet.FaceToward(_bowl.Position - pet.Position);
            pet.SetAnimation("sit");
            return false;
        }
        if (_waiting)
        {
            _waiting = false;
            _phase = Phase.Approach;
        }

        if (_phase == Phase.Approach)
        {
            var speed = pet.WalkSpeed() * 1.3f * ctx.Cell.Map.DepthScale(pet.Position.Y);
            var delta = _standAt - pet.Position;
            var step = pet.Motion.Step(pet, delta, speed, dt);
            if (delta.Length <= System.Math.Max(step, 1f))
            {
                pet.Position = _standAt;
                pet.FaceToward(_bowl.Position - pet.Position);
                _phase = Phase.Eat;
                pet.SetAnimation("eat");
                return false;
            }
            pet.Position = pet.Position + delta.Normalized * step;
            pet.FaceToward(delta);
            return false;
        }

        _eatTimer += dt;
        var rate = 100f / MealSeconds;
        pet.Needs.Hunger = System.Math.Min(100f, pet.Needs.Hunger + rate * dt);
        _bowl.Amount = System.Math.Max(0f, _bowl.Amount - dt / MealSeconds);
        return pet.Needs.Hunger >= 100f || _bowl.Amount <= 0f || _eatTimer >= MealSeconds * 1.5f;
    }

    private bool OutrankedAtBowl(BehaviorContext ctx, Pet pet)
    {
        if (ctx.Social is null) return false;
        foreach (var other in ctx.Pets)
        {
            if (ReferenceEquals(other, pet) || other.Engine.Current is not EatBehavior eat || eat._waiting) continue;
            if (!ReferenceEquals(eat._bowl, _bowl)) continue;
            if (ctx.Social.DominanceOf(other, pet) > 0.05f) return true;
        }
        return false;
    }

    public override void Stop(BehaviorContext ctx, bool interrupted)
    {
        var pet = ctx.Pet;
        _waiting = false;
        if (_phase == Phase.Eat && _eatTimer > 1f)
            pet.Bond.Feed(ctx.NowUtc);

        if (_bowl is not null && _bowl.Amount <= 0.01f)
            ctx.Cell.Bowls.Remove(_bowl);

        _bowl = null;
        pet.SetAnimation("idle");
    }
}
