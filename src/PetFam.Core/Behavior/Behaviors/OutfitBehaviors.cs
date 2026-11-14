using PetFam.Core.Geometry;
using PetFam.Core.Pets;
using PetFam.Core.World;

namespace PetFam.Core.Behavior.Behaviors;

/// <summary>
/// Shaking it off: a pet that doesn't like dressing up (Shiba, Husky…) now and then shakes its head until its hat or
/// glasses fall off onto the floor. "Free at last!" The piece waits there to be put back (pet panel), or the pet is
/// talked into it again after a while.
/// </summary>
public sealed class ShakeOffBehavior : CooldownBehavior
{
    private float _t;
    private bool _dropped;

    public ShakeOffBehavior() => Cooldown = 180f + (float)Random.Shared.NextDouble() * 300f;

    public override string Name => "shake-off";

    public override bool Interruptible => false;

    public static OutfitSlot? Droppable(Pet p)
    {
        foreach (var slot in new[] { OutfitSlot.Head, OutfitSlot.Face })
            if (p.Outfit.Get(slot) is not null && !p.Doffed.Contains(slot) && p.Borrowed?.ContainsKey(slot) != true) return slot;
        return null;
    }

    public override float Score(BehaviorContext ctx)
    {
        var p = ctx.Pet;
        if (Cooldown > 0f || p.Hidden || !ctx.Visible || Droppable(p) is null) return 0f;
        var love = OutfitCatalog.FashionLove(p);
        if (love > 0.35f) return 0f;
        return 0.25f + (0.35f - love) * 1.2f;
    }

    public override void Start(BehaviorContext ctx)
    {
        _t = 0f;
        _dropped = false;
        ctx.Pet.SetAnimation("shake");
    }

    public override bool Update(BehaviorContext ctx, float dt)
    {
        var pet = ctx.Pet;
        _t += dt;
        if (!_dropped && _t >= 1.0f && Droppable(pet) is { } slot)
        {
            _dropped = true;
            var d = ctx.Cell.Map.DepthScale(pet.Position.Y);
            var side = ctx.Rng.NextDouble() < 0.5 ? -1 : 1;
            var at = ctx.Cell.Map.ClampToRoam(pet.Position + new Vec2(side * Rand(ctx, 50f, 90f) * d, Rand(ctx, 4f, 18f) * d));
            ctx.Cell.Dropped.Add(new DroppedWear { OwnerId = pet.Id, Slot = slot, Piece = pet.Outfit.Get(slot)!, Position = at });
            pet.Doffed.Add(slot);
            pet.Say(L.S("Free at last!", "벗었다! 자유다!"), 2f);
        }
        return _t >= 1.8f;
    }

    public override void Stop(BehaviorContext ctx, bool interrupted)
    {
        ctx.Pet.SetAnimation("idle");
        Cooldown = Rand(ctx, 900f, 1800f);
    }
}

/// <summary>
/// Finders keepers (playful dogs): a hat or glasses lying on the floor gets picked up and worn, and off it goes on a
/// victory lap, before dropping it again somewhere else.
/// </summary>
public sealed class StealWearBehavior : CooldownBehavior
{
    private enum Phase { Go, Lap, Done }

    private Phase _phase;
    private DroppedWear? _item;
    private Vec2 _target;
    private int _laps;

    public StealWearBehavior() => Cooldown = 60f;

    public override string Name => "steal-wear";

    public override bool Interruptible => false;

    private static DroppedWear? Pick(BehaviorContext ctx) =>
        ctx.Cell.Dropped
            .Where(w => w.CarriedBy is null && w.OwnerId != ctx.Pet.Id && ctx.Pet.Wears(w.Slot) is null)
            .Where(w => Vec2.Distance(w.Position, ctx.Pet.Position) < 700f)
            .OrderBy(w => Vec2.Distance(w.Position, ctx.Pet.Position))
            .FirstOrDefault();

    public override float Score(BehaviorContext ctx)
    {
        var p = ctx.Pet;
        if (p.Species != PetSpecies.Dog || Cooldown > 0f || p.Hidden || !ctx.Visible || p.Personality.Playfulness < 0.55f) return 0f;
        if (p.Needs.Energy < 30f || Pick(ctx) is null) return 0f;
        return 0.5f + p.Personality.Playfulness * 0.4f;
    }

    public override void Start(BehaviorContext ctx)
    {
        _item = Pick(ctx);
        _phase = Phase.Go;
        _laps = 0;
        if (_item is not null) _target = _item.Position;
    }

    public override bool Update(BehaviorContext ctx, float dt)
    {
        var pet = ctx.Pet;
        switch (_phase)
        {
            case Phase.Go:
                if (_item is null || !ctx.Cell.Dropped.Contains(_item) || _item.CarriedBy is not null) return true;
                if (!PlaceSeeker.WalkTo(ctx, _target, dt, speedMul: 1.2f, animation: "run")) return false;
                // Got it: on its own head it goes.
                _item.CarriedBy = pet.Id;
                ctx.Cell.Dropped.Remove(_item);
                (pet.Borrowed ??= new())[_item.Slot] = _item.Piece;
                pet.Say(L.S("Mine now! Woof!", "이제 내 거야! 왕!"), 1.8f);
                _phase = Phase.Lap;
                _target = ctx.Cell.Map.RandomPoint(ctx.Rng);
                return false;
            case Phase.Lap:
                if (!PlaceSeeker.WalkTo(ctx, _target, dt, speedMul: 1.6f, animation: "run")) return false;
                if (++_laps < 3)
                {
                    _target = ctx.Cell.Map.RandomPoint(ctx.Rng);
                    return false;
                }
                _phase = Phase.Done;
                return true;
            default:
                return true;
        }
    }

    public override void Stop(BehaviorContext ctx, bool interrupted)
    {
        var pet = ctx.Pet;
        // Drops it where it ended up (also when interrupted, so nothing is lost).
        if (_item is not null && pet.Borrowed?.Remove(_item.Slot) == true)
        {
            _item.CarriedBy = null;
            _item.Position = ctx.Cell.Map.ClampToRoam(pet.Position + new Vec2(30f, 6f));
            ctx.Cell.Dropped.Add(_item);
            if (!interrupted) pet.Say(L.S("Hehe, here you go", "헤헤, 돌려줄게"), 1.6f);
        }
        if (pet.Borrowed is { Count: 0 }) pet.Borrowed = null;
        _item = null;
        pet.SetAnimation("idle");
        Cooldown = Rand(ctx, 300f, 600f);
    }
}

/// <summary>
/// Matching outfits: two pets in the same thing come side by side, face you and show it off ("We match!" / "Twins!").
/// The one who starts it walks over; the other one, invited, waits and poses.
/// </summary>
public sealed class CoupleLookBehavior : CooldownBehavior
{
    private float _t;
    private Pet? _partner;
    private Vec2 _spot;
    private bool _arrived, _invited;

    public CoupleLookBehavior() => Cooldown = 120f + (float)Random.Shared.NextDouble() * 240f;

    public override string Name => "couple-look";

    public override bool Interruptible => false;

    private static Pet? Partner(BehaviorContext ctx) =>
        ctx.Others.Where(o => o.CellId == ctx.Pet.CellId && !o.Hidden && o.Trip is null && o.Session is null && o.Hold == HoldState.None
                              && o.PoseWith is null && OutfitCatalog.Matching(ctx.Pet, o) > 0)
            .OrderBy(o => Vec2.Distance(o.Position, ctx.Pet.Position)).FirstOrDefault();

    public override float Score(BehaviorContext ctx)
    {
        var p = ctx.Pet;
        if (p.Hidden || !ctx.Visible) return 0f;
        // Invited by a partner: come and pose right away.
        if (p.PoseWith is not null) return 1.4f;
        if (Cooldown > 0f || Partner(ctx) is null) return 0f;
        return 0.35f + 0.1f * OutfitCatalog.Matching(p, Partner(ctx)!);
    }

    public override void Start(BehaviorContext ctx)
    {
        var pet = ctx.Pet;
        _t = 0f;
        _arrived = false;
        _invited = pet.PoseWith is not null;
        if (_invited)
        {
            _partner = ctx.Others.FirstOrDefault(o => o.Id == pet.PoseWith);
            pet.Heading = pet.TargetHeading = 90f;
            pet.SetAnimation("sit");
            return;
        }
        _partner = Partner(ctx);
        if (_partner is null) return;
        _partner.PoseWith = pet.Id;
        var d = ctx.Cell.Map.DepthScale(_partner.Position.Y);
        var side = pet.Position.X < _partner.Position.X ? -1 : 1;
        _spot = ctx.Cell.Map.ClampToRoam(_partner.Position + new Vec2(side * 75f * d, 2f * d));
    }

    public override bool Update(BehaviorContext ctx, float dt)
    {
        var pet = ctx.Pet;
        if (_partner is null) return true;
        _t += dt;
        if (_invited)
        {
            // Waits for the partner, then answers.
            if (_t > 1.6f && pet.Bubble is null && _t < 2.0f)
                pet.Say(L.S("Twins!", "찰떡이지!"), 1.8f);
            return _t >= 6f || _partner.PoseWith is null && _t > 2.5f;
        }
        if (!_arrived)
        {
            if (_t > 12f) return true;                                     // couldn't get there
            if (!PlaceSeeker.WalkTo(ctx, _spot, dt)) return false;
            _arrived = true;
            _t = 0f;
            pet.Heading = pet.TargetHeading = 90f;
            pet.SetAnimation("sit");
            pet.Say(L.S("We match! Cute, right?", "커플룩 어때? 귀엽지?"), 2f);
            ctx.Social?.AddFriendship(pet, _partner, 2f);
            return false;
        }
        return _t >= 4f;
    }

    public override void Stop(BehaviorContext ctx, bool interrupted)
    {
        var pet = ctx.Pet;
        if (_invited) pet.PoseWith = null;
        else if (_partner is not null && _partner.PoseWith == pet.Id) _partner.PoseWith = null;
        _partner = null;
        pet.SetAnimation("idle");
        Cooldown = Rand(ctx, 600f, 1200f);
    }
}
