using PetFam.Core.Geometry;
using PetFam.Core.Pets;
using PetFam.Core.World;

namespace PetFam.Core.Behavior.Behaviors;

/// <summary>
/// Moving to another virtual desktop (plan §6.4.2): walk off the screen edge on the side of the target desktop,
/// be "between rooms" for a moment, then walk in from the opposite edge of the target desktop. Nobody sees a pet
/// leave or arrive on a desktop the user is not looking at, so those parts are skipped.
/// </summary>
public sealed class TravelBehavior : Behavior
{
    private enum Phase { ToEdge, Out, Gap, In }

    private Phase _phase;
    private Vec2 _target;
    private float _gap;

    public override string Name => "travel";

    public override bool Interruptible => false;

    public override float Score(BehaviorContext ctx) => ctx.Pet.Trip is not null ? 6f : 0f;

    public override void Start(BehaviorContext ctx)
    {
        var pet = ctx.Pet;
        if (pet.Trip is not { } trip) return;
        if (ctx.Visible)
        {
            _phase = Phase.ToEdge;
            _target = EdgeSpot(ctx.Cell.Map, trip.Direction, pet.Position.Y);
            pet.SetAnimation(trip.Run ? "run" : "walk");
            pet.FaceToward(_target - pet.Position);
        }
        else
        {
            BeginGap(pet, trip);
        }
    }

    public override bool Update(BehaviorContext ctx, float dt)
    {
        var pet = ctx.Pet;
        if (pet.Trip is not { } trip) return true;

        switch (_phase)
        {
            case Phase.ToEdge:
                if (Walk(ctx, trip, _target, dt, clamp: true))
                {
                    _phase = Phase.Out;
                    _target = OffScreen(ctx.Cell, trip.Direction, pet.Position.Y);
                }
                return false;

            case Phase.Out:
                if (Walk(ctx, trip, _target, dt, clamp: false)) BeginGap(pet, trip);
                return false;

            case Phase.Gap:
                // ctx.Cell is already the destination (CellId changed when the gap began).
                _gap -= dt;
                if (_gap > 0f) return false;
                var map = trip.To.Map;
                var entry = EdgeSpot(map, -trip.Direction, pet.Position.Y);
                var inward = (60f + (float)ctx.Rng.NextDouble() * 160f) * map.DepthScale(entry.Y) * trip.Direction;
                _target = map.ClampToRoam(new Vec2(entry.X + inward, entry.Y));
                if (!ctx.Visible)
                {
                    // Arrived where nobody is looking: just be there.
                    pet.Position = _target;
                    return true;
                }
                pet.Position = OffScreen(trip.To, -trip.Direction, _target.Y);
                pet.SetAnimation(trip.Run ? "run" : "walk");
                _phase = Phase.In;
                return false;

            default:
                return Walk(ctx, trip, _target, dt, clamp: false);
        }
    }

    public override void Stop(BehaviorContext ctx, bool interrupted)
    {
        var pet = ctx.Pet;
        if (pet.Trip is { } trip)
        {
            var arrived = pet.CellId == trip.To.Id;
            var map = arrived ? trip.To.Map : ctx.Cell.Map;
            pet.Position = map.ClampToRoam(pet.Position);
            if (arrived && !interrupted)
                switch (trip.Reason)
                {
                    case TripReason.Call: pet.SayAs(v => v.Called, ctx.Rng, 2.5f); break;
                    case TripReason.Follow: if (ctx.Rng.NextDouble() < 0.5) pet.Say(pet.Species == PetSpecies.Dog ? L.S("Woof woof!", "멍멍!") : L.S("Mew", "냥"), 1.6f); break;
                    case TripReason.Visit: if (ctx.Rng.NextDouble() < 0.5) pet.Say(L.S("Came to visit", "놀러 왔어"), 2f); break;
                }
        }
        pet.Trip = null;
        pet.SetAnimation("idle");
    }

    private void BeginGap(Pet pet, Trip trip)
    {
        pet.CellId = trip.To.Id;
        pet.Position = OffScreen(trip.To, -trip.Direction, pet.Position.Y);
        _gap = trip.Gap;
        _phase = Phase.Gap;
    }

    /// <summary>Moves toward the target. True when there.</summary>
    private static bool Walk(BehaviorContext ctx, Trip trip, Vec2 target, float dt, bool clamp)
    {
        var pet = ctx.Pet;
        var map = ctx.Cell.Map;
        var speed = pet.WalkSpeed() * map.DepthScale(pet.Position.Y) * (trip.Run ? pet.RunMultiplier() : 1f);
        var delta = target - pet.Position;
        var dist = delta.Length;
        var step = pet.Motion.Step(pet, delta, speed, dt);
        if (dist <= System.Math.Max(step, 1f))
        {
            pet.Position = target;
            return true;
        }
        var next = pet.Position + delta.Normalized * step;
        pet.Position = clamp ? map.ClampToRoam(next) : next;
        pet.FaceToward(delta);
        return false;
    }

    /// <summary>Just past the screen edge on that side (+1 right, -1 left), so the whole body is out of sight.</summary>
    public static Vec2 OffScreen(Cell cell, int side, float y)
    {
        var b = cell.Bounds;
        var margin = 130f * System.Math.Max(0.5f, cell.Map.DepthScale(y));
        return new Vec2(side > 0 ? b.Right + margin : b.Left - margin, y);
    }

    /// <summary>The floor spot closest to that screen edge, near height y: where a pet leaves or comes in.</summary>
    public static Vec2 EdgeSpot(HabitatMap map, int side, float y)
    {
        var r = map.RoamArea;
        if (r.Width <= 0 || r.Height <= 0) return map.ClampToRoam(new Vec2(side > 0 ? r.Right : r.Left, y));
        const int Columns = 40;
        var y0 = System.Math.Clamp(y, r.Top, r.Bottom - 1);
        for (var k = 0; k <= Columns; k++)
        {
            var x = side > 0 ? r.Right - 1 - k * r.Width / (float)Columns : r.Left + k * r.Width / (float)Columns;
            for (var j = 0; j <= 16; j++)
            {
                // y0, y0+s, y0-s, y0+2s, ...
                var off = (j + 1) / 2 * (r.Height / 16f) * (j % 2 == 0 ? -1f : 1f);
                var p = new Vec2(x, System.Math.Clamp(y0 + off, r.Top, r.Bottom - 1));
                if (map.CanRoam(p)) return p;
            }
        }
        return map.ClampToRoam(new Vec2(side > 0 ? r.Right - 1 : r.Left, y0));
    }
}
