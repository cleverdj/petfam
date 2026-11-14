using PetFam.Core.Pets;
using PetFam.Core.Social;

namespace PetFam.Core.Talk;

/// <summary>
/// Now and then two pets that are near each other stop for a chat, each in its own persona, with lines the user's
/// AI writes. The AI takes a few seconds, so the lines are asked for first and the pets meet once they arrive; if
/// the two have wandered off into something else by then, the talk is dropped. One conversation at a time, a few
/// minutes apart, so it stays special (and light on the user's AI usage). Without an AI nothing happens.
/// </summary>
public sealed class TalkDirector
{
    /// <summary>Give up on an answer after this long.</summary>
    public const float Timeout = 90f;

    /// <summary>A written conversation keeps this long while the two are busy; then it is dropped.</summary>
    public const float KeepReadyFor = 60f;

    /// <summary>After this many failures in a row, wait <see cref="BackOff"/> before asking again.</summary>
    public const int FailuresBeforeBackOff = 3;
    public const float BackOff = 900f;

    public IDialogueWriter? Writer { get; set; }
    public bool Enabled { get; set; } = true;

    /// <summary>Seconds between conversations (picked at random in this range).</summary>
    public float MinGap { get; set; } = 240f;
    public float MaxGap { get; set; } = 480f;

    private sealed class Pending
    {
        public required Pet A;
        public required Pet B;
        public required Task<string?> Task;
        public required CancellationTokenSource Cts;
        public float Age;
    }

    private Pending? _pending;
    private Dialogue? _ready;
    private float _readyAge;
    private float _wait = 60f;
    private bool _force;
    private int _failures;
    private readonly Dictionary<(Guid, Guid), List<string>> _memory = new();

    public bool Waiting => _pending is not null;

    /// <summary>The AI is still writing (its task has not finished).</summary>
    public bool AnswerPending => _pending is { } p && !p.Task.IsCompleted;

    /// <summary>Tray test: the next free pair talks as soon as possible, near each other or not.</summary>
    public void RequestNow()
    {
        _force = true;
        _wait = 0f;
        _failures = 0;
    }

    /// <summary>What the two said last time (for continuity, and so the AI doesn't repeat itself).</summary>
    public IReadOnlyList<string> LastTime(Pet a, Pet b) => _memory.TryGetValue(Key(a, b), out var l) ? l : Array.Empty<string>();

    private static (Guid, Guid) Key(Pet a, Pet b) => a.Id.CompareTo(b.Id) < 0 ? (a.Id, b.Id) : (b.Id, a.Id);

    internal void Tick(Simulation sim, Random rng, float dt)
    {
        if (Writer is null || !Enabled)
        {
            Cancel();
            return;
        }

        if (_ready is { } ready)
        {
            _readyAge += dt;
            if (TryStart(sim, ready)) _ready = null;
            else if (_readyAge > KeepReadyFor)
            {
                Log(sim, $"Talk: {ready.A.Name} & {ready.B.Name} got busy, dropped the conversation");
                _ready = null;
            }
            return;
        }

        if (_pending is { } p)
        {
            p.Age += dt;
            if (p.Task.IsCompleted)
            {
                _pending = null;
                var raw = p.Task.Status == TaskStatus.RanToCompletion ? p.Task.Result : null;
                var dialogue = PetTalk.Parse(raw, p.A, p.B);
                if (dialogue is null)
                {
                    Failed(sim, $"no usable answer from {Writer.Name} after {p.Age:0.0}s" +
                                (p.Task.Exception is { } ex ? ": " + ex.GetBaseException().Message : raw is null ? "" : ": " + Short(raw)));
                    return;
                }
                _failures = 0;
                Log(sim, $"Talk: {p.A.Name} & {p.B.Name} ({Writer.Name}, {p.Age:0.0}s, tone {dialogue.Tone:+0;-0;0}): " +
                         string.Join(" / ", dialogue.Lines.Select(l => $"{l.Speaker.Name}: {l.Text}")));
                var memory = dialogue.Lines.Select(l => $"{l.Speaker.Name}: {l.Text}").TakeLast(4).ToList();
                _memory[Key(p.A, p.B)] = memory;
                _ready = dialogue;
                _readyAge = 0f;
            }
            else if (p.Age > Timeout)
            {
                p.Cts.Cancel();
                _pending = null;
                Failed(sim, $"{Writer.Name} took too long");
            }
            return;
        }

        _wait -= dt;
        if (_wait > 0f) return;
        _wait = MinGap + (float)rng.NextDouble() * (MaxGap - MinGap);

        var pair = PickPair(sim, rng, _force);
        if (pair is not { } pr)
        {
            _wait = _force ? 3f : 10f;                       // nobody free together: look again soon
            return;
        }
        _force = false;
        Ask(sim, pr.A, pr.B);
    }

    private void Ask(Simulation sim, Pet a, Pet b)
    {
        var writer = Writer!;
        // The prompt is built here, on the simulation thread; only the finished text goes to the other thread.
        var prompt = PetTalk.Prompt(a, b, sim.Social, sim.Weather, sim.Phase, LastTime(a, b));
        var cts = new CancellationTokenSource();
        var task = Task.Run(() => writer.WriteAsync(PetTalk.System, prompt, cts.Token));
        _pending = new Pending { A = a, B = b, Task = task, Cts = cts };
        Log(sim, $"Talk: asking {writer.Name} for {a.Name} & {b.Name}");
    }

    private void Failed(Simulation sim, string why)
    {
        _failures++;
        _wait = _failures >= FailuresBeforeBackOff ? BackOff : 90f;
        Log(sim, $"Talk: {why}{(_failures >= FailuresBeforeBackOff ? $" ({_failures} in a row, resting {BackOff / 60f:0} min)" : "")}");
    }

    private static bool Free(Pet p) =>
        SocialDirector.Available(p) && p.Needs.Energy > 15f && p.Bubble is null;

    private static (Pet A, Pet B)? PickPair(Simulation sim, Random rng, bool anyDistance)
    {
        var free = sim.Pets.Where(Free).OrderBy(_ => rng.Next()).ToList();
        foreach (var a in free)
        foreach (var b in free)
        {
            if (ReferenceEquals(a, b) || a.CellId != b.CellId) continue;
            if (!anyDistance && !CloseEnough(a, b, sim.World.Find(a.CellId))) continue;
            return (a, b);
        }
        return null;
    }

    /// <summary>One walks over to the other to talk: a bit farther than play starts (see SocialDirector.Near).</summary>
    public const float TalkDistance = 900f;

    private static bool CloseEnough(Pet a, Pet b, World.Cell? cell)
    {
        var depth = cell is null ? 1f : (cell.Map.DepthScale(a.Position.Y) + cell.Map.DepthScale(b.Position.Y)) / 2f;
        return Geometry.Vec2.Distance(a.Position, b.Position) <= TalkDistance * depth;
    }

    private static bool TryStart(Simulation sim, Dialogue d)
    {
        if (!sim.Pets.Contains(d.A) || !sim.Pets.Contains(d.B)) return false;
        if (!SocialDirector.Available(d.A) || !SocialDirector.Available(d.B) || d.A.CellId != d.B.CellId) return false;
        sim.Director.Start(new TalkSession(d));
        var said = d.Lines.Select(l => $"{l.Speaker.Name}: {l.Text}").ToList();
        sim.Journal.Talked(d.A, said, sim.Now);
        sim.Journal.Talked(d.B, said, sim.Now);
        Log(sim, $"Talk: {d.A.Name} walks over to {d.B.Name} to talk");
        return true;
    }

    private void Cancel()
    {
        _pending?.Cts.Cancel();
        _pending = null;
        _ready = null;
    }

    private static string Short(string s) => s.Length <= 120 ? s.ReplaceLineEndings(" ") : s[..120].ReplaceLineEndings(" ") + "…";

    private static void Log(Simulation sim, string line) => sim.Debug.Log?.Invoke(line);
}
