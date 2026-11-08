using PetFam.Core.Pets;

namespace PetFam.Core.Behavior.Behaviors;

/// <summary>Placeholder while a two-pet encounter (SocialSession) drives the pet. Aborts the encounter if interrupted.</summary>
public sealed class SocialBehavior : Behavior
{
    public const float Priority = 6f;

    public override string Name => "social:" + (_session?.Name ?? "-");

    public override bool Interruptible => false;

    private Social.SocialSession? _session;

    public override float Score(BehaviorContext ctx) => ctx.Pet.Session is { Done: false } ? Priority : 0f;

    public override void Start(BehaviorContext ctx) => _session = ctx.Pet.Session;

    public override bool Update(BehaviorContext ctx, float dt) => ctx.Pet.Session is null || ctx.Pet.Session.Done;

    public override void Stop(BehaviorContext ctx, bool interrupted)
    {
        if (interrupted && _session is { Done: false } s) s.Abort();
        _session = null;
    }
}
