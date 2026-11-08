using PetFam.Core.Geometry;
using PetFam.Core.Pets;
using PetFam.Core.World;

namespace PetFam.Core.Behavior.Behaviors;

/// <summary>
/// Sleep. A house the user built comes first: the pet walks up to the door, turns its back to you and goes in through
/// the doorway, sleeps inside (the house shows who is home), and comes out through the door on waking. Otherwise it
/// goes to a favourite spot (sun for cats, shelter for dogs) or a house recognised in the wallpaper.
/// </summary>
public sealed class SleepBehavior : Behavior
{
    private enum Phase { Approach, Enter, Sleep, Exit }

    /// <summary>How far in front of the threshold the pet lines up, and how far past it it goes in (depth 1 pixels).</summary>
    public const float FrontOfDoor = 26f;
    public const float InsideDoor = 16f;

    private Phase _phase;
    private float _remaining;
    private Vec2 _spot;
    private PetHouse? _house;
    private Vec2 _front, _inside, _housePos;

    public override string Name => _phase switch
    {
        Phase.Approach => "sleep:approach",
        Phase.Enter => "sleep:enter",
        Phase.Exit => "sleep:exit",
        _ => "sleep",
    };

    public override bool Interruptible => false;

    /// <summary>True while walking to the sleeping spot (for the social director and the planner).</summary>
    public bool Approaching => _phase == Phase.Approach;

    public override float Score(BehaviorContext ctx)
    {
        var energy = ctx.Pet.Needs.Energy;
        var traits = ctx.Pet.Traits;
        if (energy > traits.SleepThreshold) return 0f;
        var nightBonus = ctx.IsDaytime ? 0f : 0.15f;
        return (100f - energy) / 100f * 0.9f + nightBonus + traits.Sleepiness;
    }

    public override void Start(BehaviorContext ctx)
    {
        // Spike timing: short naps so the cycle is observable. Real pacing comes with needs tuning.
        _remaining = ctx.RandomRange(25f, 60f);
        _house = null;
        var map = ctx.Cell.Map;

        if (PlaceSeeker.TryPickBuiltHouse(ctx, 4000f, out var house))
        {
            _house = house;
            _housePos = house.Position;
            var depth = map.DepthScale(house.Position.Y);
            _front = new Vec2(house.Position.X, house.Position.Y + FrontOfDoor * depth);
            _inside = new Vec2(house.Position.X, house.Position.Y - InsideDoor * depth);
            _spot = _front;
            _phase = Phase.Approach;
        }
        else
        {
            _phase = PlaceSeeker.TryPickHouse(ctx, 1400f, out _spot)
                     || PlaceSeeker.TryPickAny(ctx, PlaceSeeker.RestPreference(ctx.Pet.Species), 900f, out _spot)
                ? Phase.Approach
                : Phase.Sleep;
        }
        ctx.Pet.SetAnimation(_phase == Phase.Approach ? "walk" : "sleep");
    }

    public override bool Update(BehaviorContext ctx, float dt)
    {
        var pet = ctx.Pet;

        // The house was torn down (or moved) while we were going in or sleeping in it: out on the floor.
        if (_house is not null && _phase != Phase.Approach && (!ctx.Cell.Houses.Contains(_house) || Moved(_house)))
        {
            LeaveHouse(pet, ctx.Cell.Map);
            if (_phase != Phase.Sleep) { _phase = Phase.Sleep; pet.SetAnimation("sleep"); }
        }

        switch (_phase)
        {
            case Phase.Approach:
                if (!PlaceSeeker.WalkTo(ctx, _spot, dt)) return false;
                if (_house is not null && ctx.Cell.Houses.Contains(_house) && !Moved(_house))
                {
                    // Line up at the door, back to the viewer, and walk in.
                    _phase = Phase.Enter;
                    pet.Doorway = new Doorway { Door = _house.Position, HouseDepth = ctx.Cell.Map.DepthScale(_house.Position.Y), HouseId = _house.Id };
                    return false;
                }
                _house = null;
                _phase = Phase.Sleep;
                pet.SetAnimation("sleep");
                if (PlaceSeeker.AtHouse(ctx)) pet.Hidden = true;
                return false;

            case Phase.Enter:
                if (!PlaceSeeker.WalkTo(ctx, _inside, dt, speedMul: 0.55f)) return false;
                pet.Doorway = null;
                pet.Hidden = true;
                pet.InsideHouse = _house!.Id;
                // Turn round inside and lie down in the doorway, face toward you, eyes closed.
                pet.Heading = pet.TargetHeading = 90f;
                pet.SetAnimation("doze");
                _phase = Phase.Sleep;
                return false;

            case Phase.Sleep:
                _remaining -= dt;
                // Fast regen while napping so the spike shows wake-up; Needs.Tick also regenerates slowly.
                pet.Needs.Energy = System.Math.Min(100f, pet.Needs.Energy + dt * 1.2f * pet.Traits.EnergyRegen);
                if (_remaining > 0f && pet.Needs.Energy < 100f) return false;
                if (_house is null || !pet.Hidden) return true;
                // Wake up and come out through the door.
                pet.Hidden = false;
                pet.InsideHouse = null;
                pet.Doorway = new Doorway { Door = _house.Position, HouseDepth = ctx.Cell.Map.DepthScale(_house.Position.Y), HouseId = _house.Id };
                _phase = Phase.Exit;
                return false;

            default:
                if (!PlaceSeeker.WalkTo(ctx, _front, dt, speedMul: 0.55f)) return false;
                pet.Doorway = null;
                return true;
        }
    }

    public override void Stop(BehaviorContext ctx, bool interrupted)
    {
        var pet = ctx.Pet;
        if (pet.Hidden || pet.Doorway is not null) LeaveHouse(pet, ctx.Cell.Map);
        pet.Hidden = false;   // comes out of the house
        pet.JustWokeUp = !interrupted && _phase is Phase.Sleep or Phase.Exit;
        pet.SetAnimation("idle");
        _house = null;
    }

    /// <summary>Out of the house at once (woken by the user, or the house is gone): in front of the door.</summary>
    private void LeaveHouse(Pet pet, HabitatMap map)
    {
        if (_house is not null && (pet.Hidden || pet.Doorway is not null)) pet.Position = map.ClampToRoam(_front);
        pet.Hidden = false;
        pet.Doorway = null;
        pet.InsideHouse = null;
    }

    private bool Moved(PetHouse h) => Vec2.Distance(h.Position, _housePos) > 2f;
}
