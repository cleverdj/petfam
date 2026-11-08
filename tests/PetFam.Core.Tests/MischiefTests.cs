using PetFam.Core.Behavior;
using PetFam.Core.Behavior.Behaviors;
using PetFam.Core.Geometry;
using PetFam.Core.Pets;
using PetFam.Core.Platform;
using PetFam.Core.World;

namespace PetFam.Core.Tests;

public class MischiefTests
{
    private static readonly DateTime T0 = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);

    private static (Pet Pet, BehaviorContext Ctx) Make(PetSpecies species, Breed? breed = null, bool daytime = true, WeatherNow? weather = null)
    {
        var bounds = new RectI(0, 0, 1920, 1080);
        var working = new RectI(0, 0, 1920, 1040);
        var cell = new Cell { Id = "d:m", DesktopId = "d", MonitorId = "m", Bounds = bounds, WorkingArea = working, Map = HabitatMap.Flat(bounds, working) };
        var rng = new Random(11);
        var pet = new Pet(species, "t", T0, rng, breed) { CellId = "d:m", Position = new Vec2(960, 900) };
        pet.Growth = new Growth(T0, Growth.AdultAt);
        var ctx = new BehaviorContext
        {
            Pet = pet, Cell = cell, User = new UserContext { Idle = TimeSpan.Zero, Cursor = new Vec2(-5000, -5000) },
            Rng = rng, Debug = new DebugOptions(), NowUtc = T0, IsDaytime = daytime, Weather = weather ?? WeatherNow.Clear,
        };
        return (pet, ctx);
    }

    private static float Run(Behavior.Behavior b, BehaviorContext ctx, float limit = 90f, Action? each = null)
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
    public void A_cat_stares_at_you_then_knocks_the_food_bowl_away()
    {
        var (pet, ctx) = Make(PetSpecies.Cat);
        var bowl = new FoodBowl { Position = new Vec2(700, 900) };
        ctx.Cell.Bowls.Add(bowl);
        pet.Needs.Hunger = 90f;
        var k = new KnockBowlBehavior();
        Ready(k);
        Assert.True(k.Score(ctx) > 0f);

        var stared = false;
        var batted = false;
        var t = Run(k, ctx, each: () =>
        {
            stared |= pet.Animation == "sit" && Math.Abs(pet.Heading - 90f) < 1f;
            batted |= pet.Animation == "bat";
        });
        Assert.True(t < 60f, "finished");
        Assert.True(stared && batted);
        Assert.True(Vec2.Distance(bowl.Position, new Vec2(700, 900)) > 40f, "the bowl moved");
        Assert.True(ctx.Cell.Map.CanRoam(bowl.Position));
        Assert.Equal(0f, k.Score(ctx));                       // not again right away

        var (_, dogCtx) = Make(PetSpecies.Dog);
        dogCtx.Cell.Bowls.Add(new FoodBowl { Position = new Vec2(700, 900) });
        var dog = new KnockBowlBehavior();
        Ready(dog);
        Assert.Equal(0f, dog.Score(dogCtx));
    }

    [Fact]
    public void A_cat_climbs_onto_your_window_sits_on_top_and_comes_back_down()
    {
        var (pet, ctx) = Make(PetSpecies.Cat);
        var win = new RectI(500, 300, 900, 600);
        ctx.User.Foreground = new ForegroundWindowInfo(new IntPtr(42), win, "Notes", "N", false, false);
        var w = new WindowPerchBehavior();
        Ready(w);
        Assert.True(w.Score(ctx) > 0f);

        var maxRise = 0f;
        var onEdge = false;
        Run(w, ctx, each: () =>
        {
            if (pet.Perch is { } s)
            {
                maxRise = Math.Max(maxRise, s.Rise);
                onEdge |= s.Rise >= 1f && s.AnchorX >= win.Left && s.AnchorX <= win.Right && pet.Layer == PetLayer.WindowEdge;
            }
        });
        Assert.Equal(1f, maxRise);
        Assert.True(onEdge);
        Assert.Null(pet.Perch);
        Assert.Equal(PetLayer.Backyard, pet.Layer);
        Assert.True(ctx.Cell.Map.CanRoam(pet.Position));

        // No room above a window at the very top of the screen.
        var (_, top) = Make(PetSpecies.Cat);
        top.User.Foreground = new ForegroundWindowInfo(new IntPtr(43), new RectI(0, 0, 1920, 1040), "Max", "M", true, false);
        var w2 = new WindowPerchBehavior();
        Ready(w2);
        Assert.Equal(0f, w2.Score(top));
    }

    [Fact]
    public void A_perched_cat_hops_down_when_the_window_moves()
    {
        var (pet, ctx) = Make(PetSpecies.Cat);
        var win = new RectI(500, 300, 900, 600);
        ctx.User.Foreground = new ForegroundWindowInfo(new IntPtr(42), win, "Notes", "N", false, false);
        var w = new WindowPerchBehavior();
        Ready(w);
        w.Start(ctx);
        for (var i = 0; i < 400 && (pet.Perch is null || pet.Perch.Rise < 1f); i++) w.Update(ctx, 0.1f);
        Assert.NotNull(pet.Perch);

        ctx.User.Foreground = new ForegroundWindowInfo(new IntPtr(42), new RectI(100, 200, 900, 600), "Notes", "N", false, false);
        var done = false;
        for (var i = 0; i < 300 && !done; i++) done = w.Update(ctx, 0.1f);
        w.Stop(ctx, false);
        Assert.Null(pet.Perch);
        Assert.Equal(PetLayer.Backyard, pet.Layer);
    }

    [Fact]
    public void A_cat_sits_up_on_the_roof_drawn_in_front_of_the_house_and_hops_down()
    {
        var (pet, ctx) = Make(PetSpecies.Cat);
        var house = new PetHouse { Position = new Vec2(1300, 800) };
        ctx.Cell.Houses.Add(house);
        var r = new RoofPerchBehavior();
        Ready(r);
        Assert.True(r.Score(ctx) > 0f);

        var highest = float.MaxValue;
        var inFront = true;
        Run(r, ctx, each: () =>
        {
            highest = Math.Min(highest, pet.Position.Y);
            if (pet.Position.Y < house.Position.Y - 60f) inFront &= pet.Position.Y + pet.ZBoost > house.Position.Y;
        });
        var d = ctx.Cell.Map.DepthScale(house.Position.Y);
        Assert.True(highest < house.Position.Y - 90f * d, $"got up to {highest:F0}");
        Assert.True(inFront);
        Assert.Equal(0f, pet.ZBoost);
        Assert.True(ctx.Cell.Map.CanRoam(pet.Position));

        var (_, rainy) = Make(PetSpecies.Cat, weather: WeatherNow.Clear with { Kind = WeatherKind.Rain, Intensity = 0.5f });
        rainy.Cell.Houses.Add(new PetHouse { Position = new Vec2(1300, 800) });
        var r2 = new RoofPerchBehavior();
        Ready(r2);
        Assert.Equal(0f, r2.Score(rainy));                    // not in the rain
    }

    [Fact]
    public void A_bored_dog_comes_to_the_pointer_and_play_bows()
    {
        var (pet, ctx) = Make(PetSpecies.Dog, Breed.Bichon);
        ctx.User.CursorOnDesktop = true;
        ctx.User.Cursor = new Vec2(700, 850);
        pet.Needs.Fun = 20f;
        pet.Needs.Energy = 90f;
        pet.Bond.DebugSet(50f, T0);
        var inv = new InvitePlayBehavior();
        Ready(inv);
        Assert.True(inv.Score(ctx) > 0.4f);

        var bowed = false;
        var hopped = false;
        Run(inv, ctx, each: () =>
        {
            bowed |= pet.Animation == "playbow" && Vec2.Distance(pet.Position, ctx.User.Cursor) < 200f;
            hopped |= pet.Lift > 1f;
        });
        Assert.True(bowed && hopped);
        Assert.NotNull(pet.Bubble);
        Assert.Equal(0f, pet.Lift);
    }

    private sealed class WindowsPlatform : IPlatform
    {
        public ForegroundWindowInfo? Window { get; set; }
        public void RegisterOwnWindow(IntPtr handle) { }
        public ForegroundWindowInfo? GetForegroundWindow() => Window;
        public Vec2 GetCursorPosition() => new(-5000, -5000);
        public TimeSpan GetIdleTime() => TimeSpan.Zero;
        public bool IsDoNotDisturb() => false;
        public string? GetWallpaperPath() => null;
        public WallpaperFit GetWallpaperFit() => WallpaperFit.Fill;
        public bool IsDesktopAt(Vec2 screenPoint) => true;
    }

    [Fact]
    public void Dogs_bark_at_a_newly_opened_window_but_not_at_ones_already_open()
    {
        var platform = new WindowsPlatform { Window = new ForegroundWindowInfo(new IntPtr(1), new RectI(200, 200, 600, 400), "Old", "O", false, false) };
        var sim = new Simulation(platform, new Random(5));
        var bounds = new RectI(0, 0, 1920, 1080);
        var working = new RectI(0, 0, 1920, 1040);
        var cell = sim.World.Upsert(new Cell { Id = "d:m", DesktopId = "d", MonitorId = "m", Bounds = bounds, WorkingArea = working, Map = HabitatMap.Flat(bounds, working) });
        var dog = sim.AddPet(Breed.Chihuahua, "Taco", cell, T0);
        dog.Growth = new Growth(T0, Growth.AdultAt);
        dog.Personality.Shyness = 1f;                         // a wary little dog: always barks
        var cat = sim.AddPet(Breed.Bengal, "Leo", cell, T0);
        cat.Personality.Shyness = 1f;

        var barked = 0;
        void RunFor(float seconds, double from)
        {
            for (var i = 0; i < seconds * 10; i++)
            {
                sim.Tick(0.1f, T0.AddSeconds(from + i * 0.1));
                if (dog.Engine.Current is AlertBehavior && dog.Animation == "hiss") barked++;
            }
        }

        RunFor(10, 0);
        Assert.Equal(0, barked);                              // the window was already open
        platform.Window = new ForegroundWindowInfo(new IntPtr(2), new RectI(800, 300, 600, 400), "New", "N", false, false);
        RunFor(5, 10);
        Assert.True(barked > 0);
        Assert.Null(cat.Alert);                               // cats don't bark
        Assert.Null(dog.Alert);                               // answered
        barked = 0;
        platform.Window = new ForegroundWindowInfo(new IntPtr(1), new RectI(200, 200, 600, 400), "Old", "O", false, false);
        RunFor(5, 15);
        Assert.Equal(0, barked);                              // back to a window it knows
    }

    [Fact]
    public void A_butterfly_comes_by_the_pet_pounces_and_it_always_gets_away()
    {
        var (pet, ctx) = Make(PetSpecies.Cat, Breed.Bengal);
        var b = new ButterflyBehavior();
        Ready(b);
        Assert.True(b.Score(ctx) > 0f);

        var pounces = 0;
        var was = "";
        var lowest = 0f;
        var start = Vec2.Zero;
        var t = Run(b, ctx, each: () =>
        {
            if (pet.Animation == "pounce" && was != "pounce") pounces++;
            was = pet.Animation;
            if (pet.Butterfly is { } f) lowest = Math.Max(lowest, f.Y);
            Assert.True(pet.Butterfly is null || pet.Butterfly.Value.Y < pet.Position.Y, "it flies above the floor");
        });
        Assert.True(t < 60f);
        Assert.InRange(pounces, 2, 3);
        Assert.Null(pet.Butterfly);
        Assert.Null(pet.Gaze);
        Assert.Equal(0f, pet.Lift);

        var (_, night) = Make(PetSpecies.Cat, daytime: false);
        var nb = new ButterflyBehavior();
        Ready(nb);
        Assert.Equal(0f, nb.Score(night));                    // no butterflies at night
    }

    [Fact]
    public void Cats_knead_and_dogs_scratch_their_ears()
    {
        var (cat, catCtx) = Make(PetSpecies.Cat);
        var k = new KneadBehavior();
        Ready(k);
        Assert.True(k.Score(catCtx) > 0f);
        var kneaded = false;
        Run(k, catCtx, each: () => kneaded |= cat.Animation == "knead");
        Assert.True(kneaded);

        var (dog, dogCtx) = Make(PetSpecies.Dog);
        var s = new ScratchBehavior();
        Ready(s);
        Assert.True(s.Score(dogCtx) > 0f);
        var scratched = false;
        Run(s, dogCtx, each: () => scratched |= dog.Animation == "scratch");
        Assert.True(scratched);

        var catScratch = new ScratchBehavior();
        Ready(catScratch);
        Assert.Equal(0f, catScratch.Score(catCtx));
        var dogKnead = new KneadBehavior();
        Ready(dogKnead);
        Assert.Equal(0f, dogKnead.Score(dogCtx));
    }
}
