using PetFam.Core.Geometry;
using PetFam.Core.Pets;

namespace PetFam.Core.Social;

/// <summary>
/// A dog invites play with a play bow (front down, rear up, tail wagging).
/// With a cat: the cat either bolts and the dog gives chase until the cat turns and swats, or the cat arches,
/// hisses and the dog backs off (the cat moves up in rank). Dogs and cats read each other's signals differently,
/// so the cat's answer depends on how well they know each other.
/// With another dog: a bow back, a game of tag, boxing on hind legs, and the winner standing over the loser,
/// who shows its belly.
/// </summary>
public sealed class PlayBowSession : SocialSession
{
    private enum Phase { Approach, Bow, Hiss, BackOff, Flee, Swat, BowBack, Tag, Box, Pin }

    private Phase _phase;
    private Pet _runner = null!;
    private Pet _chaser = null!;
    private Pet _winner = null!;
    private Pet _loser = null!;
    private Vec2 _target;
    private int _tagRounds;

    public PlayBowSession(Pet dog, Pet partner) : base(dog, partner) { }

    public override string Name => "playbow:" + _phase;

    protected override void Step(SocialContext ctx, float dt)
    {
        var dog = Initiator;
        var other = Partner;
        switch (_phase)
        {
            case Phase.Approach:
                if (Vec2.Distance(dog.Position, other.Position) < 260f) { Face(other, dog); other.SetAnimation("idle"); }
                if (MoveTo(ctx, dog, Beside(ctx, other, dog.Position, 70f), dt, 1.3f, "walk") || T > 8f)
                {
                    Face(dog, other);
                    dog.Say(L.S("Woof!", "왈!"));
                    Next(ref _phase, Phase.Bow);
                }
                break;

            case Phase.Bow:
                dog.SetAnimation("playbow");
                Face(dog, other);
                Face(other, dog);
                other.SetAnimation(other.Species == PetSpecies.Cat ? "sit" : "idle");
                if (T < 1.3f) break;
                if (other.Species == PetSpecies.Cat)
                {
                    if (Accepts(ctx, other, dog))
                    {
                        _runner = other; _chaser = dog;
                        _target = AwayFrom(ctx, other, dog.Position, 380f);
                        Next(ref _phase, Phase.Flee);
                    }
                    else
                    {
                        other.Say(L.S("Hiss!", "하악!"));
                        Next(ref _phase, Phase.Hiss);
                    }
                }
                else
                {
                    if (Accepts(ctx, other, dog, 0.15f))
                    {
                        other.Say(L.S("Woof!", "왈!"));
                        Next(ref _phase, Phase.BowBack);
                    }
                    else
                    {
                        other.Say(L.S("Grrr", "으르렁"));
                        Next(ref _phase, Phase.Hiss);
                    }
                }
                break;

            case Phase.Hiss:
                other.SetAnimation("hiss");
                Face(other, dog);
                dog.SetAnimation("idle");
                if (T >= 1.4f)
                {
                    dog.Say(L.S("Whimper…", "낑…"));
                    _target = AwayFrom(ctx, dog, other.Position, 160f);
                    ctx.Social.ShiftDominance(other, dog, 0.12f);
                    ctx.Social.AddFriendship(dog, other, -3f);
                    Next(ref _phase, Phase.BackOff);
                }
                break;

            case Phase.BackOff:
                other.SetAnimation("hiss");
                if (MoveTo(ctx, dog, _target, dt, 0.7f, "walk") || T > 4f) End();
                break;

            case Phase.Flee:
            {
                var runnerDone = MoveTo(ctx, _runner, _target, dt, _runner.RunMultiplier(), "run");
                var behind = Beside(ctx, _runner, _chaser.Position, 60f);
                MoveTo(ctx, _chaser, behind, dt, _chaser.RunMultiplier() * 0.9f, "run");
                if (runnerDone || T > 6f)
                {
                    if (_runner.Species == PetSpecies.Cat) { _runner.Say(L.S("Mew!", "냥!")); Next(ref _phase, Phase.Swat); }
                    else if (++_tagRounds < 2)
                    {
                        (_runner, _chaser) = (_chaser, _runner);   // tag: now the other one runs
                        _target = AwayFrom(ctx, _runner, _chaser.Position, 300f);
                        Next(ref _phase, Phase.Tag);
                    }
                    else Next(ref _phase, Phase.Box);
                }
                break;
            }

            case Phase.Tag:
                // Short pause while roles swap, then the chase resumes.
                _runner.SetAnimation("playbow");
                _chaser.SetAnimation("playbow");
                if (T >= 0.8f) Next(ref _phase, Phase.Flee);
                break;

            case Phase.Swat:
                Face(other, dog);
                Face(dog, other);
                other.SetAnimation("bat");
                dog.SetAnimation(T < 0.5f ? "startle" : "playbow");
                dog.Lift = T < 0.5f ? Hop(T / 0.5f, 18f) : 0f;
                if (T >= 1.4f)
                {
                    ctx.Social.AddFriendship(dog, other, 6f);
                    ctx.Social.ShiftDominance(other, dog, 0.04f);
                    dog.Needs.Play(25f);
                    other.Needs.Play(20f);
                    End();
                }
                break;

            case Phase.BowBack:
                other.SetAnimation("playbow");
                dog.SetAnimation("playbow");
                if (T >= 1f)
                {
                    _runner = other; _chaser = dog;
                    _target = AwayFrom(ctx, other, dog.Position, 320f);
                    Next(ref _phase, Phase.Flee);
                }
                break;

            case Phase.Box:
            {
                // Face each other close up, rear on hind legs and spar.
                var spot = Beside(ctx, dog, other.Position, 55f);
                var arrived = MoveTo(ctx, other, spot, dt, 1.2f, "walk");
                Face(dog, other);
                if (arrived) Face(other, dog);
                if (!arrived && T < 2f) break;
                dog.SetAnimation("box");
                other.SetAnimation("box");
                dog.Say(L.S("Woof woof", "왈왈"), 0.6f);
                if (T >= 4f)
                {
                    _winner = Winner(ctx, dog, other);
                    _loser = ReferenceEquals(_winner, dog) ? other : dog;
                    Next(ref _phase, Phase.Pin);
                }
                break;
            }

            case Phase.Pin:
                _winner.SetAnimation("standover");
                _loser.SetAnimation("submit");
                if (T >= 1.8f)
                {
                    ctx.Social.ShiftDominance(_winner, _loser, 0.08f);
                    ctx.Social.AddFriendship(dog, other, 8f);
                    dog.Needs.Play(30f);
                    other.Needs.Play(30f);
                    End();
                }
                break;
        }
    }
}

/// <summary>
/// A cat stalks another pet, wiggles its rear and pounces (the same hunting sequence it uses on toys).
/// A dog jumps in surprise, then play-bows and chases the cat. Another cat wrestles back; afterwards the winner
/// chases the loser. A grumpy target hisses and swats instead and the stalker slinks off.
/// </summary>
public sealed class StalkPounceSession : SocialSession
{
    private enum Phase { Stalk, Wiggle, Pounce, Startle, DogBow, DogChase, Wrestle, Chase, Rebuff, Slink }

    private Phase _phase;
    private Vec2 _from, _to, _target;
    private bool _accepted;
    private Pet _winner = null!;
    private Pet _loser = null!;

    public StalkPounceSession(Pet cat, Pet target) : base(cat, target) { }

    public override string Name => "pounce:" + _phase;

    protected override void Step(SocialContext ctx, float dt)
    {
        var cat = Initiator;
        var prey = Partner;
        switch (_phase)
        {
            case Phase.Stalk:
                if (prey.Animation is "walk" or "run") prey.SetAnimation("idle");
                if (MoveTo(ctx, cat, Beside(ctx, prey, cat.Position, 150f), dt, 0.45f, "stalk") || T > 10f)
                {
                    Face(cat, prey);
                    Next(ref _phase, Phase.Wiggle);
                }
                break;

            case Phase.Wiggle:
                cat.SetAnimation("wiggle");
                Face(cat, prey);
                if (T >= 1f)
                {
                    _from = cat.Position;
                    _to = Beside(ctx, prey, cat.Position, 35f);
                    _accepted = Accepts(ctx, prey, cat);
                    Next(ref _phase, Phase.Pounce);
                }
                break;

            case Phase.Pounce:
            {
                var k = System.Math.Min(1f, T / 0.5f);
                cat.SetAnimation("pounce");
                cat.Position = Vec2.Lerp(_from, _to, k);
                cat.Lift = Hop(k, 45f);
                if (k < 1f) break;
                cat.Lift = 0f;
                if (!_accepted)
                {
                    prey.Say(prey.Species == PetSpecies.Cat ? L.S("Hiss!", "하악!") : L.S("Grrr", "으르렁"));
                    Next(ref _phase, Phase.Rebuff);
                }
                else if (prey.Species == PetSpecies.Dog)
                {
                    prey.Say(L.S("Eep!", "깜짝!"));
                    Next(ref _phase, Phase.Startle);
                }
                else
                {
                    Next(ref _phase, Phase.Wrestle);
                }
                break;
            }

            case Phase.Startle:
                prey.SetAnimation("startle");
                prey.Lift = Hop(T / 0.45f, 26f);
                cat.SetAnimation("bat");
                if (T >= 0.45f)
                {
                    prey.Lift = 0f;
                    Face(prey, cat);
                    prey.Say(L.S("Woof!", "왈!"));
                    Next(ref _phase, Phase.DogBow);
                }
                break;

            case Phase.DogBow:
                prey.SetAnimation("playbow");
                cat.SetAnimation("idle");
                Face(cat, prey);
                if (T >= 1f)
                {
                    _target = AwayFrom(ctx, cat, prey.Position, 360f);
                    Next(ref _phase, Phase.DogChase);
                }
                break;

            case Phase.DogChase:
            {
                var done = MoveTo(ctx, cat, _target, dt, cat.RunMultiplier(), "run");
                MoveTo(ctx, prey, Beside(ctx, cat, prey.Position, 60f), dt, prey.RunMultiplier() * 0.85f, "run");
                if (done || T > 5f)
                {
                    ctx.Social.AddFriendship(cat, prey, 5f);
                    ctx.Social.ShiftDominance(cat, prey, 0.04f);
                    cat.Needs.Play(25f);
                    prey.Needs.Play(20f);
                    End();
                }
                break;
            }

            case Phase.Wrestle:
                cat.SetAnimation("wrestle");
                prey.SetAnimation("wrestle");
                Face(cat, prey);
                Face(prey, cat);
                if (T >= 2.6f)
                {
                    _winner = Winner(ctx, cat, prey);
                    _loser = ReferenceEquals(_winner, cat) ? prey : cat;
                    _target = AwayFrom(ctx, _loser, _winner.Position, 300f);
                    Next(ref _phase, Phase.Chase);
                }
                break;

            case Phase.Chase:
            {
                var done = MoveTo(ctx, _loser, _target, dt, _loser.RunMultiplier(), "run");
                MoveTo(ctx, _winner, Beside(ctx, _loser, _winner.Position, 55f), dt, _winner.RunMultiplier() * 0.9f, "run");
                if (done || T > 5f)
                {
                    ctx.Social.ShiftDominance(_winner, _loser, 0.06f);
                    ctx.Social.AddFriendship(cat, prey, 6f);
                    cat.Needs.Play(25f);
                    prey.Needs.Play(25f);
                    End();
                }
                break;
            }

            case Phase.Rebuff:
                prey.SetAnimation(prey.Species == PetSpecies.Cat ? (T < 0.8f ? "hiss" : "bat") : "hiss");
                Face(prey, cat);
                cat.SetAnimation("startle");
                if (T >= 1.5f)
                {
                    ctx.Social.ShiftDominance(prey, cat, 0.1f);
                    ctx.Social.AddFriendship(cat, prey, -3f);
                    _target = AwayFrom(ctx, cat, prey.Position, 180f);
                    Next(ref _phase, Phase.Slink);
                }
                break;

            case Phase.Slink:
                if (MoveTo(ctx, cat, _target, dt, 0.8f, "walk") || T > 4f) End();
                break;
        }
    }
}

/// <summary>Allogrooming between friendly cats: the higher-ranked cat licks the other, who lies down and enjoys it.</summary>
public sealed class GroomEachOtherSession : SocialSession
{
    private enum Phase { Approach, Groom }

    private Phase _phase;

    public GroomEachOtherSession(Pet groomer, Pet groomed) : base(groomer, groomed) { }

    public override string Name => "allogroom:" + _phase;

    protected override void Step(SocialContext ctx, float dt)
    {
        var a = Initiator;
        var b = Partner;
        switch (_phase)
        {
            case Phase.Approach:
                b.SetAnimation("lie");
                if (MoveTo(ctx, a, Beside(ctx, b, a.Position, 55f), dt, 1f, "walk") || T > 8f) Next(ref _phase, Phase.Groom);
                break;
            case Phase.Groom:
                Face(a, b);
                a.SetAnimation("groom");
                b.SetAnimation("lie");
                b.Needs.ReceiveAffection(dt * 6f);
                if (T >= 4.5f)
                {
                    ctx.Social.AddFriendship(a, b, 4f);
                    b.Say(L.S("Purr~", "골골~"), 1.5f);
                    End();
                }
                break;
        }
    }
}
