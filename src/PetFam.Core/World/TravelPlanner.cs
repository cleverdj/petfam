using PetFam.Core.Behavior;
using PetFam.Core.Behavior.Behaviors;
using PetFam.Core.Pets;

namespace PetFam.Core.World;

/// <summary>
/// Decides when pets go to another virtual desktop (plan §6.4.3): follow-mode pets come after the user switches,
/// free pets visit the desktop the user stays on and now and then wander off on their own, home-mode pets stay home,
/// and hungry or sleepy pets go to the desktop with food or a house. The trip itself is <see cref="TravelBehavior"/>.
/// </summary>
public sealed class TravelPlanner
{
    public const float CheckInterval = 1f;

    /// <summary>A free pet leaves the desktop on its own about this often (seconds, average, at curiosity 50).</summary>
    public const float WanderOffEvery = 25f * 60f;

    /// <summary>A home-mode pet that was called goes back home after this long.</summary>
    public const float HomeAfter = 120f;

    /// <summary>A pet that left the user's desktop on its own does not come back for at least this long.</summary>
    public const float ComeBackAfter = 90f;

    private float _since;
    private float _onDesktop;

    /// <summary>Per pet: the earliest time (seconds on this desktop) it may come to the user's desktop.</summary>
    private readonly Dictionary<Guid, float> _comeAt = new();

    private readonly Dictionary<Guid, float> _awayFromHome = new();

    /// <summary>Seconds since the user arrived on the desktop they are looking at.</summary>
    public float SecondsOnDesktop => _onDesktop;

    /// <summary>The user switched desktops: everyone gets their own moment to come along.</summary>
    public void OnSwitched(IEnumerable<Pet> pets, Random rng)
    {
        _onDesktop = 0f;
        _since = 0f;
        _comeAt.Clear();
        foreach (var pet in pets) _comeAt[pet.Id] = ComeDelay(pet, rng);
    }

    /// <summary>
    /// How long after the switch the pet comes. Follow: 1-3 s once it trusts you a little (bond 15+), shy ones later.
    /// Free: minutes, sooner with a higher bond (10 min at bond 0, 2 min at bond 100). Home: never by itself.
    /// </summary>
    public static float ComeDelay(Pet pet, Random rng)
    {
        var r = (float)rng.NextDouble();
        return pet.Travel switch
        {
            TravelMode.Follow => pet.Bond.Value >= 15f ? 1f + r * 2f : 6f + r * 10f,
            TravelMode.Free => System.Math.Max(2f, 10f - pet.Bond.Value / 12.5f) * 60f * (0.8f + r * 0.4f),
            _ => float.PositiveInfinity,
        };
    }

    /// <summary>Not busy with something that should not be cut short.</summary>
    public static bool CanLeave(Pet p) =>
        p.Trip is null && p.Session is null && p.Hold == HoldState.None && p.Queue.Count == 0 && !p.Hidden &&
        p.Layer == PetLayer.Backyard &&
        p.Engine.Current is not (SleepBehavior or ShelterBehavior or EatBehavior or HeldBehavior or QueuedInteractionBehavior or SocialBehavior or TravelBehavior);

    public void Tick(Simulation sim, float dt, Random rng)
    {
        var world = sim.World;
        if (sim.VisibleCellId is null || world.DesktopOrder.Count < 2) return;
        _onDesktop += dt;
        _since += dt;
        if (_since < CheckInterval) return;
        var step = _since;
        _since = 0f;
        if (world.Find(sim.VisibleCellId) is not { } visible) return;

        foreach (var pet in sim.Pets)
        {
            if (!_comeAt.ContainsKey(pet.Id)) _comeAt[pet.Id] = _onDesktop + ComeDelay(pet, rng);
            if (!CanLeave(pet) || world.Find(pet.CellId) is not { } here) continue;

            // Home-mode pets live on their home desktop.
            var home = pet.Travel == TravelMode.Home && pet.HomeCellId is { } h && h != here.Id ? world.Find(h) : null;

            if (ReferenceEquals(here, visible))
            {
                if (home is not null)
                {
                    var away = _awayFromHome.GetValueOrDefault(pet.Id) + step;
                    _awayFromHome[pet.Id] = away;
                    if (away >= HomeAfter)
                    {
                        Go(sim, pet, home, TripReason.Home, run: false, rng);
                        _awayFromHome.Remove(pet.Id);
                    }
                    continue;
                }

                if (NeedTrip(world, pet, here) is { } need)
                {
                    Go(sim, pet, need, TripReason.Need, run: false, rng);
                    _comeAt[pet.Id] = _onDesktop + ComeBackAfter;
                    continue;
                }

                // A free pet sometimes goes for a look at the next desktop.
                if (pet.Travel == TravelMode.Free &&
                    rng.NextDouble() < step / WanderOffEvery * (0.5f + pet.Needs.Curiosity / 100f))
                {
                    var dir = rng.Next(2) == 0 ? -1 : 1;
                    if ((world.Neighbor(here, dir) ?? world.Neighbor(here, -dir)) is { } next)
                    {
                        Go(sim, pet, next, TripReason.Visit, run: false, rng);
                        _comeAt[pet.Id] = _onDesktop + System.Math.Max(ComeBackAfter, ComeDelay(pet, rng));
                    }
                }
                continue;
            }

            // On a desktop the user is not looking at.
            if (home is not null)
            {
                Go(sim, pet, home, TripReason.Home, run: false, rng);
                continue;
            }
            if (pet.Travel == TravelMode.Home) continue;

            var hungryForVisible = NeedTrip(world, pet, here) is { } n && ReferenceEquals(n, visible);
            if (hungryForVisible || _onDesktop >= _comeAt[pet.Id])
            {
                var reason = hungryForVisible ? TripReason.Need : pet.Travel == TravelMode.Follow ? TripReason.Follow : TripReason.Visit;
                Go(sim, pet, visible, reason, run: pet.Travel == TravelMode.Follow, rng);
            }
        }
    }

    /// <summary>The user called the pet (tray). Trusting pets run over; shy ones dawdle.</summary>
    public static bool Call(Simulation sim, Pet pet, Random rng)
    {
        var world = sim.World;
        if (sim.VisibleCellId is null || world.Find(sim.VisibleCellId) is not { } visible) return false;
        if (pet.CellId == visible.Id || pet.Trip is not null || pet.Hold != HoldState.None) return false;

        // Whatever it was doing over there (even sleeping in its house), it gets up and comes.
        pet.Hidden = false;
        pet.Session?.Abort();
        pet.Queue.Clear();
        pet.Engine.ForceStop(new BehaviorContext
        {
            Pet = pet, Cell = world.Find(pet.CellId) ?? visible, User = sim.User, Rng = rng, Debug = sim.Debug,
            NowUtc = DateTime.UtcNow, Visible = false,
        });
        var trusting = pet.Bond.Value >= (pet.Species == PetSpecies.Dog ? 15f : 30f);
        Go(sim, pet, visible, TripReason.Call, run: trusting, rng, gap: trusting ? 1f : 4f);
        return true;
    }

    /// <summary>Hungry with no food here, or sleepy with no house here: the nearest desktop that has it.</summary>
    public static Cell? NeedTrip(World world, Pet pet, Cell here)
    {
        static bool HasFood(Cell c) => c.Bowls.Any(b => b.Amount > 0f);
        int Distance(Cell c) => System.Math.Abs(world.DesktopIndex(c) - world.DesktopIndex(here));

        if (pet.Needs.Hunger < 25f && !HasFood(here))
            return world.Cells.Where(c => c != here && HasFood(c)).MinBy(Distance);
        if (pet.Needs.Energy < pet.Traits.SleepThreshold - 15f && here.Houses.Count == 0)
            return world.Cells.Where(c => c != here && c.Houses.Count > 0).MinBy(Distance);
        return null;
    }

    public static void Go(Simulation sim, Pet pet, Cell to, TripReason reason, bool run, Random rng, float? gap = null)
    {
        var world = sim.World;
        var from = world.Find(pet.CellId);
        var delta = from is null ? 1 : world.DesktopIndex(to) - world.DesktopIndex(from);
        var hops = System.Math.Max(1, System.Math.Abs(delta));
        pet.Trip = new Trip
        {
            To = to,
            Reason = reason,
            Direction = delta < 0 ? -1 : 1,
            Run = run && pet.Needs.Energy > 20f,
            Gap = gap ?? (1f + (float)rng.NextDouble() * 1.5f) * (1f + (hops - 1) * 0.6f),
        };
    }
}
