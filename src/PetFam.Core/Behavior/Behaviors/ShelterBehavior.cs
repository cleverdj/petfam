using PetFam.Core.Geometry;
using PetFam.Core.Pets;
using PetFam.Core.World;

namespace PetFam.Core.Behavior.Behaviors;

/// <summary>
/// Getting out of the rain (and, for cats, the snow; for everyone, a thunderstorm). Real cats hate getting wet and
/// head for cover at the first drops; dogs mostly carry on unless it pours or thunders, and love snow.
/// A house the user built is the best cover: in through the door, wait inside until it stops, out through the door.
/// Otherwise somewhere sheltered in the wallpaper (under a tree, a porch), else curl up where it is.
/// </summary>
public sealed class ShelterBehavior : Behavior
{
    private enum Phase { Approach, Enter, Inside, Exit, Huddle }

    /// <summary>Longest stay before peeking out again (a long rain should not hide a pet all day).</summary>
    public const float MaxStay = 240f;

    private Phase _phase;
    private float _stay;
    private float _clearFor;
    private float _cooldown;
    private PetHouse? _house;
    private Vec2 _front, _inside, _spot, _housePos;

    public override string Name => _phase switch
    {
        Phase.Approach => "shelter:approach",
        Phase.Enter => "shelter:enter",
        Phase.Exit => "shelter:exit",
        Phase.Inside => "shelter:inside",
        _ => "shelter:huddle",
    };

    public override bool Interruptible => false;

    /// <summary>How much this pet wants to get out of this weather (0: does not mind).</summary>
    public static float Dislike(Pet pet, WeatherNow w)
    {
        var cat = pet.Species == PetSpecies.Cat;
        var b = pet.Breed;
        // Breeds differ: a Maltese hates getting wet even in a drizzle, a Jindo shrugs off a shower.
        var rain = b.RainDislike * (0.6f + 0.6f * w.Intensity);
        var v = w.Kind switch
        {
            WeatherKind.Rain => (cat ? 0.55f + 0.45f * w.Intensity : w.Intensity >= 0.9f ? 0.5f : 0f) + rain,
            WeatherKind.Thunder => (cat ? 1f : 0.7f) + rain * 0.5f,
            WeatherKind.Snow => cat ? 0.3f + 0.3f * w.Intensity : System.Math.Max(0f, (1f - b.SnowLove) * 0.6f),
            _ => 0f,
        };
        // Dressed for it: a raincoat keeps the rain off, a scarf or beanie keeps the snow from bothering it as much.
        if (w.Kind is WeatherKind.Rain or WeatherKind.Thunder && pet.IsWearing("raincoat")) v *= w.Kind == WeatherKind.Thunder ? 0.7f : 0.35f;
        if (w.Kind == WeatherKind.Snow && (pet.IsWearing("scarf") || pet.IsWearing("beanie") || pet.IsWearing("sweater"))) v *= 0.6f;
        return System.Math.Clamp(v, 0f, 1f);
    }

    public override float Score(BehaviorContext ctx)
    {
        if (_cooldown > 0f || ctx.Pet.Hidden) return 0f;
        var dislike = Dislike(ctx.Pet, ctx.Weather);
        if (dislike <= 0f) return 0f;
        // Under cover in the wallpaper (a tree, a porch) is good enough only when it has no house of its own to go to.
        if (!PlaceSeeker.HasHouseFor(ctx.Cell, ctx.Pet) && ctx.Cell.Map.ZoneAt(ctx.Pet.Position, ZoneType.Hide) is not null) return 0f;
        return 0.35f + dislike * 0.9f;
    }

    public void TickCooldown(float dt) => _cooldown = System.Math.Max(0f, _cooldown - dt);

    public override void Start(BehaviorContext ctx)
    {
        var pet = ctx.Pet;
        var map = ctx.Cell.Map;
        _stay = 0f;
        _clearFor = 0f;
        _house = null;

        if (PlaceSeeker.TryPickBuiltHouse(ctx, 4000f, out var house))
        {
            _house = house;
            _housePos = house.Position;
            var depth = map.DepthScale(house.Position.Y);
            _front = new Vec2(house.Position.X, house.Position.Y + SleepBehavior.FrontOfDoor * depth);
            _inside = new Vec2(house.Position.X, house.Position.Y - SleepBehavior.InsideDoor * depth);
            _spot = _front;
            _phase = Phase.Approach;
        }
        else if (PlaceSeeker.TryPickAny(ctx, new[] { ZoneType.Hide, ZoneType.House }, 1200f, out _spot))
        {
            _phase = Phase.Approach;
        }
        else
        {
            _phase = Phase.Huddle;
            pet.SetAnimation("sit");
            return;
        }
        // Hurry: a cat runs for cover.
        pet.SetAnimation("run");
        if (ctx.Visible && ctx.Rng.NextDouble() < 0.6)
        {
            if (ctx.Weather.Kind == WeatherKind.Thunder) pet.Say(pet.Species == PetSpecies.Cat ? L.S("Eek!", "히익!") : L.S("Whimper…", "낑…"), 1.8f);
            else pet.SayAs(v => ctx.Weather.Snowing ? v.Snow : v.Rain, ctx.Rng, 1.8f);
        }
    }

    public override bool Update(BehaviorContext ctx, float dt)
    {
        var pet = ctx.Pet;
        var bad = Dislike(pet, ctx.Weather) > 0f;
        _clearFor = bad ? 0f : _clearFor + dt;

        if (_house is not null && _phase is Phase.Enter or Phase.Inside or Phase.Exit &&
            (!ctx.Cell.Houses.Contains(_house) || Vec2.Distance(_house.Position, _housePos) > 2f))
        {
            OutOfHouse(pet, ctx.Cell.Map);
            return true;
        }

        switch (_phase)
        {
            case Phase.Approach:
                if (!PlaceSeeker.WalkTo(ctx, _spot, dt, speedMul: 1.6f, animation: "run")) return false;
                if (_house is not null && ctx.Cell.Houses.Contains(_house))
                {
                    _phase = Phase.Enter;
                    pet.Doorway = new Doorway { Door = _house.Position, HouseDepth = ctx.Cell.Map.DepthScale(_house.Position.Y), HouseId = _house.Id };
                    return false;
                }
                _house = null;
                _phase = Phase.Huddle;
                pet.SetAnimation("sit");
                return false;

            case Phase.Enter:
                if (!PlaceSeeker.WalkTo(ctx, _inside, dt, speedMul: 0.6f)) return false;
                pet.Doorway = null;
                pet.Hidden = true;
                pet.InsideHouse = _house!.Id;
                // Lie in the doorway looking out at the rain.
                pet.Heading = pet.TargetHeading = 90f;
                pet.SetAnimation("lie");
                _phase = Phase.Inside;
                return false;

            case Phase.Inside:
                _stay += dt;
                if (_clearFor < 6f && _stay < MaxStay) return false;
                pet.Hidden = false;
                pet.InsideHouse = null;
                pet.Doorway = new Doorway { Door = _house!.Position, HouseDepth = ctx.Cell.Map.DepthScale(_house.Position.Y), HouseId = _house.Id };
                _phase = Phase.Exit;
                return false;

            case Phase.Exit:
                if (!PlaceSeeker.WalkTo(ctx, _front, dt, speedMul: 0.6f)) return false;
                pet.Doorway = null;
                return true;

            default:
                // Under a tree / curled up: wait it out.
                _stay += dt;
                pet.SetAnimation("sit");
                return _clearFor >= 4f || _stay >= MaxStay * 0.5f;
        }
    }

    public override void Stop(BehaviorContext ctx, bool interrupted)
    {
        var pet = ctx.Pet;
        if (pet.Hidden || pet.Doorway is not null) OutOfHouse(pet, ctx.Cell.Map);
        pet.SetAnimation("idle");
        // Stayed the longest it will: it goes about its business for a while before hiding again.
        _cooldown = _stay >= MaxStay * 0.5f ? 90f : 8f;
        _house = null;
    }

    private void OutOfHouse(Pet pet, HabitatMap map)
    {
        if (_house is not null && (pet.Hidden || pet.Doorway is not null)) pet.Position = map.ClampToRoam(_front);
        pet.Hidden = false;
        pet.Doorway = null;
        pet.InsideHouse = null;
    }
}
