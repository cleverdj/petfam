namespace PetFam.Core.Behavior;

/// <summary>
/// A behavior scores itself from needs × context × personality; the engine runs the best one (plan §5.1).
/// One instance per pet: behaviors keep their own timers and cooldowns.
/// </summary>
public abstract class Behavior
{
    public abstract string Name { get; }

    /// <summary>Can the engine switch away from this behavior mid-way when something scores much higher?</summary>
    public virtual bool Interruptible => true;

    public abstract float Score(BehaviorContext ctx);

    public virtual void Start(BehaviorContext ctx) { }

    /// <summary>Advance by dt seconds. Return true when finished.</summary>
    public abstract bool Update(BehaviorContext ctx, float dt);

    public virtual void Stop(BehaviorContext ctx, bool interrupted) { }
}
