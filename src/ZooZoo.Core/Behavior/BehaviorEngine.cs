using ZooZoo.Core.Behavior.Behaviors;
using ZooZoo.Core.Pets;

namespace ZooZoo.Core.Behavior;

/// <summary>
/// Utility selection with hysteresis: re-evaluates every few seconds and only switches when another
/// behavior is clearly better, so pets do not flicker between actions. A user hold (carry / pet)
/// pre-empts everything immediately.
/// </summary>
public sealed class BehaviorEngine
{
    public const float ReevaluateInterval = 3f;
    public const float SwitchMargin = 0.25f;

    private readonly List<Behavior> _behaviors;
    private float _sinceReevaluate;

    public Behavior? Current { get; private set; }

    public IReadOnlyList<Behavior> Behaviors => _behaviors;

    public BehaviorEngine(IEnumerable<Behavior> behaviors)
    {
        _behaviors = behaviors.ToList();
    }

    public static BehaviorEngine CreateDefault(PetSpecies species)
    {
        var list = new List<Behavior>
        {
            new HeldBehavior(),
            new QueuedInteractionBehavior(),
            new SocialBehavior(),
            new StretchBehavior(),
            new EatBehavior(),
            new IdleBehavior(),
            new WanderBehavior(),
            new SitBehavior(),
            new LieBehavior(),
            new SleepBehavior(),
            new PeekBehavior(),
        };
        if (species == PetSpecies.Cat)
        {
            list.Add(new GroomBehavior());
            list.Add(new HuntCursorBehavior());
        }
        if (species == PetSpecies.Dog)
        {
            list.Add(new DigBehavior());
            list.Add(new SwimBehavior());
        }
        return new BehaviorEngine(list);
    }

    public void Update(BehaviorContext ctx, float dt)
    {
        var pet = ctx.Pet;

        // A mouse hold wins immediately, even over non-interruptible behaviors.
        if (HeldBehavior.IsTouched(pet) && Current is not HeldBehavior)
            Switch(ctx, _behaviors.OfType<HeldBehavior>().First(), interrupted: true);

        // A user command starts right away (Sims: the queue head interrupts autonomy).
        else if (pet.Queue.Count > 0 && Current is not HeldBehavior and not QueuedInteractionBehavior)
            Switch(ctx, _behaviors.OfType<QueuedInteractionBehavior>().First(), interrupted: true);

        // Another pet started an encounter with this one: join it.
        else if (pet.Session is { Done: false } && Current is not HeldBehavior and not QueuedInteractionBehavior and not SocialBehavior)
            Switch(ctx, _behaviors.OfType<SocialBehavior>().First(), interrupted: true);

        if (Current is null)
            Switch(ctx, Best(ctx), interrupted: false);

        var finished = Current!.Update(ctx, dt);
        _sinceReevaluate += dt;

        if (finished)
        {
            // Stop first: a finishing behavior may raise flags (e.g. JustWokeUp) that decide what comes next.
            var previous = Current;
            previous.Stop(ctx, interrupted: false);
            Current = null;
            // Priority behaviors (queue, holds) may follow themselves; ordinary ones should not repeat back to back.
            var next = Best(ctx, exclude: previous.Score(ctx) >= 5f ? null : previous);
            Current = next;
            _sinceReevaluate = 0f;
            next.Start(ctx);
            return;
        }

        if (_sinceReevaluate < ReevaluateInterval || !Current.Interruptible)
            return;

        _sinceReevaluate = 0f;
        var best = Best(ctx);
        if (!ReferenceEquals(best, Current) && best.Score(ctx) > Current.Score(ctx) + SwitchMargin)
            Switch(ctx, best, interrupted: true);
    }

    public void ForceStop(BehaviorContext ctx)
    {
        Current?.Stop(ctx, interrupted: true);
        Current = null;
    }

    private Behavior Best(BehaviorContext ctx, Behavior? exclude = null)
    {
        Behavior? best = null;
        var bestScore = float.NegativeInfinity;
        foreach (var b in _behaviors)
        {
            if (ReferenceEquals(b, exclude) && _behaviors.Count > 1) continue;
            var s = b.Score(ctx);
            if (s > bestScore)
            {
                bestScore = s;
                best = b;
            }
        }
        return best ?? _behaviors[0];
    }

    private void Switch(BehaviorContext ctx, Behavior next, bool interrupted)
    {
        Current?.Stop(ctx, interrupted);
        Current = next;
        _sinceReevaluate = 0f;
        next.Start(ctx);
    }
}
