using PetFam.Core.Geometry;

namespace PetFam.Core.Pets;

/// <summary>
/// Game-style ground movement for a pet.
/// Speed ramps up from rest and brakes before arriving instead of jumping between full speed and a dead stop.
/// The pet slows down to turn toward where it is heading rather than sliding sideways.
/// The renderer reads the measured motion for leaning into turns, head lead and a walk cycle tied to the
/// distance covered, so the feet do not slide.
/// </summary>
public sealed class Locomotion
{
    /// <summary>Seconds from standing to cruise speed.</summary>
    public const float AccelTime = 0.32f;

    /// <summary>Seconds from cruise speed to standing.</summary>
    public const float BrakeTime = 0.22f;

    /// <summary>Commanded ground speed in pixels per second.</summary>
    public float Speed { get; private set; }

    /// <summary>Ground distance covered in model units (pixels at depth 1), plus little steps while turning on the spot.</summary>
    public float Distance { get; private set; }

    /// <summary>Measured screen velocity in pixels per second, smoothed.</summary>
    public Vec2 Velocity { get; private set; }

    /// <summary>Measured ground speed in model units per second, smoothed.</summary>
    public float GroundSpeed { get; private set; }

    /// <summary>Measured turn rate in degrees per second, smoothed. Positive turns clockwise on screen.</summary>
    public float TurnRate { get; private set; }

    /// <summary>Measured change of ground speed in model units per second², smoothed.</summary>
    public float Acceleration { get; private set; }

    private bool _driven;
    private float _cruise = 100f;
    private bool _has;
    private Vec2 _lastPos;
    private float _lastHeading;

    /// <summary>
    /// How far to move this frame toward a point <paramref name="delta"/> away at <paramref name="cruise"/> px/s.
    /// Never more than the remaining distance.
    /// </summary>
    public float Step(Pet pet, Vec2 delta, float cruise, float dt)
    {
        var dist = delta.Length;
        if (dist < 1e-3f || dt <= 0f || cruise <= 0f) return 0f;
        _driven = true;
        _cruise = cruise;

        // Turn first: a pet facing away barely creeps forward until its body comes round.
        var want = MathF.Atan2(delta.Y, delta.X) * 180f / MathF.PI;
        var off = MathF.Abs(Normalize(want - pet.Heading));
        var align = off <= 30f ? 1f : off >= 120f ? 0.15f : 1f - (off - 30f) / 90f * 0.85f;
        var target = cruise * align;

        // Arrive: brake so the pet comes to a soft stop on the spot instead of overshooting at full speed.
        var brake = cruise / BrakeTime;
        target = MathF.Min(target, MathF.Max(MathF.Sqrt(brake * dist), cruise * 0.25f));

        var accel = cruise / AccelTime;
        Speed = Speed < target ? MathF.Min(target, Speed + accel * dt) : MathF.Max(target, Speed - brake * dt);
        return MathF.Min(MathF.Max(Speed, cruise * 0.12f) * dt, dist);
    }

    /// <summary>Called once per tick after behaviors moved the pet. <paramref name="depth"/> is the perspective scale at its feet.</summary>
    public void EndFrame(Pet pet, float dt, float depth)
    {
        if (!_driven) Speed = MathF.Max(0f, Speed - _cruise / BrakeTime * dt);
        _driven = false;
        if (dt <= 0f) return;
        if (!_has)
        {
            _has = true;
            _lastPos = pet.Position;
            _lastHeading = pet.Heading;
        }

        var moved = pet.Position - _lastPos;
        var inst = moved * (1f / dt);
        // Teleports (drops, entering the house, peeking) and being carried are not walking.
        if (inst.Length > 2500f || pet.Hold == HoldState.Carried) inst = Vec2.Zero;
        var k = MathF.Min(1f, dt * 10f);
        Velocity += (inst - Velocity) * k;

        var ground = inst.Length / MathF.Max(0.1f, depth);
        var before = GroundSpeed;
        GroundSpeed += (ground - GroundSpeed) * k;
        Acceleration += ((GroundSpeed - before) / dt - Acceleration) * MathF.Min(1f, dt * 6f);

        var turned = Normalize(pet.Heading - _lastHeading);
        TurnRate += (turned / dt - TurnRate) * MathF.Min(1f, dt * 8f);

        // Turning on the spot still shuffles the feet: count the arc swept by the shoulders.
        Distance += ground * dt + MathF.Abs(turned) * MathF.PI / 180f * 12f;

        _lastPos = pet.Position;
        _lastHeading = pet.Heading;
    }

    private static float Normalize(float deg)
    {
        deg %= 360f;
        if (deg > 180f) deg -= 360f;
        if (deg <= -180f) deg += 360f;
        return deg;
    }
}
