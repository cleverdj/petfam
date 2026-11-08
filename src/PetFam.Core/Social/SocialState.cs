using PetFam.Core.Pets;

namespace PetFam.Core.Social;

/// <summary>How two pets get on: friendship 0..100 and dominance -1..1 (positive = A outranks B).</summary>
public sealed class Relation
{
    public required Guid A { get; init; }
    public required Guid B { get; init; }
    public float Friendship { get; set; } = 40f;
    public float Dominance { get; set; }
}

/// <summary>Relations between every pair of pets, and the pecking order derived from them.</summary>
public sealed class SocialState
{
    private readonly Dictionary<(Guid, Guid), Relation> _relations = new();

    public IEnumerable<Relation> All => _relations.Values;

    private static (Guid, Guid) Key(Guid a, Guid b) => a.CompareTo(b) < 0 ? (a, b) : (b, a);

    public Relation Get(Pet a, Pet b)
    {
        var key = Key(a.Id, b.Id);
        if (!_relations.TryGetValue(key, out var r))
        {
            r = new Relation { A = key.Item1, B = key.Item2 };
            _relations[key] = r;
        }
        return r;
    }

    public void Restore(Relation r) => _relations[Key(r.A, r.B)] = r;

    /// <summary>Drops every relation of a pet that left.</summary>
    public void Forget(Guid id)
    {
        foreach (var key in _relations.Keys.Where(k => k.Item1 == id || k.Item2 == id).ToList())
            _relations.Remove(key);
    }

    public float Friendship(Pet a, Pet b) => Get(a, b).Friendship;

    /// <summary>How much <paramref name="a"/> outranks <paramref name="b"/> (-1..1).</summary>
    public float DominanceOf(Pet a, Pet b)
    {
        var r = Get(a, b);
        return r.A == a.Id ? r.Dominance : -r.Dominance;
    }

    public void AddFriendship(Pet a, Pet b, float amount)
    {
        var r = Get(a, b);
        r.Friendship = System.Math.Clamp(r.Friendship + amount, 0f, 100f);
    }

    public void ShiftDominance(Pet winner, Pet loser, float amount)
    {
        var r = Get(winner, loser);
        r.Dominance = System.Math.Clamp(r.Dominance + (r.A == winner.Id ? amount : -amount), -1f, 1f);
    }

    /// <summary>1 = top of the pecking order.</summary>
    public int Rank(Pet pet, IReadOnlyList<Pet> pets)
    {
        float Score(Pet p) => pets.Where(o => !ReferenceEquals(o, p)).Sum(o => DominanceOf(p, o));
        var mine = Score(pet);
        return 1 + pets.Count(o => !ReferenceEquals(o, pet) && Score(o) > mine + 1e-4f);
    }

    /// <summary>The pet this one likes best, if any.</summary>
    public Pet? BestFriend(Pet pet, IReadOnlyList<Pet> pets) =>
        pets.Where(o => !ReferenceEquals(o, pet)).OrderByDescending(o => Friendship(pet, o)).FirstOrDefault();
}
