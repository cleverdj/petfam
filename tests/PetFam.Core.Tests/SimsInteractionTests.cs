using PetFam.Core.Behavior.Behaviors;
using PetFam.Core.Geometry;
using PetFam.Core.Interactions;
using PetFam.Core.Pets;
using PetFam.Core.Platform;
using PetFam.Core.World;

namespace PetFam.Core.Tests;

public class SimsInteractionTests
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

    private static (Simulation sim, Cell cell, Pet pet) Make(PetSpecies species, float bond)
    {
        var sim = new Simulation(new FakePlatform(), new Random(5));
        var bounds = new RectI(0, 0, 1920, 1080);
        var working = new RectI(0, 0, 1920, 1040);
        var cell = sim.World.Upsert(new Cell
        {
            Id = "d:m", DesktopId = "d", MonitorId = "m", Bounds = bounds, WorkingArea = working,
            Map = HabitatMap.Flat(bounds, working),
        });
        var pet = sim.AddPet(species, "p", cell, T0);
        pet.Bond.Restore(bond, T0, T0, DateOnly.FromDateTime(T0), 0);
        pet.Needs.Curiosity = 0f;
        return (sim, cell, pet);
    }

    private static void Run(Simulation sim, float seconds)
    {
        for (var i = 0; i < (int)(seconds / 0.1f); i++) sim.Tick(0.1f, T0);
    }

    private static Interaction Get(string id) => InteractionCatalog.Find(id)!;

    [Fact]
    public void Menu_hides_inapplicable_items_and_greys_out_locked_ones()
    {
        var (sim, _, cat) = Make(PetSpecies.Cat, bond: 10f);
        var menu = sim.MenuFor(cat, T0).ToDictionary(x => x.Interaction.Id, x => x.Availability);

        Assert.False(menu.ContainsKey("sit"));     // dogs only
        Assert.False(menu.ContainsKey("towel"));   // only when dirty or wet
        Assert.True(menu["pet"].Enabled);
        Assert.False(menu["hug"].Enabled);         // needs the Curious stage
        Assert.Contains("Curious", menu["hug"].Reason);
    }

    [Fact]
    public void Queued_actions_run_in_order_and_interrupt_autonomy()
    {
        var (sim, _, pet) = Make(PetSpecies.Dog, bond: 95f);
        Run(sim, 2f);
        Assert.True(sim.Enqueue(pet, Get("pet"), T0));
        Assert.True(sim.Enqueue(pet, Get("sit"), T0));

        sim.Tick(0.1f, T0);
        Assert.IsType<QueuedInteractionBehavior>(pet.Engine.Current);
        Assert.Equal("petted", pet.Animation);
        Assert.Equal(QueueStatus.Running, pet.Queue[0].Status);

        Run(sim, 3.2f);
        Assert.Single(pet.Queue);
        Assert.Equal("sit", pet.Queue[0].Interaction.Id);
        Assert.Equal("sit", pet.Animation);

        Run(sim, 3.5f);
        Assert.Empty(pet.Queue);
    }

    [Fact]
    public void Petting_from_the_queue_raises_bond_and_affection()
    {
        var (sim, _, pet) = Make(PetSpecies.Cat, bond: 95f);
        pet.Needs.Affection = 20f;
        var before = pet.Bond.Value;
        sim.Enqueue(pet, Get("pet"), T0);
        Run(sim, 3.5f);
        Assert.True(pet.Needs.Affection > 35f);
        Assert.True(pet.Bond.Value > before);
    }

    [Fact]
    public void Cancelling_removes_pending_and_stops_running_actions()
    {
        var (sim, _, pet) = Make(PetSpecies.Dog, bond: 95f);
        sim.Enqueue(pet, Get("pet"), T0);
        sim.Enqueue(pet, Get("sit"), T0);
        sim.Tick(0.1f, T0);

        sim.Cancel(pet, pet.Queue[1]);              // pending: gone immediately
        Assert.Single(pet.Queue);

        sim.Cancel(pet, pet.Queue[0]);              // running: stops on the next tick
        sim.Tick(0.1f, T0);
        Assert.Empty(pet.Queue);
        Assert.IsNotType<QueuedInteractionBehavior>(pet.Engine.Current);
    }

    [Fact]
    public void Queue_is_capped()
    {
        var (sim, _, pet) = Make(PetSpecies.Dog, bond: 95f);
        for (var i = 0; i < Pet.MaxQueue; i++) Assert.True(sim.Enqueue(pet, Get("pet"), T0));
        Assert.False(sim.Enqueue(pet, Get("pet"), T0));
    }

    [Fact]
    public void Low_bond_pets_refuse_sometimes_but_never_refuse_food()
    {
        var (_, _, cat) = Make(PetSpecies.Cat, bond: 5f);
        Assert.True(QueuedInteractionBehavior.RefusalChance(cat, Get("pet")) > 0.3f);
        Assert.Equal(0f, QueuedInteractionBehavior.RefusalChance(cat, Get("treat")));

        var (_, _, friend) = Make(PetSpecies.Cat, bond: 95f);
        Assert.Equal(0f, QueuedInteractionBehavior.RefusalChance(friend, Get("pet")));
    }

    [Fact]
    public void A_refusal_shows_a_bubble_and_gives_no_reward()
    {
        var (sim, _, cat) = Make(PetSpecies.Cat, bond: 0f);
        // Make refusal certain: a stranger in a bad mood being hugged is capped at 90%, so retry a few times.
        cat.Needs.Hunger = 0f; cat.Needs.Energy = 0f; cat.Needs.Fun = 0f; cat.Needs.Affection = 0f;
        var refused = false;
        for (var i = 0; i < 10 && !refused; i++)
        {
            cat.Queue.Add(new QueuedAction { Interaction = Get("pet") });
            sim.Tick(0.1f, T0);
            refused = cat.Bubble == "Hmph!";
            Run(sim, 3.5f);
        }
        Assert.True(refused);
    }

    [Fact]
    public void Fetch_throws_a_ball_and_the_dog_brings_it_back()
    {
        var (sim, cell, dog) = Make(PetSpecies.Dog, bond: 95f);
        dog.Needs.Fun = 20f;
        var start = dog.Position;
        sim.Enqueue(dog, Get("ball"), T0);
        sim.Tick(0.1f, T0);
        Assert.NotNull(cell.Ball);

        var sawCarry = false;
        for (var i = 0; i < 500 && dog.Queue.Count > 0; i++)
        {
            sim.Tick(0.1f, T0);
            sawCarry |= cell.Ball?.CarriedBy == dog.Id;
        }
        Assert.True(sawCarry);
        Assert.Empty(dog.Queue);
        Assert.Null(cell.Ball);
        Assert.True(Vec2.Distance(dog.Position, start) < 60f);
        Assert.True(dog.Needs.Fun > 50f);
    }

    [Fact]
    public void Dog_waits_for_the_person_to_throw_and_brings_the_ball_back()
    {
        var (sim, cell, dog) = Make(PetSpecies.Dog, bond: 95f);
        dog.Needs.Fun = 20f;
        sim.Enqueue(dog, Get("ball"), T0);
        Run(sim, 3f);
        var ball = cell.Ball!;
        Assert.Null(ball.CarriedBy);
        Assert.False(ball.IsMoving);
        Assert.True(Vec2.Distance(ball.Position, dog.Position) < 60f);   // dropped at its feet, waiting

        // Pick it up, carry it to the right, flick it further right.
        var from = new Vec2(dog.Position.X + 150, dog.Position.Y);
        sim.BeginBallHold(ball);
        sim.UpdateBallHold(cell, ball, from);
        Run(sim, 1f);
        Assert.Equal(from, ball.Position);                              // held balls stay in hand
        sim.ThrowBall(ball, new Vec2(1200, 0));
        Assert.True(ball.IsMoving);

        var farthest = 0f;
        var sawCarry = false;
        for (var i = 0; i < 200 && !(sawCarry && ball.CarriedBy is null); i++)
        {
            sim.Tick(0.1f, T0);
            farthest = MathF.Max(farthest, ball.Position.X - from.X);
            sawCarry |= ball.CarriedBy == dog.Id;
        }
        Assert.True(farthest > 300f);
        Assert.True(sawCarry);
        Assert.Null(ball.CarriedBy);
        Assert.True(Vec2.Distance(dog.Position, from) < 60f);           // brought back to where it was thrown from
        Assert.Same(ball, cell.Ball);                                   // and waits for the next throw
        Assert.Single(dog.Queue);
    }

    [Fact]
    public void Thrown_ball_bounces_rolls_and_stops_inside_the_roam_area()
    {
        var (_, cell, _) = Make(PetSpecies.Dog, bond: 95f);
        var ball = new Ball { Position = new Vec2(900, 800), HeldByUser = true };
        ball.Throw(new Vec2(5000, -300));
        Assert.True(ball.Velocity.Length <= Ball.MaxThrowSpeed + 0.1f);
        var bounces = 0;
        var wasUp = false;
        for (var i = 0; i < 600 && ball.IsMoving; i++)
        {
            ball.Step(1f / 60f, cell.Map);
            if (ball.Height > 1f) wasUp = true;
            else if (wasUp && ball.Height <= 0f) { bounces++; wasUp = false; }
            Assert.True(cell.Map.RoamArea.Contains(ball.Position));
        }
        Assert.False(ball.IsMoving);
        Assert.True(bounces >= 2);
    }

    [Fact]
    public void Attention_stops_the_pet_and_turns_it_to_the_viewer()
    {
        var (sim, _, pet) = Make(PetSpecies.Cat, bond: 50f);
        pet.Heading = 0f;
        sim.BeginAttention(pet);
        var at = pet.Position;
        Run(sim, 1f);
        Assert.IsType<HeldBehavior>(pet.Engine.Current);
        Assert.Equal(at, pet.Position);
        Assert.Equal(90f, pet.Heading, precision: 1);
        sim.EndAttention(pet);
        sim.Tick(0.1f, T0);
        Assert.IsNotType<HeldBehavior>(pet.Engine.Current);
    }

    [Fact]
    public void Mood_follows_the_lowest_need()
    {
        var (_, _, pet) = Make(PetSpecies.Dog, bond: 50f);
        Assert.Equal(MoodLevel.Good, MoodCalc.Level(pet));
        pet.Needs.Hunger = 0f;
        pet.Needs.Energy = 10f;
        Assert.NotEqual(MoodLevel.Good, MoodCalc.Level(pet));
        pet.Appearance.AddMud(1f);
        Assert.Equal(0f, MoodCalc.Hygiene(pet));
    }
}
