using ZooZoo.Core.Geometry;
using ZooZoo.Core.Pets;
using ZooZoo.Core.World;

namespace ZooZoo.Core.Behavior;

/// <summary>Picks a spot of a given place kind and walks the pet there (shared by rest, dig and swim).</summary>
public static class PlaceSeeker
{
    /// <summary>Favourite resting places: cats love sunny spots, dogs like lying in the shelter of something big.</summary>
    public static ZoneType[] RestPreference(PetSpecies species) => species == PetSpecies.Cat
        ? new[] { ZoneType.Sun, ZoneType.Hide }
        : new[] { ZoneType.Hide, ZoneType.Sun };

    /// <summary>
    /// A reachable spot of <paramref name="kind"/> within <paramref name="maxDistance"/>, reachable in a straight line
    /// over floor (and over water when <paramref name="throughWater"/>).
    /// </summary>
    public static bool TryPick(BehaviorContext ctx, ZoneType kind, float maxDistance, out Vec2 target, bool throughWater = false)
    {
        target = default;
        var map = ctx.Cell.Map;
        if (map.Mask is not { } mask || mask.CountOf(kind) == 0) return false;

        var from = ctx.Pet.Position;
        for (var i = 0; i < 30; i++)
        {
            var p = mask.RandomWhere(ctx.Rng, k => k == kind);
            if (Vec2.Distance(p, from) > maxDistance) continue;
            if (!mask.LineWhere(from, p, k => WalkMask.Walkable(k) || (throughWater && k == ZoneType.Water))) continue;
            target = p;
            return true;
        }
        return false;
    }

    public static bool TryPickAny(BehaviorContext ctx, IEnumerable<ZoneType> kinds, float maxDistance, out Vec2 target)
    {
        foreach (var kind in kinds)
            if (TryPick(ctx, kind, maxDistance, out target)) return true;
        target = default;
        return false;
    }

    /// <summary>Moves toward target; true on arrival.</summary>
    public static bool WalkTo(BehaviorContext ctx, Vec2 target, float dt, float speedMul = 1f, string animation = "walk")
    {
        var pet = ctx.Pet;
        var speed = pet.Species.WalkSpeed() * ctx.Cell.Map.DepthScale(pet.Position.Y) * speedMul;
        var delta = target - pet.Position;
        var step = speed * dt;
        if (delta.Length <= System.Math.Max(step, 2f))
        {
            pet.Position = target;
            return true;
        }
        pet.Position = pet.Position + delta.Normalized * step;
        pet.FaceToward(delta);
        pet.SetAnimation(animation);
        return false;
    }
}
