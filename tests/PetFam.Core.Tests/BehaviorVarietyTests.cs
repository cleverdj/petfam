using PetFam.Core.Behavior;
using PetFam.Core.Behavior.Behaviors;
using PetFam.Core.Geometry;
using PetFam.Core.Pets;
using PetFam.Core.World;

namespace PetFam.Core.Tests;

public class BehaviorVarietyTests
{
    private static readonly DateTime T0 = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);

    private static (Pet Pet, BehaviorContext Ctx) Make(PetSpecies species, Breed? breed = null, bool daytime = true)
    {
        var bounds = new RectI(0, 0, 1920, 1080);
        var working = new RectI(0, 0, 1920, 1040);
        var cell = new Cell { Id = "d:m", DesktopId = "d", MonitorId = "m", Bounds = bounds, WorkingArea = working, Map = HabitatMap.Flat(bounds, working) };
        var rng = new Random(7);
        var pet = new Pet(species, "t", T0, rng, breed) { CellId = "d:m", Position = new Vec2(960, 900) };
        pet.Growth = new Growth(T0, Growth.AdultAt);
        var ctx = new BehaviorContext
        {
            Pet = pet, Cell = cell, User = new UserContext { Idle = TimeSpan.Zero, Cursor = new Vec2(-5000, -5000) },
            Rng = rng, Debug = new DebugOptions(), NowUtc = T0, IsDaytime = daytime,
        };
        return (pet, ctx);
    }

    /// <summary>Runs a behavior from start to finish (or the time limit); returns how long it took.</summary>
    private static float Run(Behavior.Behavior b, BehaviorContext ctx, float limit = 60f, Action? each = null)
    {
        b.Start(ctx);
        var t = 0f;
        for (; t < limit; t += 0.1f)
        {
            each?.Invoke();
            if (b.Update(ctx, 0.1f)) break;
        }
        b.Stop(ctx, interrupted: false);
        return t;
    }

    private static void Ready(CooldownBehavior b) => b.TickCooldown(10000f);

    [Fact]
    public void Zoomies_cover_the_floor_at_speed_then_the_pet_pants()
    {
        var (pet, ctx) = Make(PetSpecies.Dog, Breed.Bichon);
        var z = new ZoomiesBehavior();
        Assert.Equal(0f, z.Score(ctx));                       // not right after arriving
        Ready(z);
        Assert.True(z.Score(ctx) > 0.4f);

        var start = pet.Position;
        var farthest = 0f;
        var panted = false;
        Run(z, ctx, each: () =>
        {
            farthest = Math.Max(farthest, Vec2.Distance(start, pet.Position));
            panted |= pet.Animation == "pant";
        });
        Assert.True(farthest > 300f, $"ran {farthest:F0} px");
        Assert.True(panted);
        Assert.Equal(0f, z.Score(ctx));                       // rests afterwards
    }

    [Fact]
    public void Cats_get_the_zoomies_more_at_night()
    {
        var (_, day) = Make(PetSpecies.Cat);
        var (_, night) = Make(PetSpecies.Cat, daytime: false);
        var a = new ZoomiesBehavior();
        var b = new ZoomiesBehavior();
        Ready(a);
        Ready(b);
        Assert.True(b.Score(night) > a.Score(day));
    }

    [Fact]
    public void A_dog_chases_its_tail_round_and_round_and_ends_where_it_began()
    {
        var (pet, ctx) = Make(PetSpecies.Dog);
        var c = new ChaseTailBehavior();
        Ready(c);
        Assert.True(c.Score(ctx) > 0f);
        var start = pet.Position;
        Run(c, ctx);
        Assert.True(c.Turned >= 720f, $"turned {c.Turned:F0}°");
        Assert.True(Vec2.Distance(start, pet.Position) < 2f);

        var (_, catCtx) = Make(PetSpecies.Cat);
        var catChase = new ChaseTailBehavior();
        Ready(catChase);
        Assert.Equal(0f, catChase.Score(catCtx));             // cats don't
    }

    [Fact]
    public void A_dog_sniffs_its_way_around_and_hounds_do_it_more()
    {
        var (pet, ctx) = Make(PetSpecies.Dog, Breed.Beagle);
        var s = new SniffBehavior();
        Ready(s);
        var sniffs = 0;
        var was = "";
        Run(s, ctx, each: () =>
        {
            if (pet.Animation == "sniff" && was != "sniff") sniffs++;
            was = pet.Animation;
        });
        Assert.InRange(sniffs, 3, 4);

        var (_, shibaCtx) = Make(PetSpecies.Dog, Breed.Shiba);
        var shiba = new SniffBehavior();
        Ready(shiba);
        var beagle = new SniffBehavior();
        Ready(beagle);
        Assert.True(beagle.Score(Make(PetSpecies.Dog, Breed.Beagle).Ctx) > shiba.Score(shibaCtx));
    }

    [Fact]
    public void A_cat_watches_something_up_in_the_sky_then_looks_away()
    {
        var (pet, ctx) = Make(PetSpecies.Cat);
        var w = new WatchBehavior();
        Ready(w);
        Vec2? gaze = null;
        Run(w, ctx, each: () => gaze ??= pet.Gaze);
        Assert.NotNull(gaze);
        Assert.True(gaze!.Value.Y < ctx.Cell.Map.RoamArea.Top, "looks up above the floor");
        Assert.Null(pet.Gaze);
    }

    [Fact]
    public void A_hungry_dog_comes_to_beg_under_the_pointer_unless_there_is_food()
    {
        var (pet, ctx) = Make(PetSpecies.Dog);
        ctx.User.Cursor = new Vec2(400, 700);
        ctx.User.CursorOnDesktop = true;
        pet.Needs.Hunger = 20f;
        var beg = new BegBehavior();
        Ready(beg);
        Assert.True(beg.Score(ctx) > 0.5f);

        var sat = false;
        Run(beg, ctx, each: () => sat |= pet.Animation == "sit" && Math.Abs(pet.Position.X - 400) < 120);
        Assert.True(sat);
        Assert.True(pet.Bubble is not null);

        ctx.Cell.Bowls.Add(new FoodBowl { Position = new Vec2(1200, 950) });
        var again = new BegBehavior();
        Ready(again);
        Assert.Equal(0f, again.Score(ctx));                   // there is food: it eats instead
    }

    [Fact]
    public void A_dog_that_trusts_you_trots_after_the_moving_pointer()
    {
        var (pet, ctx) = Make(PetSpecies.Dog);
        ctx.User.CursorOnDesktop = true;
        ctx.User.CursorSpeed = 400f;
        ctx.User.Cursor = new Vec2(300, 950);
        var f = new FollowCursorBehavior();
        Ready(f);
        Assert.Equal(0f, f.Score(ctx));                       // a stranger does not follow you
        pet.Bond.DebugSet(70f, T0);
        Assert.True(f.Score(ctx) > 0.5f);

        var startGap = Vec2.Distance(pet.Position, ctx.User.Cursor);
        f.Start(ctx);
        for (var i = 0; i < 40; i++) f.Update(ctx, 0.1f);
        Assert.True(Vec2.Distance(pet.Position, ctx.User.Cursor) < startGap - 200f);
    }

    [Fact]
    public void Tired_pets_yawn()
    {
        var (pet, ctx) = Make(PetSpecies.Cat);
        var y = new YawnBehavior();
        Ready(y);
        Assert.Equal(0f, y.Score(ctx));                       // full of energy
        pet.Needs.Energy = 30f;
        Assert.True(y.Score(ctx) > 0.3f);
        var yawned = false;
        Run(y, ctx, each: () => yawned |= pet.Animation == "yawn");
        Assert.True(yawned);
    }

    private sealed class QuietPlatform : PetFam.Core.Platform.IPlatform
    {
        public void RegisterOwnWindow(IntPtr handle) { }
        public PetFam.Core.Platform.ForegroundWindowInfo? GetForegroundWindow() => null;
        public Vec2 GetCursorPosition() => new(-5000, -5000);
        public TimeSpan GetIdleTime() => TimeSpan.Zero;
        public bool IsDoNotDisturb() => false;
        public string? GetWallpaperPath() => null;
        public PetFam.Core.Platform.WallpaperFit GetWallpaperFit() => PetFam.Core.Platform.WallpaperFit.Fill;
        public bool IsDesktopAt(Vec2 screenPoint) => true;
    }

    [Fact]
    public void Over_an_afternoon_pets_do_many_different_things_and_none_takes_over()
    {
        var sim = new Simulation(new QuietPlatform(), new Random(3));
        var bounds = new RectI(0, 0, 1920, 1080);
        var working = new RectI(0, 0, 1920, 1040);
        var cell = sim.World.Upsert(new Cell { Id = "d:m", DesktopId = "d", MonitorId = "m", Bounds = bounds, WorkingArea = working, Map = HabitatMap.Flat(bounds, working) });
        var dog = sim.AddPet(Breed.Beagle, "Snoopy", cell, T0);
        var cat = sim.AddPet(Breed.Bengal, "Leo", cell, T0);
        foreach (var p in new[] { dog, cat }) { p.Growth = new Growth(T0, Growth.JuniorAt + 5); p.Bond.DebugSet(60f, T0); }
        // Early afternoon where the clock thinks it is: the sun follows the clock's place, the time of day follows
        // the computer's time zone, and on a build machine those two need not match.
        var (rise, set) = sim.Clock.SunToday(T0)!.Value;
        sim.Clock.Pretend = (rise + (set - rise) / 2).TimeOfDay;

        var time = new Dictionary<string, float>();
        var seen = new HashSet<string>();
        for (var i = 0; i < 2 * 3600 * 10; i++)
        {
            sim.Tick(0.1f, T0.AddSeconds(i * 0.1));
            foreach (var p in new[] { dog, cat })
            {
                var name = p.Engine.Current?.GetType().Name ?? "-";
                seen.Add(p.Species + ":" + name);
                time[name] = time.GetValueOrDefault(name) + 0.1f;
            }
            if (i % 9000 == 0) { dog.Needs.Hunger = 40f; }   // peckish now and then
        }

        foreach (var expected in new[] { "Dog:ZoomiesBehavior", "Dog:ChaseTailBehavior", "Dog:SniffBehavior", "Cat:WatchBehavior", "Cat:ZoomiesBehavior", "Cat:KneadBehavior", "Dog:ScratchBehavior", "Cat:ButterflyBehavior" })
            Assert.Contains(expected, seen);
        var total = time.Values.Sum();
        foreach (var name in new[] { "ZoomiesBehavior", "ChaseTailBehavior", "SniffBehavior", "WatchBehavior", "BegBehavior", "YawnBehavior", "ButterflyBehavior", "KneadBehavior", "ScratchBehavior", "WindowPerchBehavior", "RoofPerchBehavior", "KnockBowlBehavior", "InvitePlayBehavior" })
            Assert.True(time.GetValueOrDefault(name) / total < 0.2f, $"{name} took {100 * time.GetValueOrDefault(name) / total:F0}% of the time");
    }
}
