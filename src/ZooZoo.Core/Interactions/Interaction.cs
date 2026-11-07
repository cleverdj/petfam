using ZooZoo.Core.Behavior;
using ZooZoo.Core.Pets;
using ZooZoo.Core.World;

namespace ZooZoo.Core.Interactions;

/// <summary>Whether an interaction shows up in the pie menu, and if so whether it can be picked.</summary>
public readonly record struct Availability(bool Visible, bool Enabled, string? Reason)
{
    public static Availability Ok => new(true, true, null);
    public static Availability Hidden => new(false, false, null);
    public static Availability Disabled(string reason) => new(true, false, reason);
}

/// <summary>
/// One thing the user can ask a pet to do, Sims style: it appears in the pie menu, goes into the pet's
/// action queue, may be refused, and runs as a small script (runner) when its turn comes.
/// </summary>
public abstract class Interaction
{
    public abstract string Id { get; }
    public abstract string Label { get; }
    public abstract string Icon { get; }

    /// <summary>Lowest bond stage that unlocks it.</summary>
    public virtual BondStage MinBond => BondStage.Stranger;

    /// <summary>Extra refusal chance on top of the bond/mood based one (can be negative).</summary>
    public virtual float RefusalBias => 0f;

    /// <summary>Some things are never refused (food).</summary>
    public virtual bool CanRefuse => true;

    public Availability Check(Pet pet, Cell cell, DateTime nowUtc)
    {
        var own = CheckOwn(pet, cell, nowUtc);
        if (!own.Visible) return own;
        if (pet.Bond.Stage < MinBond) return Availability.Disabled($"유대감 '{BondNames.Of(MinBond)}' 이상 필요");
        return own;
    }

    protected virtual Availability CheckOwn(Pet pet, Cell cell, DateTime nowUtc) => Availability.Ok;

    public abstract InteractionRunner CreateRunner();
}

/// <summary>Runs one queued interaction. Lives inside QueuedInteractionBehavior.</summary>
public abstract class InteractionRunner
{
    /// <summary>0..1, shown on the queue chip.</summary>
    public float Progress { get; protected set; }

    public abstract void Start(BehaviorContext ctx);

    /// <summary>Advance; return true when finished.</summary>
    public abstract bool Update(BehaviorContext ctx, float dt);

    /// <summary>completed = ran to the end (apply rewards); false = cancelled or interrupted.</summary>
    public virtual void Stop(BehaviorContext ctx, bool completed) { }
}

public enum QueueStatus
{
    Pending,
    Running,
}

public sealed class QueuedAction
{
    public Guid Id { get; } = Guid.NewGuid();
    public required Interaction Interaction { get; init; }
    public QueueStatus Status { get; set; } = QueueStatus.Pending;
    public float Progress { get; set; }
    public bool CancelRequested { get; set; }
}

public static class BondNames
{
    public static string Of(BondStage s) => s switch
    {
        BondStage.Stranger => "낯섦",
        BondStage.Wary => "경계",
        BondStage.Curious => "호기심",
        BondStage.Familiar => "친숙",
        BondStage.Trusting => "신뢰",
        _ => "가족",
    };
}
