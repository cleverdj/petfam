using PetFam.Core.Geometry;
using PetFam.Core.Pets;
using PetFam.Core.World;

namespace PetFam.Core.Behavior.Behaviors;

/// <summary>
/// Walk or run to a random spot anywhere in the roam area. Further away (higher on screen) moves slower
/// on screen. Long trips by energetic or bored pets become runs.
/// </summary>
public sealed class WanderBehavior : Behavior
{
    public const float MinTripDistance = 160f;
    public const float RunTripDistance = 420f;

    private Vec2 _target;
    private bool _running;

    public override string Name => _running ? "run" : "wander";

    public override float Score(BehaviorContext ctx)
    {
        var p = ctx.Pet;
        var funDeficit = (100f - p.Needs.Fun) / 100f;
        var snowJoy = ctx.Weather.Snowing && p.Species == PetSpecies.Dog ? 0.25f : 0f;   // dogs love snow
        return 0.25f + p.Personality.Activity * 0.3f + funDeficit * 0.15f + snowJoy;
    }

    public override void Start(BehaviorContext ctx)
    {
        var map = ctx.Cell.Map;
        var pet = ctx.Pet;

        // Prefer a destination some distance away, on the floor, reachable in a straight line over floor.
        var target = pet.Position;
        for (var tries = 0; tries < 30; tries++)
        {
            var t = map.RandomPoint(ctx.Rng);
            if (!map.CanRoam(t) || !map.LineWalkable(pet.Position, t)) continue;
            if (pet.Species == PetSpecies.Cat && map.ZoneAt(t, ZoneType.Mud) is not null) continue;
            target = t;
            if (Vec2.Distance(t, pet.Position) >= MinTripDistance) break;
        }

        _target = target;

        var funDeficit = (100f - pet.Needs.Fun) / 100f;
        var runChance = 0.15f + pet.Personality.Activity * 0.35f + funDeficit * 0.3f
                        + (ctx.Weather.Snowing && pet.Species == PetSpecies.Dog ? 0.45f : 0f);
        _running = Vec2.Distance(target, pet.Position) >= RunTripDistance
                   && pet.Needs.Energy > 35f
                   && ctx.Rng.NextDouble() < runChance;

        pet.FaceToward(_target - pet.Position);
        pet.SetAnimation(_running ? "run" : "walk");
    }

    public override bool Update(BehaviorContext ctx, float dt)
    {
        var pet = ctx.Pet;
        var map = ctx.Cell.Map;
        var speed = pet.WalkSpeed() * (0.8f + pet.Personality.Activity * 0.4f) * map.DepthScale(pet.Position.Y);
        if (_running)
        {
            speed *= pet.RunMultiplier();
            pet.Needs.Energy = System.Math.Max(0f, pet.Needs.Energy - dt * 0.8f);
            pet.Needs.Fun = System.Math.Min(100f, pet.Needs.Fun + dt * 1.5f);
        }

        var delta = _target - pet.Position;
        var dist = delta.Length;
        var step = pet.Motion.Step(pet, delta, speed, dt);

        if (dist <= System.Math.Max(step, 1f))
        {
            pet.Position = _target;
            return true;
        }

        pet.Position = map.ClampToRoam(pet.Position + delta.Normalized * step);
        pet.FaceToward(delta);
        return false;
    }

    public override void Stop(BehaviorContext ctx, bool interrupted) => ctx.Pet.SetAnimation("idle");
}
