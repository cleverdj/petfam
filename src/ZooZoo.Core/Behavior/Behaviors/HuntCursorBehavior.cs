using ZooZoo.Core.Geometry;
using ZooZoo.Core.Pets;

namespace ZooZoo.Core.Behavior.Behaviors;

/// <summary>
/// Hunting instinct: a cat treats the moving mouse pointer as prey. It crouches and creeps closer, wiggles its rear,
/// pounces on the spot under the pointer and bats at it. While the pointer keeps moving it goes again (a few times);
/// once the pointer stays still the cat loses interest.
/// </summary>
public sealed class HuntCursorBehavior : Behavior
{
    public const float MinPreySpeed = 120f;     // px/s of pointer movement that triggers the hunt
    public const float GiveUpStill = 2.5f;      // s of a still pointer before the cat gives up
    public const float Cooldown = 15f;

    private enum Phase { Stalk, Wiggle, Pounce, Bat }

    private Phase _phase;
    private float _t;
    private Vec2 _from, _to;
    private int _pounces;
    private float _cooldown;

    public override string Name => "hunt:" + _phase;

    public override bool Interruptible => false;

    public void TickCooldown(float dt) => _cooldown = System.Math.Max(0f, _cooldown - dt);

    public override float Score(BehaviorContext ctx)
    {
        var pet = ctx.Pet;
        var user = ctx.User;
        if (pet.Species != PetSpecies.Cat || _cooldown > 0f || pet.Hidden) return 0f;
        if (!user.CursorOnDesktop || user.CursorSpeed < MinPreySpeed || pet.Needs.Energy < 20f) return 0f;
        if (Vec2.Distance(Prey(ctx), pet.Position) > 750f * ctx.Cell.Map.DepthScale(pet.Position.Y)) return 0f;
        var funDeficit = (100f - pet.Needs.Fun) / 100f;
        return 0.45f + pet.Personality.Playfulness * 0.4f + funDeficit * 0.3f;
    }

    /// <summary>The floor spot under the pointer: the "prey" lives on the floor even when the pointer is in the sky.</summary>
    public static Vec2 Prey(BehaviorContext ctx)
    {
        var map = ctx.Cell.Map;
        var c = ctx.User.Cursor;
        return map.ClampToRoam(new Vec2(c.X, System.Math.Clamp(c.Y, map.RoamArea.Top, map.RoamArea.Bottom - 1)));
    }

    public override void Start(BehaviorContext ctx)
    {
        _phase = Phase.Stalk;
        _t = 0f;
        _pounces = 0;
    }

    public override bool Update(BehaviorContext ctx, float dt)
    {
        var pet = ctx.Pet;
        var map = ctx.Cell.Map;
        var depth = map.DepthScale(pet.Position.Y);
        var prey = Prey(ctx);
        _t += dt;

        if (_phase != Phase.Pounce && (ctx.User.CursorStill > GiveUpStill || !ctx.User.CursorOnDesktop)) return true;

        switch (_phase)
        {
            case Phase.Stalk:
            {
                // Creep low and slow; stop at striking distance.
                var delta = prey - pet.Position;
                var strike = 150f * depth;
                pet.FaceToward(delta);
                if (delta.Length <= strike || _t > 12f)
                {
                    _phase = Phase.Wiggle;
                    _t = 0f;
                    break;
                }
                var step = pet.Species.WalkSpeed() * 0.45f * depth * dt;
                pet.Position = map.ClampToRoam(pet.Position + delta.Normalized * System.Math.Min(step, delta.Length - strike));
                pet.SetAnimation("stalk");
                break;
            }

            case Phase.Wiggle:
                pet.FaceToward(prey - pet.Position);
                pet.SetAnimation("wiggle");
                if (_t >= ctx.RandomRange(0.6f, 1.1f))
                {
                    _from = pet.Position;
                    _to = prey;
                    _phase = Phase.Pounce;
                    _t = 0f;
                }
                break;

            case Phase.Pounce:
            {
                var k = System.Math.Min(1f, _t / 0.45f);
                pet.SetAnimation("pounce");
                pet.Position = map.ClampToRoam(Vec2.Lerp(_from, _to, k));
                pet.Lift = MathF.Sin(k * MathF.PI) * 50f;
                if (k >= 1f)
                {
                    pet.Lift = 0f;
                    _pounces++;
                    pet.Needs.Play(12f);
                    pet.Needs.Energy = System.Math.Max(0f, pet.Needs.Energy - 2f);
                    if (ctx.Rng.NextDouble() < 0.4) pet.Say("냥!", 1f);
                    _phase = Phase.Bat;
                    _t = 0f;
                }
                break;
            }

            case Phase.Bat:
                pet.SetAnimation("bat");
                pet.FaceToward(prey - pet.Position);
                if (_t >= 0.9f)
                {
                    if (_pounces >= 3 || ctx.User.CursorSpeed < MinPreySpeed * 0.5f) return true;
                    _phase = Phase.Stalk;
                    _t = 0f;
                }
                break;
        }
        return false;
    }

    public override void Stop(BehaviorContext ctx, bool interrupted)
    {
        ctx.Pet.Lift = 0f;
        ctx.Pet.SetAnimation("idle");
        _cooldown = Cooldown;
    }
}
