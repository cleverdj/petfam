using PetFam.Core.Behavior.Behaviors;
using PetFam.Core.Pets;
using PetFam.Core.World;

namespace PetFam.Core.Interactions;

/// <summary>Every interaction the pie menu can offer, in menu order.</summary>
public static class InteractionCatalog
{
    public static IReadOnlyList<Interaction> All { get; } = new Interaction[]
    {
        new PetInteraction(),
        new ChinScratchInteraction(),
        new HugInteraction(),
        new PlayBallInteraction(),
        new TreatInteraction(),
        new FeedInteraction(),
        new TowelInteraction(),
        new SitCommandInteraction(),
        new RestInteraction(),
    };

    public static Interaction? Find(string id) => All.FirstOrDefault(i => i.Id == id);
}

internal sealed class PetInteraction : Interaction
{
    public override string Id => "pet";
    public override string Label => L.S("Pet", "쓰다듬기");
    public override string Icon => "🤚";
    public override float RefusalBias => -0.1f;

    public override InteractionRunner CreateRunner() => new PoseRunner("petted", 3f,
        tick: (p, dt) => p.Needs.ReceiveAffection(dt * 8f),
        complete: (p, now) => p.Bond.Pet(now));
}

internal sealed class ChinScratchInteraction : Interaction
{
    public override string Id => "chin";
    public override string Label => L.S("Chin scratch", "턱 긁어주기");
    public override string Icon => "😽";
    public override BondStage MinBond => BondStage.Wary;

    public override InteractionRunner CreateRunner() => new PoseRunner("petted", 3.5f,
        tick: (p, dt) => p.Needs.ReceiveAffection(dt * 10f),
        complete: (p, now) => p.Bond.Pet(now),
        say: L.S("Purr~", "골골~"));
}

internal sealed class HugInteraction : Interaction
{
    public override string Id => "hug";
    public override string Label => L.S("Hug", "안아주기");
    public override string Icon => "🤗";
    public override BondStage MinBond => BondStage.Curious;
    public override float RefusalBias => 0.15f;

    public override InteractionRunner CreateRunner() => new PoseRunner("carried", 3f,
        tick: (p, dt) => p.Needs.ReceiveAffection(dt * 12f),
        complete: (p, now) => p.Bond.Play(now));
}

internal sealed class PlayBallInteraction : Interaction
{
    public override string Id => "ball";
    public override string Label => L.S("Play ball", "공놀이");
    public override string Icon => "⚽";
    public override BondStage MinBond => BondStage.Wary;

    protected override Availability CheckOwn(Pet pet, Cell cell, DateTime nowUtc) =>
        pet.Needs.Energy < 25f ? Availability.Disabled(L.S("Too tired", "너무 피곤해요")) : Availability.Ok;

    public override InteractionRunner CreateRunner() => new FetchRunner();
}

internal sealed class TreatInteraction : Interaction
{
    public static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(5);

    public override string Id => "treat";
    public override string Label => L.S("Give a treat", "간식 주기");
    public override string Icon => "🍪";
    public override bool CanRefuse => false;

    protected override Availability CheckOwn(Pet pet, Cell cell, DateTime nowUtc)
    {
        if (pet.Needs.Hunger > 95f) return Availability.Disabled(L.S("Full", "배불러요"));
        if (nowUtc - pet.LastTreatUtc < Cooldown) return Availability.Disabled(L.S("Just ate", "방금 먹었어요"));
        return Availability.Ok;
    }

    public override InteractionRunner CreateRunner() => new PoseRunner("eat", 2.5f,
        complete: (p, now) =>
        {
            p.Needs.Hunger = System.Math.Min(100f, p.Needs.Hunger + 15f);
            p.Needs.Play(10f);
            p.Bond.ReactToPeek(now);
            p.LastTreatUtc = now;
        },
        say: L.S("Yum!", "냠!"));
}

internal sealed class FeedInteraction : Interaction
{
    public override string Id => "feed";
    public override string Label => L.S("Feed", "밥 주기");
    public override string Icon => "🍚";
    public override bool CanRefuse => false;

    protected override Availability CheckOwn(Pet pet, Cell cell, DateTime nowUtc) =>
        pet.Needs.Hunger > EatBehavior.HungerThreshold ? Availability.Disabled(L.S("Full", "배불러요")) : Availability.Ok;

    public override InteractionRunner CreateRunner() => new BehaviorRunner(new EatBehavior(), 12f, before: ctx =>
    {
        var pet = ctx.Pet;
        var dir = MathF.Cos(pet.Heading * MathF.PI / 180f) >= 0 ? 1f : -1f;
        ctx.Cell.Bowls.Add(new FoodBowl { Position = ctx.Cell.Map.ClampToRoam(pet.Position + new Geometry.Vec2(dir * 140f, 0f)) });
    });
}

internal sealed class TowelInteraction : Interaction
{
    public override string Id => "towel";
    public override string Label => L.S("Towel dry", "수건으로 닦기");
    public override string Icon => "🧺";
    public override float RefusalBias => 0.05f;

    protected override Availability CheckOwn(Pet pet, Cell cell, DateTime nowUtc) =>
        pet.Appearance.IsClean ? Availability.Hidden : Availability.Ok;

    public override InteractionRunner CreateRunner() => new PoseRunner("petted", 2.5f,
        complete: (p, now) =>
        {
            p.Appearance.Towel();
            p.Bond.Wash(now);
        });
}

internal sealed class SitCommandInteraction : Interaction
{
    public override string Id => "sit";
    public override string Label => L.S("Sit!", "앉아!");
    public override string Icon => "🐾";
    public override BondStage MinBond => BondStage.Curious;

    protected override Availability CheckOwn(Pet pet, Cell cell, DateTime nowUtc) =>
        pet.Species == PetSpecies.Dog ? Availability.Ok : Availability.Hidden;

    public override InteractionRunner CreateRunner() => new PoseRunner("sit", 3f,
        complete: (p, now) =>
        {
            p.Needs.Play(5f);
            p.Bond.ReactToPeek(now);
        },
        say: L.S("Woof!", "왈!"));
}

internal sealed class RestInteraction : Interaction
{
    public override string Id => "rest";
    public override string Label => L.S("Rest", "쉬게 하기");
    public override string Icon => "💤";
    public override bool CanRefuse => false;

    protected override Availability CheckOwn(Pet pet, Cell cell, DateTime nowUtc) =>
        pet.Needs.Energy < 70f ? Availability.Ok : Availability.Hidden;

    public override InteractionRunner CreateRunner() => new BehaviorRunner(new SleepBehavior(), 40f);
}
