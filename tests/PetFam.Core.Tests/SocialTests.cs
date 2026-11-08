using PetFam.Core.Behavior.Behaviors;
using PetFam.Core.Geometry;
using PetFam.Core.Pets;
using PetFam.Core.Platform;
using PetFam.Core.Social;
using PetFam.Core.World;

namespace PetFam.Core.Tests;

public class SocialTests
{
    private static readonly DateTime T0 = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);

    private sealed class FakePlatform : IPlatform
    {
        public Vec2 Cursor { get; set; } = new(-5000, -5000);
        public bool OnDesktop { get; set; } = true;
        public void RegisterOwnWindow(IntPtr handle) { }
        public ForegroundWindowInfo? GetForegroundWindow() => null;
        public Vec2 GetCursorPosition() => Cursor;
        public TimeSpan GetIdleTime() => TimeSpan.Zero;
        public bool IsDoNotDisturb() => false;
        public string? GetWallpaperPath() => null;
        public WallpaperFit GetWallpaperFit() => WallpaperFit.Fill;
        public bool IsDesktopAt(Vec2 screenPoint) => OnDesktop;
    }

    private static (Simulation sim, Cell cell, FakePlatform platform) MakeSim(int seed = 11)
    {
        var platform = new FakePlatform();
        var sim = new Simulation(platform, new Random(seed));
        var bounds = new RectI(0, 0, 1920, 1080);
        var working = new RectI(0, 0, 1920, 1040);
        var cell = sim.World.Upsert(new Cell
        {
            Id = "d:m", DesktopId = "d", MonitorId = "m", Bounds = bounds, WorkingArea = working,
            Map = HabitatMap.Flat(bounds, working),
        });
        return (sim, cell, platform);
    }

    private static Pet Add(Simulation sim, Cell cell, PetSpecies species, string name, Vec2 at, float playfulness = 0.9f)
    {
        var p = sim.AddPet(species, name, cell, T0);
        p.Position = at;
        p.Needs.Curiosity = 0f;
        p.Personality.Playfulness = playfulness;
        return p;
    }

    /// <summary>Runs the session to the end, collecting every animation each pet showed.</summary>
    private static (HashSet<string> a, HashSet<string> b, float maxLiftB) RunSession(Simulation sim, Pet a, Pet b, float seconds = 40f)
    {
        var seenA = new HashSet<string>();
        var seenB = new HashSet<string>();
        var lift = 0f;
        for (var i = 0; i < (int)(seconds / 0.1f) && (a.Session is not null || b.Session is not null); i++)
        {
            sim.Tick(0.1f, T0);
            seenA.Add(a.Animation);
            seenB.Add(b.Animation);
            lift = Math.Max(lift, b.Lift);
        }
        return (seenA, seenB, lift);
    }

    [Fact]
    public void Dog_play_bows_and_a_friendly_cat_runs_then_swats()
    {
        var (sim, cell, _) = MakeSim();
        var dog = Add(sim, cell, PetSpecies.Dog, "Peanut", new Vec2(700, 800));
        var cat = Add(sim, cell, PetSpecies.Cat, "Momo", new Vec2(1100, 820), playfulness: 1f);
        sim.Social.Get(dog, cat).Friendship = 100f;
        var friendship = sim.Social.Friendship(dog, cat);

        Assert.True(sim.StartPlay(dog, cat));
        Assert.IsType<PlayBowSession>(dog.Session);
        var (dogSeen, catSeen, _) = RunSession(sim, dog, cat);

        Assert.Contains("playbow", dogSeen);
        Assert.Contains("run", catSeen);
        Assert.Contains("bat", catSeen);
        Assert.Null(dog.Session);
        Assert.Null(cat.Session);
        Assert.True(sim.Social.Friendship(dog, cat) >= friendship);
    }

    [Fact]
    public void An_unfriendly_cat_hisses_and_the_dog_backs_off_and_loses_rank()
    {
        var (sim, cell, _) = MakeSim();
        var dog = Add(sim, cell, PetSpecies.Dog, "Peanut", new Vec2(700, 800));
        var cat = Add(sim, cell, PetSpecies.Cat, "Momo", new Vec2(1100, 820), playfulness: 0f);
        sim.Social.Get(dog, cat).Friendship = 0f;
        cat.Needs.Energy = 20f;

        sim.StartPlay(dog, cat);
        var (_, catSeen, _) = RunSession(sim, dog, cat);

        Assert.Contains("hiss", catSeen);
        Assert.True(sim.Social.DominanceOf(cat, dog) > 0f);
        Assert.Equal(1, sim.Social.Rank(cat, sim.Pets));
        Assert.Equal(2, sim.Social.Rank(dog, sim.Pets));
    }

    [Fact]
    public void A_cat_stalks_wiggles_and_pounces_on_a_dog_who_jumps()
    {
        var (sim, cell, _) = MakeSim();
        var cat = Add(sim, cell, PetSpecies.Cat, "Momo", new Vec2(600, 800));
        var dog = Add(sim, cell, PetSpecies.Dog, "Peanut", new Vec2(1150, 820), playfulness: 1f);
        sim.Social.Get(dog, cat).Friendship = 100f;

        sim.Director.Start(new StalkPounceSession(cat, dog));
        var (catSeen, dogSeen, dogLift) = RunSession(sim, cat, dog);

        Assert.Contains("stalk", catSeen);
        Assert.Contains("wiggle", catSeen);
        Assert.Contains("pounce", catSeen);
        Assert.Contains("startle", dogSeen);
        Assert.True(dogLift > 5f);
        Assert.Equal(0f, cat.Lift);
    }

    [Fact]
    public void Two_cats_wrestle_and_the_winner_gains_rank()
    {
        var (sim, cell, _) = MakeSim();
        var a = Add(sim, cell, PetSpecies.Cat, "Momo", new Vec2(600, 800));
        var b = Add(sim, cell, PetSpecies.Cat, "Nabi", new Vec2(1100, 820), playfulness: 1f);
        sim.Social.Get(a, b).Friendship = 100f;

        sim.Director.Start(new StalkPounceSession(a, b));
        var (aSeen, bSeen, _) = RunSession(sim, a, b);

        Assert.Contains("wrestle", aSeen);
        Assert.Contains("wrestle", bSeen);
        Assert.NotEqual(0f, sim.Social.DominanceOf(a, b));
    }

    [Fact]
    public void Two_dogs_bow_chase_box_and_one_submits()
    {
        var (sim, cell, _) = MakeSim();
        var a = Add(sim, cell, PetSpecies.Dog, "Peanut", new Vec2(600, 800));
        var b = Add(sim, cell, PetSpecies.Dog, "Choco", new Vec2(1100, 820), playfulness: 1f);
        sim.Social.Get(a, b).Friendship = 100f;

        sim.StartPlay(a, b);
        var (aSeen, bSeen, _) = RunSession(sim, a, b);
        var all = aSeen.Concat(bSeen).ToHashSet();

        Assert.Contains("playbow", bSeen);   // bowed back
        Assert.Contains("box", all);
        Assert.Contains("submit", all);
        Assert.Contains("standover", all);
        Assert.NotEqual(0f, sim.Social.DominanceOf(a, b));
    }

    [Fact]
    public void Picking_up_a_pet_ends_its_game()
    {
        var (sim, cell, _) = MakeSim();
        var dog = Add(sim, cell, PetSpecies.Dog, "Peanut", new Vec2(700, 800));
        var cat = Add(sim, cell, PetSpecies.Cat, "Momo", new Vec2(1100, 820));
        sim.StartPlay(dog, cat);
        sim.Tick(0.1f, T0);
        sim.BeginCarry(cat);
        sim.Tick(0.1f, T0);
        sim.Tick(0.1f, T0);
        Assert.Null(cat.Session);
        Assert.Null(dog.Session);
        Assert.Empty(sim.Director.Active);
    }

    [Fact]
    public void The_lower_ranked_dog_waits_while_the_top_dog_eats()
    {
        var (sim, cell, _) = MakeSim();
        var top = Add(sim, cell, PetSpecies.Dog, "Peanut", new Vec2(800, 800));
        var low = Add(sim, cell, PetSpecies.Dog, "Choco", new Vec2(1000, 800));
        sim.Social.ShiftDominance(top, low, 0.6f);
        top.Needs.Hunger = 20f;
        low.Needs.Hunger = 20f;
        sim.PlaceBowl(cell, new Vec2(900, 900));

        var waited = false;
        for (var i = 0; i < 120; i++)
        {
            sim.Tick(0.1f, T0);
            if (low.Engine.Current?.Name == "wait-for-food" && top.Engine.Current?.Name == "eat") waited = true;
        }
        Assert.True(waited);
        Assert.True(top.Needs.Hunger > low.Needs.Hunger);
    }

    [Fact]
    public void A_cat_hunts_the_moving_pointer_and_stops_when_it_is_still()
    {
        var (sim, cell, platform) = MakeSim();
        var cat = Add(sim, cell, PetSpecies.Cat, "Momo", new Vec2(500, 850), playfulness: 1f);
        var center = new Vec2(1150, 850);

        var seen = new HashSet<string>();
        var maxLift = 0f;
        for (var i = 0; i < 200; i++)
        {
            var a = i * 0.25f;
            platform.Cursor = center + new Vec2(MathF.Cos(a) * 60f, MathF.Sin(a) * 30f);   // a wriggling "mouse"
            sim.Tick(0.1f, T0);
            seen.Add(cat.Animation);
            maxLift = Math.Max(maxLift, cat.Lift);
        }
        Assert.Contains("stalk", seen);
        Assert.Contains("wiggle", seen);
        Assert.Contains("pounce", seen);
        Assert.Contains("bat", seen);
        Assert.True(maxLift > 20f);

        // The pointer stops: the cat loses interest.
        for (var i = 0; i < 60; i++) sim.Tick(0.1f, T0);
        Assert.IsNotType<HuntCursorBehavior>(cat.Engine.Current);
    }

    [Fact]
    public void Dogs_do_not_hunt_the_pointer()
    {
        var (sim, cell, platform) = MakeSim();
        var dog = Add(sim, cell, PetSpecies.Dog, "Peanut", new Vec2(800, 850));
        for (var i = 0; i < 100; i++)
        {
            platform.Cursor = new Vec2(1100 + MathF.Cos(i * 0.3f) * 80f, 850);
            sim.Tick(0.1f, T0);
            Assert.NotEqual("pounce", dog.Animation);
        }
    }

    [Fact]
    public void Adopted_pets_get_new_names_and_relations_survive_save()
    {
        var (sim, cell, _) = MakeSim();
        var a = sim.AdoptPet(PetSpecies.Cat, cell, T0)!;
        var b = sim.AdoptPet(PetSpecies.Cat, cell, T0)!;
        Assert.NotEqual(a.Name, b.Name);
        sim.Social.AddFriendship(a, b, 30f);
        sim.Social.ShiftDominance(a, b, 0.4f);

        var save = Persistence.SaveStore.ToSave(sim.Pets, T0, sim.Social);
        var rel = Assert.Single(save.Relations);
        Assert.Equal(70f, rel.Friendship);
        Assert.Equal(0.4f, Math.Abs(rel.Dominance), precision: 3);
    }

    [Fact]
    public void Cats_only_start_stalking_a_cat_that_is_near()
    {
        var (sim, cell, _) = MakeSim(seed: 5);
        var a = Add(sim, cell, PetSpecies.Cat, "Momo", new Vec2(250, 950), playfulness: 1f);
        var b = Add(sim, cell, PetSpecies.Cat, "Nabi", new Vec2(1700, 950), playfulness: 1f);
        foreach (var p in new[] { a, b }) { p.Growth = new Growth(T0, Growth.AdultAt); p.Needs.Fun = 0f; }

        // Whenever an encounter starts, the two were close at that moment (they may wander together first).
        var starts = 0;
        var wasInSession = false;
        for (var i = 0; i < 3000; i++)
        {
            sim.Tick(0.1f, T0);
            var inSession = a.Session is not null;
            if (inSession && !wasInSession)
            {
                starts++;
                var depth = (cell.Map.DepthScale(a.Position.Y) + cell.Map.DepthScale(b.Position.Y)) / 2f;
                Assert.True(Vec2.Distance(a.Position, b.Position) <= 420f * depth,
                    $"started {Vec2.Distance(a.Position, b.Position):F0} px apart");
            }
            wasInSession = inSession;
        }
    }

    [Fact]
    public void Cats_side_by_side_do_start_playing()
    {
        var (sim, cell, _) = MakeSim(seed: 5);
        var a = Add(sim, cell, PetSpecies.Cat, "Momo", new Vec2(800, 950), playfulness: 1f);
        var b = Add(sim, cell, PetSpecies.Cat, "Nabi", new Vec2(950, 950), playfulness: 1f);
        foreach (var p in new[] { a, b }) { p.Growth = new Growth(T0, Growth.AdultAt); p.Needs.Fun = 0f; }
        var started = false;
        for (var i = 0; i < 1200 && !started; i++)
        {
            sim.Tick(0.1f, T0);
            started = a.Session is not null;
        }
        Assert.True(started);
    }
}
