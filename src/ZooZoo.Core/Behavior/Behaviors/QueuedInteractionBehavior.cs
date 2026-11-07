using ZooZoo.Core.Interactions;
using ZooZoo.Core.Pets;

namespace ZooZoo.Core.Behavior.Behaviors;

/// <summary>
/// Runs the head of the pet's action queue (Sims style). User commands outrank autonomy; only a mouse hold
/// (HeldBehavior) beats them. Before starting, the pet may refuse depending on bond and mood.
/// </summary>
public sealed class QueuedInteractionBehavior : Behavior
{
    public const float Priority = 8f;

    private QueuedAction? _action;
    private InteractionRunner? _runner;

    public override string Name => _action is null ? "queued" : "do:" + _action.Interaction.Id;

    public override bool Interruptible => false;

    public QueuedAction? Active => _action;

    public override float Score(BehaviorContext ctx) => ctx.Pet.Queue.Count > 0 ? Priority : 0f;

    public override void Start(BehaviorContext ctx)
    {
        var pet = ctx.Pet;
        _action = pet.Queue.Count > 0 ? pet.Queue[0] : null;
        if (_action is null) return;

        _action.Status = QueueStatus.Running;
        _action.Progress = 0f;
        _runner = ShouldRefuse(ctx, _action.Interaction) ? new RefuseRunner() : _action.Interaction.CreateRunner();
        _runner.Start(ctx);
    }

    public override bool Update(BehaviorContext ctx, float dt)
    {
        if (_action is null || _runner is null) return true;
        if (_action.CancelRequested)
        {
            Finish(ctx, completed: false);
            return true;
        }

        var done = _runner.Update(ctx, dt);
        _action.Progress = _runner.Progress;
        if (done) Finish(ctx, completed: true);
        return done;
    }

    public override void Stop(BehaviorContext ctx, bool interrupted)
    {
        // Interrupted by a mouse hold: put the action back so it restarts afterwards.
        if (_action is null || _runner is null) return;
        _runner.Stop(ctx, completed: false);
        _action.Status = QueueStatus.Pending;
        _action.Progress = 0f;
        _action = null;
        _runner = null;
    }

    private void Finish(BehaviorContext ctx, bool completed)
    {
        _runner!.Stop(ctx, completed && _runner is not RefuseRunner);
        ctx.Pet.Queue.Remove(_action!);
        _action = null;
        _runner = null;
    }

    /// <summary>Refusal chance: low bond and a bad mood make pets say no. Food and rest are never refused.</summary>
    public static float RefusalChance(Pet pet, Interaction interaction)
    {
        if (!interaction.CanRefuse) return 0f;
        var chance = pet.Bond.Stage switch
        {
            BondStage.Stranger => 0.6f,
            BondStage.Wary => 0.35f,
            BondStage.Curious => 0.15f,
            BondStage.Familiar => 0.05f,
            _ => 0f,
        };
        if (MoodCalc.Level(pet) == MoodLevel.Bad) chance += 0.2f;
        if (pet.Species == PetSpecies.Cat && interaction.Id == "hug" && pet.Bond.Stage < BondStage.Trusting) chance += 0.2f;
        chance += interaction.RefusalBias;
        return System.Math.Clamp(chance, 0f, 0.9f);
    }

    private static bool ShouldRefuse(BehaviorContext ctx, Interaction interaction) =>
        ctx.Rng.NextDouble() < RefusalChance(ctx.Pet, interaction);
}
