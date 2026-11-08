using PetFam.Core.Geometry;
using PetFam.Core.Pets;
using PetFam.Core.World;

namespace PetFam.Core.Behavior;

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

    /// <summary>
    /// A house to sleep in: the doorstep of a house the user built (nearest first), or a house recognised in the
    /// wallpaper.
    /// </summary>
    public static bool TryPickHouse(BehaviorContext ctx, float maxDistance, out Vec2 target)
    {
        var map = ctx.Cell.Map;
        var from = ctx.Pet.Position;
        foreach (var house in ctx.Cell.Houses.OrderBy(h => Vec2.Distance(h.Position, from)))
        {
            var door = map.ClampToRoam(house.Doorstep(map.DepthScale(house.Position.Y)));
            if (Vec2.Distance(door, from) > maxDistance) continue;
            if (map.Mask is { } mask && !mask.LineWhere(from, door, WalkMask.Walkable)) continue;
            target = door;
            return true;
        }
        return TryPick(ctx, ZoneType.House, maxDistance, out target);
    }

    /// <summary>
    /// A house the user built that this pet may go into: its own house first, else the nearest one that belongs to
    /// everybody. Other pets' houses are theirs. (Houses stand on the floor, so the door is always walkable to.)
    /// </summary>
    public static bool TryPickBuiltHouse(BehaviorContext ctx, float maxDistance, out PetHouse house)
    {
        var map = ctx.Cell.Map;
        var pet = ctx.Pet;
        var from = pet.Position;
        foreach (var h in ctx.Cell.Houses.Where(h => h.Welcomes(pet))
                     .OrderBy(h => h.OwnerId == pet.Id ? 0 : 1).ThenBy(h => Vec2.Distance(h.Position, from)))
        {
            var door = h.Doorstep(map.DepthScale(h.Position.Y));
            if (Vec2.Distance(door, from) > maxDistance) continue;
            house = h;
            return true;
        }
        house = null!;
        return false;
    }

    /// <summary>Is there a house here this pet may go into?</summary>
    public static bool HasHouseFor(Cell cell, Pet pet) => cell.Houses.Any(h => h.Welcomes(pet));

    /// <summary>Is the pet at the door of a house (built or recognised)?</summary>
    public static bool AtHouse(BehaviorContext ctx)
    {
        var map = ctx.Cell.Map;
        var p = ctx.Pet.Position;
        if (ctx.Cell.Houses.Any(h => Vec2.Distance(p, map.ClampToRoam(h.Doorstep(map.DepthScale(h.Position.Y)))) <= 12f * map.DepthScale(p.Y)))
            return true;
        return map.ZoneAt(p, ZoneType.House) is not null;
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
        var speed = pet.WalkSpeed() * ctx.Cell.Map.DepthScale(pet.Position.Y) * speedMul;
        var delta = target - pet.Position;
        var step = pet.Motion.Step(pet, delta, speed, dt);
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
