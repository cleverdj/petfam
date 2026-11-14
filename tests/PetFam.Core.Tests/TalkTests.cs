using PetFam.Core.Geometry;
using PetFam.Core.Pets;
using PetFam.Core.Platform;
using PetFam.Core.Talk;
using PetFam.Core.World;

namespace PetFam.Core.Tests;

public class TalkTests
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

    private sealed class FakeWriter : IDialogueWriter
    {
        public string Answer = """
            Sure! {"lines":[{"who":"A","text":"Did the human fill the bowl?","act":"sit"},
            {"who":"B","text":"Not yet. Grr.","act":"hiss"},{"who":"A","text":"Let's go stare at them."}],"tone":"warm"}
            """;
        public readonly List<string> Prompts = new();
        public string Name => "Fake";

        public Task<string?> WriteAsync(string system, string prompt, CancellationToken ct)
        {
            Prompts.Add(prompt);
            return Task.FromResult<string?>(Answer);
        }
    }

    private static (Simulation Sim, Cell Cell) Make()
    {
        var sim = new Simulation(new FakePlatform(), new Random(3));
        var bounds = new RectI(0, 0, 1920, 1080);
        var working = new RectI(0, 0, 1920, 1040);
        var cell = sim.World.Upsert(new Cell
        {
            Id = "d:m", DesktopId = "d", MonitorId = "m", Bounds = bounds, WorkingArea = working,
            Map = HabitatMap.Flat(bounds, working),
        });
        return (sim, cell);
    }

    /// <summary>The writer runs on the thread pool; a test's simulated seconds pass far faster than that.</summary>
    private static void WaitForWriter(Simulation sim)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (sim.Talk.Waiting && sim.Talk.AnswerPending && DateTime.UtcNow < until) Thread.Sleep(2);
    }

    [Fact]
    public void Each_pet_gets_its_own_persona_in_the_prompt()
    {
        var (sim, cell) = Make();
        var cat = sim.AddPet(Breed.All.First(b => b.Species == PetSpecies.Cat), "Momo", cell, T0);
        var dog = sim.AddPet(Breed.All.First(b => b.Species == PetSpecies.Dog), "Peanut", cell, T0);
        cat.Needs.Hunger = 10f;

        var prompt = PetTalk.Prompt(cat, dog, sim.Social, WeatherNow.Clear, DayPhase.Day, new[] { "Momo: hi" });

        Assert.Contains("Momo, a baby " + cat.Breed.Name.En + " (cat)", prompt);
        Assert.Contains("Peanut, a baby " + dog.Breed.Name.En + " (dog)", prompt);
        Assert.Contains(cat.Breed.Persona.En, prompt);
        Assert.Contains(dog.Breed.Persona.En, prompt);
        Assert.Contains("hungry", prompt);
        Assert.Contains("Last time they talked: Momo: hi", prompt);
    }

    [Fact]
    public void The_answer_is_read_even_with_text_around_it_and_odd_acts_are_dropped()
    {
        var (sim, cell) = Make();
        var cat = sim.AddPet(PetSpecies.Cat, "Momo", cell, T0);
        var dog = sim.AddPet(PetSpecies.Dog, "Peanut", cell, T0);

        var d = PetTalk.Parse(new FakeWriter().Answer, cat, dog);

        Assert.NotNull(d);
        Assert.Equal(3, d!.Lines.Count);
        Assert.Same(cat, d.Lines[0].Speaker);
        Assert.Equal("sit", d.Lines[0].Act);
        Assert.Same(dog, d.Lines[1].Speaker);
        Assert.Null(d.Lines[1].Act);                                        // dogs do not hiss
        Assert.Equal(1, d.Tone);
        Assert.Null(PetTalk.Parse("I can't do that", cat, dog));
        Assert.Null(PetTalk.Parse("{\"lines\":[{\"who\":\"A\",\"text\":\"hi\"}]}", cat, dog));   // one line is not a talk
    }

    [Fact]
    public void Stage_directions_are_left_out_of_the_bubble()
    {
        Assert.Equal("조용히 해요, 프랭크.", PetTalk.StripDirections("(꼬리를 살짝 들며) 조용히 해요, 프랭크."));
        Assert.Equal("Hi there!", PetTalk.StripDirections("*wags tail* Hi [happily] there!"));
    }

    [Fact]
    public void Two_pets_nearby_meet_and_say_the_lines_in_turn_and_grow_closer()
    {
        var (sim, cell) = Make();
        var cat = sim.AddPet(PetSpecies.Cat, "Momo", cell, T0);
        var dog = sim.AddPet(PetSpecies.Dog, "Peanut", cell, T0);
        cat.Position = new Vec2(800, 900);
        dog.Position = new Vec2(1000, 900);
        foreach (var p in sim.Pets) { p.Needs.Energy = 90f; p.Needs.Hunger = 90f; p.Needs.Fun = 90f; }
        var writer = new FakeWriter();
        sim.Talk.Writer = writer;
        sim.Talk.RequestNow();
        var before = sim.Social.Friendship(cat, dog);

        var said = new List<(string Name, string Text)>();
        for (var i = 0; i < 900 && said.Count < 3; i++)
        {
            sim.Tick(0.1f, T0.AddSeconds(i * 0.1));
            WaitForWriter(sim);
            foreach (var p in sim.Pets)
                if (p.Bubble is { } b && (said.Count == 0 || said[^1].Text != b) && p.Session is TalkSession)
                    said.Add((p.Name, b));
        }

        Assert.Single(writer.Prompts);
        Assert.Equal(3, said.Count);
        Assert.Equal("Did the human fill the bowl?", said[0].Text);
        Assert.NotEqual(said[0].Name, said[1].Name);                        // they take turns
        for (var i = 0; i < 100; i++) sim.Tick(0.1f, T0.AddSeconds(90 + i * 0.1));
        Assert.Null(cat.Session);
        Assert.True(sim.Social.Friendship(cat, dog) > before);
    }

    [Fact]
    public void Without_an_AI_or_with_talk_turned_off_nobody_is_asked()
    {
        var (sim, cell) = Make();
        sim.AddPet(PetSpecies.Cat, "Momo", cell, T0);
        sim.AddPet(PetSpecies.Dog, "Peanut", cell, T0);
        var writer = new FakeWriter();
        sim.Talk.Writer = writer;
        sim.Talk.Enabled = false;
        sim.Talk.RequestNow();
        for (var i = 0; i < 100; i++) sim.Tick(0.1f, T0);
        Assert.Empty(writer.Prompts);
    }

    [Fact]
    public void A_broken_answer_is_logged_and_tried_again_later_not_every_frame()
    {
        var (sim, cell) = Make();
        sim.AddPet(PetSpecies.Cat, "Momo", cell, T0).Position = new Vec2(800, 900);
        sim.AddPet(PetSpecies.Dog, "Peanut", cell, T0).Position = new Vec2(1000, 900);
        var log = new List<string>();
        sim.Debug.Log = log.Add;
        var writer = new FakeWriter { Answer = "nope" };
        sim.Talk.Writer = writer;
        sim.Talk.RequestNow();
        for (var i = 0; i < 300; i++) { sim.Tick(0.1f, T0); WaitForWriter(sim); }
        Assert.Single(writer.Prompts);
        Assert.Contains(log, l => l.StartsWith("Talk: no usable answer"));
    }
}
