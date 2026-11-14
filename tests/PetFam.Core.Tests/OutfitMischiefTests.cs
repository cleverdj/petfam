using PetFam.Core.Behavior;
using PetFam.Core.Behavior.Behaviors;
using PetFam.Core.Geometry;
using PetFam.Core.Pets;
using PetFam.Core.Platform;
using PetFam.Core.World;

namespace PetFam.Core.Tests;

public class OutfitMischiefTests
{
    private static readonly DateTime T0 = new(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

    private sealed class FakePlatform : IPlatform
    {
        public void RegisterOwnWindow(IntPtr handle) { }
        public ForegroundWindowInfo? GetForegroundWindow() => null;
        public Vec2 GetCursorPosition() => new(-5000, -5000);
        public TimeSpan GetIdleTime() => TimeSpan.Zero;
        public bool IsDoNotDisturb() => false;
        public string? GetWallpaperPath() => null;
        public WallpaperFit GetWallpaperFit() => WallpaperFit.Fill;
        public bool IsDesktopAt(Vec2 screenPoint) => true;
    }

    private static (Simulation Sim, Cell Cell) World()
    {
        var sim = new Simulation(new FakePlatform(), new Random(4));
        var b = new RectI(0, 0, 1920, 1080);
        var w = new RectI(0, 0, 1920, 1040);
        var cell = sim.World.Upsert(new Cell { Id = "d:m", DesktopId = "d", MonitorId = "m", Bounds = b, WorkingArea = w, Map = HabitatMap.Flat(b, w) });
        return (sim, cell);
    }

    private static Pet Add(Simulation sim, Cell cell, Breed breed, Vec2 at)
    {
        var p = sim.AddPet(breed, breed.Name.En, cell, T0);
        p.Growth = new Growth(T0, Growth.AdultAt);
        p.Position = at;
        return p;
    }

    private static BehaviorContext Ctx(Simulation sim, Cell cell, Pet pet) => new()
    {
        Pet = pet, Cell = cell, User = sim.User, Rng = new Random(5), Debug = sim.Debug, NowUtc = T0, Pets = sim.Pets, Social = sim.Social,
    };

    private static float Run(Behavior.Behavior b, BehaviorContext ctx, float limit = 60f)
    {
        b.Start(ctx);
        var t = 0f;
        for (; t < limit; t += 0.1f)
            if (b.Update(ctx, 0.1f)) break;
        b.Stop(ctx, interrupted: false);
        return t;
    }

    [Fact]
    public void A_shiba_shakes_its_hat_off_onto_the_floor_and_it_can_be_put_back()
    {
        var (sim, cell) = World();
        var shiba = Add(sim, cell, Breed.Shiba, new Vec2(900, 900));
        var hat = new OutfitPiece("beanie", 0x3C8DDE);
        shiba.Outfit.Set(OutfitSlot.Head, hat);

        var shake = new ShakeOffBehavior();
        Assert.Equal(0f, shake.Score(Ctx(sim, cell, shiba)));           // not straight away
        shake.TickCooldown(10000f);
        Assert.True(shake.Score(Ctx(sim, cell, shiba)) > 0.4f);
        Run(shake, Ctx(sim, cell, shiba));

        var dropped = Assert.Single(cell.Dropped);
        Assert.Equal(hat, dropped.Piece);
        Assert.Null(shiba.Wears(OutfitSlot.Head));
        Assert.Equal(hat, shiba.Outfit.Get(OutfitSlot.Head));           // still its own: saved, comes back
        Assert.Equal(0f, shake.Score(Ctx(sim, cell, shiba)));           // rests a long while

        Assert.Equal(1, sim.PutBack(shiba));
        Assert.Empty(cell.Dropped);
        Assert.Equal(hat, shiba.Wears(OutfitSlot.Head));

        // A Maltese loves its ribbon and never shakes it off.
        var maltese = Add(sim, cell, Breed.Maltese, new Vec2(500, 900));
        maltese.Outfit.Set(OutfitSlot.Head, new OutfitPiece("ribbon", 0xF0508C));
        var keep = new ShakeOffBehavior();
        keep.TickCooldown(10000f);
        Assert.Equal(0f, keep.Score(Ctx(sim, cell, maltese)));
    }

    [Fact]
    public void A_piece_left_on_the_floor_goes_back_on_after_a_while()
    {
        var (sim, cell) = World();
        var husky = Add(sim, cell, Breed.SiberianHusky, new Vec2(900, 900));
        husky.Outfit.Set(OutfitSlot.Face, new OutfitPiece("sunglasses", 0x2E2A28));
        var lying = new DroppedWear { OwnerId = husky.Id, Slot = OutfitSlot.Face, Piece = husky.Outfit.Get(OutfitSlot.Face)!, Position = new Vec2(950, 900) };
        cell.Dropped.Add(lying);
        husky.Doffed.Add(OutfitSlot.Face);

        var backOn = false;
        for (var t = 0f; t < Simulation.DroppedBackAfter + 2f; t += 0.25f)
        {
            sim.Tick(0.25f, T0);
            backOn |= !cell.Dropped.Contains(lying) && husky.Wears(OutfitSlot.Face)?.ItemId == "sunglasses";
        }
        // (A husky may well shake them off again right after: that's a new piece on the floor.)
        Assert.True(backOn);
        Assert.DoesNotContain(lying, cell.Dropped);
    }

    [Fact]
    public void A_playful_dog_runs_off_wearing_a_hat_it_found_then_drops_it_again()
    {
        var (sim, cell) = World();
        var owner = Add(sim, cell, Breed.Shiba, new Vec2(1400, 900));
        var hat = new OutfitPiece("straw-hat", 0xE84A5F);
        owner.Outfit.Set(OutfitSlot.Head, hat);
        owner.Doffed.Add(OutfitSlot.Head);
        cell.Dropped.Add(new DroppedWear { OwnerId = owner.Id, Slot = OutfitSlot.Head, Piece = hat, Position = new Vec2(1100, 900) });

        var dog = Add(sim, cell, Breed.Bichon, new Vec2(700, 900));
        dog.Personality.Playfulness = 0.9f;
        dog.Needs.Energy = 90f;
        var steal = new StealWearBehavior();
        steal.TickCooldown(10000f);
        Assert.True(steal.Score(Ctx(sim, cell, dog)) > 0.5f);

        var ctx = Ctx(sim, cell, dog);
        steal.Start(ctx);
        var wore = false;
        for (var t = 0f; t < 60f; t += 0.1f)
        {
            if (steal.Update(ctx, 0.1f)) break;
            wore |= dog.Wears(OutfitSlot.Head) == hat;
        }
        steal.Stop(ctx, false);

        Assert.True(wore, "wore the hat on its victory lap");
        Assert.Null(dog.Borrowed);
        var back = Assert.Single(cell.Dropped);
        Assert.Equal(owner.Id, back.OwnerId);                                // still the shiba's
        Assert.Null(owner.Wears(OutfitSlot.Head));
        Assert.Equal(1, sim.PutBack(owner));
        Assert.Equal(hat, owner.Wears(OutfitSlot.Head));

        // Cats don't do this.
        var cat = Add(sim, cell, Breed.Bengal, new Vec2(700, 900));
        Assert.DoesNotContain(cat.Engine.Behaviors, b => b is StealWearBehavior);
    }

    [Fact]
    public void Pets_in_matching_outfits_pose_together_and_grow_closer()
    {
        var (sim, cell) = World();
        var a = Add(sim, cell, Breed.Maltese, new Vec2(500, 900));
        var b = Add(sim, cell, Breed.Persian, new Vec2(1300, 900));
        Assert.Equal(0, OutfitCatalog.Matching(a, b));
        a.Outfit.Set(OutfitSlot.Head, new OutfitPiece("ribbon", 0xF0508C));
        b.Outfit.Set(OutfitSlot.Head, new OutfitPiece("ribbon", 0x5A9CF0));   // same piece, another colour still counts
        a.Outfit.Set(OutfitSlot.Neck, new OutfitPiece("bell", 0xE84A5F));
        b.Outfit.Set(OutfitSlot.Neck, new OutfitPiece("bell", 0xE84A5F));
        Assert.Equal(2, OutfitCatalog.Matching(a, b));

        var before = sim.Social.Friendship(a, b);
        var pose = new CoupleLookBehavior();
        pose.TickCooldown(10000f);
        Assert.True(pose.Score(Ctx(sim, cell, a)) > 0.3f);
        var ctx = Ctx(sim, cell, a);
        pose.Start(ctx);
        Assert.Equal(a.Id, b.PoseWith);                                      // invited
        var invited = new CoupleLookBehavior();
        Assert.True(invited.Score(Ctx(sim, cell, b)) > 1f);                  // comes straight away
        for (var t = 0f; t < 30f; t += 0.1f)
            if (pose.Update(ctx, 0.1f)) break;
        Assert.True(Vec2.Distance(a.Position, b.Position) < 200f, "side by side");
        Assert.Equal(90f, a.Heading);                                        // facing you
        pose.Stop(ctx, false);
        Assert.Null(b.PoseWith);
        Assert.True(sim.Social.Friendship(a, b) > before);

        // Sharing a screen in matching outfits keeps bringing them closer.
        var mid = sim.Social.Friendship(a, b);
        for (var t = 0f; t < 600f; t += 1f) sim.Tick(1f, T0);
        Assert.True(sim.Social.Friendship(a, b) > mid);
    }
}
