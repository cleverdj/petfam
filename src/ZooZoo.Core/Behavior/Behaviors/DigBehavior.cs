using ZooZoo.Core.Geometry;
using ZooZoo.Core.Pets;
using ZooZoo.Core.World;

namespace ZooZoo.Core.Behavior.Behaviors;

/// <summary>
/// Dogs dig. When the wallpaper has a mud patch they run to it first and come back covered (plan §4.5.3);
/// on dirt floors digging also gets them a little muddy.
/// </summary>
public sealed class DigBehavior : Behavior
{
    private float _remaining;
    private bool _approaching;
    private Vec2 _spot;

    public override string Name => _approaching ? "dig:approach" : "dig";

    public override float Score(BehaviorContext ctx)
    {
        var p = ctx.Pet;
        if (p.Species != PetSpecies.Dog) return 0f;
        var funDeficit = (100f - p.Needs.Fun) / 100f;
        var mudBonus = ctx.Cell.Map.Has(ZoneType.Mud) && p.Appearance.Mud < 0.5f ? 0.15f : 0f;
        return 0.05f + p.Personality.Playfulness * 0.2f + funDeficit * 0.3f + mudBonus;
    }

    public override void Start(BehaviorContext ctx)
    {
        _remaining = ctx.RandomRange(3f, 6f);
        _approaching = ctx.Pet.Appearance.Mud < 0.5f && PlaceSeeker.TryPick(ctx, ZoneType.Mud, 1400f, out _spot);
        ctx.Pet.SetAnimation(_approaching ? "run" : "dig");
    }

    public override bool Update(BehaviorContext ctx, float dt)
    {
        var pet = ctx.Pet;
        if (_approaching)
        {
            if (!PlaceSeeker.WalkTo(ctx, _spot, dt, 1.8f, "run")) return false;
            _approaching = false;
            pet.SetAnimation("dig");
        }

        _remaining -= dt;
        pet.Needs.Play(dt * 4f);
        var onMud = ctx.Cell.Map.ZoneAt(pet.Position, ZoneType.Mud) is not null;
        if (onMud) pet.Appearance.AddMud(dt * 0.18f);
        else if (ctx.Cell.Map.Surface == SurfaceKind.Dirt) pet.Appearance.AddMud(dt * 0.08f);
        return _remaining <= 0f;
    }

    public override void Stop(BehaviorContext ctx, bool interrupted) => ctx.Pet.SetAnimation("idle");
}
