using ZooZoo.Core.Behavior.Behaviors;
using ZooZoo.Core.Pets;
using ZooZoo.Core.World;

namespace ZooZoo.Core.Social;

/// <summary>
/// Decides when pets start playing with each other and runs the active encounters. Checks every few seconds;
/// the more bored, playful and friendly a pet is, the likelier it starts something.
/// </summary>
public sealed class SocialDirector
{
    public const float CheckInterval = 4f;

    private readonly List<SocialSession> _active = new();
    private float _sinceCheck;

    public IReadOnlyList<SocialSession> Active => _active;

    /// <summary>Is the pet free for a social encounter (not held, busy with a command, asleep, eating, swimming...)?</summary>
    public static bool Available(Pet pet) =>
        pet.Session is null && pet.Hold == HoldState.None && pet.Queue.Count == 0 && !pet.Hidden &&
        pet.Layer == PetLayer.Backyard &&
        pet.Engine.Current is null or IdleBehavior or WanderBehavior or SitBehavior or GroomBehavior or LieBehavior { Approaching: false };

    public void Tick(IReadOnlyList<Pet> pets, Func<Pet, Cell?> cellOf, SocialState social, Random rng, DateTime nowUtc, float dt)
    {
        _sinceCheck += dt;
        if (_sinceCheck >= CheckInterval)
        {
            _sinceCheck = 0f;
            TryStart(pets, cellOf, social, rng);
        }

        foreach (var s in _active.ToList())
        {
            var cell = cellOf(s.Initiator);
            if (s.Done || cell is null)
            {
                s.Abort();
                _active.Remove(s);
                continue;
            }
            s.Update(new SocialContext { Cell = cell, Social = social, Rng = rng, NowUtc = nowUtc }, dt);
            if (s.Done) _active.Remove(s);
        }
    }

    private void TryStart(IReadOnlyList<Pet> pets, Func<Pet, Cell?> cellOf, SocialState social, Random rng)
    {
        foreach (var a in pets.OrderBy(_ => rng.Next()))
        {
            if (!Available(a) || a.Needs.Energy < 30f) continue;
            var funDeficit = (100f - a.Needs.Fun) / 100f;
            foreach (var b in pets.OrderBy(_ => rng.Next()))
            {
                if (ReferenceEquals(a, b) || !Available(b) || a.CellId != b.CellId) continue;
                var chance = 0.06f + funDeficit * 0.25f + a.Personality.Playfulness * 0.1f + social.Friendship(a, b) * 0.001f;
                if (rng.NextDouble() >= chance) continue;
                Start(Choose(a, b, social, rng));
                return;
            }
        }
    }

    /// <summary>Picks the kind of encounter for this pair (dog bows, cats stalk or groom friends).</summary>
    public static SocialSession Choose(Pet a, Pet b, SocialState social, Random rng)
    {
        if (a.Species == PetSpecies.Dog) return new PlayBowSession(a, b);
        if (b.Species == PetSpecies.Cat && social.Friendship(a, b) >= 60f && rng.NextDouble() < 0.35)
        {
            var groomer = social.DominanceOf(a, b) >= 0f ? a : b;
            return new GroomEachOtherSession(groomer, ReferenceEquals(groomer, a) ? b : a);
        }
        return new StalkPounceSession(a, b);
    }

    public void Start(SocialSession session)
    {
        session.Begin();
        _active.Add(session);
    }
}
