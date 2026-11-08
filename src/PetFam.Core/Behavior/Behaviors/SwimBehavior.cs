using PetFam.Core.Geometry;
using PetFam.Core.Pets;
using PetFam.Core.World;

namespace PetFam.Core.Behavior.Behaviors;

/// <summary>
/// Dogs love water (plan §4.5.3): walk to the water, jump in, paddle around, climb out onto the floor and shake.
/// The fur ends up wet (darker, dripping) and dries over time. Cats never swim.
/// </summary>
public sealed class SwimBehavior : Behavior
{
    public const float Cooldown = 90f;

    private enum Phase { ToWater, Swim, ToShore, Shake, Done }

    private Phase _phase;
    private Vec2 _target;
    private float _timer;
    private float _swimLeft;
    private float _cooldown;

    public override string Name => "swim:" + _phase;

    public override bool Interruptible => false;

    public void TickCooldown(float dt) => _cooldown = System.Math.Max(0f, _cooldown - dt);

    public override float Score(BehaviorContext ctx)
    {
        var p = ctx.Pet;
        if (p.Species != PetSpecies.Dog || _cooldown > 0f || !ctx.Cell.Map.Has(ZoneType.Water)) return 0f;
        if (p.Needs.Energy < 30f) return 0f;
        var funDeficit = (100f - p.Needs.Fun) / 100f;
        // Water-loving breeds (a Poodle) jump in more readily than ones that hate it (a Shiba).
        return (0.15f + p.Personality.Playfulness * 0.2f + funDeficit * 0.35f) * p.Breed.WaterLove * 2f;
    }

    public override void Start(BehaviorContext ctx)
    {
        _phase = PlaceSeeker.TryPick(ctx, ZoneType.Water, 1600f, out _target, throughWater: true) ? Phase.ToWater : Phase.Done;
        _swimLeft = ctx.RandomRange(8f, 15f);
        _timer = 0f;
    }

    public override bool Update(BehaviorContext ctx, float dt)
    {
        var pet = ctx.Pet;
        var mask = ctx.Cell.Map.Mask;
        if (mask is null) return true;

        switch (_phase)
        {
            case Phase.ToWater:
                if (mask.KindAt(pet.Position) == ZoneType.Water)
                {
                    pet.Appearance.EnterWater();
                    pet.Say(L.S("Splash!", "첨벙!"), 1.2f);
                    NextSwimTarget(ctx, mask);
                    _phase = Phase.Swim;
                    return false;
                }
                PlaceSeeker.WalkTo(ctx, _target, dt, 1.6f, "run");
                return false;

            case Phase.Swim:
                _swimLeft -= dt;
                pet.Appearance.EnterWater(0f);   // stays soaked while in the water
                pet.Needs.Play(dt * 3f);
                pet.Needs.Energy = System.Math.Max(0f, pet.Needs.Energy - dt * 0.6f);
                if (PlaceSeeker.WalkTo(ctx, _target, dt, 0.55f, "swim")) NextSwimTarget(ctx, mask);
                pet.SetAnimation("swim");
                if (_swimLeft <= 0f)
                {
                    _target = mask.NearestWhere(pet.Position, WalkMask.Walkable);
                    _phase = Phase.ToShore;
                }
                return false;

            case Phase.ToShore:
            {
                var inWater = mask.KindAt(pet.Position) == ZoneType.Water;
                if (PlaceSeeker.WalkTo(ctx, _target, dt, inWater ? 0.6f : 1f, inWater ? "swim" : "walk") && !inWater)
                {
                    _phase = Phase.Shake;
                    _timer = 0f;
                    pet.SetAnimation("shake");
                }
                return false;
            }

            case Phase.Shake:
                _timer += dt;
                pet.SetAnimation("shake");
                if (_timer >= 1.6f)
                {
                    pet.Appearance.Shake();
                    return true;
                }
                return false;

            default:
                return true;
        }
    }

    public override void Stop(BehaviorContext ctx, bool interrupted)
    {
        var pet = ctx.Pet;
        _cooldown = Cooldown;
        // Never leave a pet standing in the water.
        if (ctx.Cell.Map.Mask is { } mask && mask.KindAt(pet.Position) == ZoneType.Water)
            pet.Position = mask.NearestWhere(pet.Position, WalkMask.Walkable);
        pet.SetAnimation("idle");
    }

    private void NextSwimTarget(BehaviorContext ctx, WalkMask mask)
    {
        for (var i = 0; i < 20; i++)
        {
            var p = mask.RandomWhere(ctx.Rng, k => k == ZoneType.Water);
            if (Vec2.Distance(p, ctx.Pet.Position) > 500f) continue;
            if (!mask.LineWhere(ctx.Pet.Position, p, k => k == ZoneType.Water)) continue;
            _target = p;
            return;
        }
        _target = ctx.Pet.Position;
    }
}
