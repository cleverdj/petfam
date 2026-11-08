using PetFam.Core.Behavior.Behaviors;
using PetFam.Core.Geometry;
using PetFam.Core.Persistence;
using PetFam.Core.Pets;
using PetFam.Core.Platform;
using PetFam.Core.World;

namespace PetFam.Core.Tests;

public class HouseAndGoodbyeTests
{
    private static readonly DateTime T0 = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);

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

    private static (Simulation Sim, Cell Cell) Make()
    {
        var sim = new Simulation(new FakePlatform(), new Random(5));
        var bounds = new RectI(0, 0, 1920, 1080);
        var working = new RectI(0, 0, 1920, 1040);
        var cell = sim.World.Upsert(new Cell
        {
            Id = "d:m", DesktopId = "d", MonitorId = "m", Bounds = bounds, WorkingArea = working,
            Map = HabitatMap.Flat(bounds, working),
        });
        return (sim, cell);
    }

    [Fact]
    public void Removing_a_pet_ends_its_play_and_forgets_its_relations()
    {
        var (sim, cell) = Make();
        var cat = sim.AddPet(PetSpecies.Cat, "Momo", cell, T0);
        var dog = sim.AddPet(PetSpecies.Dog, "Peanut", cell, T0);
        sim.Social.Get(cat, dog).Friendship = 80f;
        Assert.True(sim.StartPlay(dog, cat));
        Assert.NotNull(cat.Session);

        Assert.True(sim.RemovePet(dog));

        Assert.DoesNotContain(dog, sim.Pets);
        Assert.Null(cat.Session);                                           // the partner is free again
        Assert.DoesNotContain(sim.Social.All, r => r.A == dog.Id || r.B == dog.Id);
        Assert.False(sim.RemovePet(dog));
        for (var i = 0; i < 30; i++) sim.Tick(0.1f, T0);                    // the world keeps running
    }

    [Fact]
    public void A_sleepy_pet_naps_inside_a_house_the_user_built()
    {
        var (sim, cell) = Make();
        var dog = sim.AddPet(PetSpecies.Dog, "Peanut", cell, T0);
        var house = new PetHouse { Name = "Peanut's", Position = new Vec2(dog.Position.X + 300, dog.Position.Y) };
        Assert.True(sim.AddHouse(cell, house));
        dog.Needs.Energy = 5f;

        var wentIn = false;
        for (var i = 0; i < 600 && !wentIn; i++)
        {
            sim.Tick(0.1f, T0);
            wentIn = dog.Hidden;
        }
        Assert.True(wentIn);
        Assert.IsType<SleepBehavior>(dog.Engine.Current);
        Assert.True(Vec2.Distance(dog.Position, house.Position) < 30f);

        sim.RemoveHouse(cell, house);                                       // tearing it down wakes the sleeper out
        Assert.False(dog.Hidden);
    }

    [Fact]
    public void A_pet_goes_in_and_comes_out_through_the_door()
    {
        var (sim, cell) = Make();
        var cat = sim.AddPet(PetSpecies.Cat, "Momo", cell, T0);
        var house = new PetHouse { Position = new Vec2(cat.Position.X - 250, cat.Position.Y) };
        Assert.True(sim.AddHouse(cell, house));
        cat.Needs.Energy = 5f;

        // Walks up to the door, then through it with its back to the viewer.
        var sawDoorway = false;
        var walkedAway = false;
        for (var i = 0; i < 600 && !cat.Hidden; i++)
        {
            sim.Tick(0.1f, T0);
            if (cat.Doorway is { } d)
            {
                sawDoorway = true;
                Assert.Equal(house.Id, d.HouseId);
                Assert.True(System.Math.Abs(cat.Position.X - house.Position.X) < 3f, "lined up with the door");
                if (cat.Position.Y < house.Position.Y) walkedAway |= System.Math.Abs(cat.TargetHeading - 270f) < 20f || System.Math.Abs(cat.TargetHeading + 90f) < 20f;
            }
        }
        Assert.True(sawDoorway);
        Assert.True(walkedAway, "went in facing away");
        Assert.True(cat.Hidden);
        Assert.Equal(house.Id, cat.InsideHouse);
        Assert.Null(cat.Doorway);
        Assert.True(cat.Position.Y < house.Position.Y, "inside, behind the threshold");

        // Wakes up and walks out of the door toward the viewer.
        cat.Needs.Energy = 100f;
        var cameOut = false;
        for (var i = 0; i < 200 && cat.Engine.Current is SleepBehavior; i++)
        {
            sim.Tick(0.1f, T0);
            if (cat.Doorway is not null) { cameOut = true; Assert.False(cat.Hidden); }
        }
        Assert.True(cameOut);
        Assert.Null(cat.InsideHouse);
        Assert.True(cat.Position.Y > house.Position.Y, "in front of the house");
    }

    [Fact]
    public void Houses_are_limited_and_saved_with_their_decorations()
    {
        var (sim, cell) = Make();
        for (var i = 0; i < Cell.MaxHouses; i++)
            Assert.True(sim.AddHouse(cell, new PetHouse { Position = Simulation.SpotForNewHouse(cell) }));
        Assert.False(sim.AddHouse(cell, new PetHouse()));

        var h = cell.Houses[0];
        h.Name = "Cat Castle";
        h.Shape = HouseShape.Round;
        h.RoofColor = 3;
        h.WallColor = 2;
        h.Decorations = new HashSet<string> { "flags", "chimney" };

        var save = SaveStore.ToSave(sim.Pets, T0, sim.Social, sim.World.Cells);
        var path = Path.Combine(Path.GetTempPath(), $"petfam-house-{Guid.NewGuid():N}.json");
        try
        {
            SaveStore.Write(path, save);
            var back = SaveStore.Read(path)!;
            Assert.Equal(Cell.MaxHouses, back.Houses.Count);
            var r = SaveStore.Restore(back.Houses.Single(x => x.Id == h.Id));
            Assert.Equal("Cat Castle", r.Name);
            Assert.Equal(HouseShape.Round, r.Shape);
            Assert.Equal(3, r.RoofColor);
            Assert.Equal(2, r.WallColor);
            Assert.Equal(new HashSet<string> { "flags", "chimney" }, r.Decorations);
            Assert.Equal(h.Position, r.Position);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void New_houses_are_placed_apart()
    {
        var (sim, cell) = Make();
        sim.AddHouse(cell, new PetHouse { Position = Simulation.SpotForNewHouse(cell) });
        var second = Simulation.SpotForNewHouse(cell);
        Assert.True(MathF.Abs(second.X - cell.Houses[0].Position.X) > PetHouse.BaseWidth);
    }
}
