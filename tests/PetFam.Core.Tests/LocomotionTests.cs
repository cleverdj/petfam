using PetFam.Core;
using PetFam.Core.Geometry;
using PetFam.Core.Pets;
using Xunit;

namespace PetFam.Core.Tests;

public class LocomotionTests
{
    private static Pet MakePet(float heading = 0f) =>
        new(PetSpecies.Cat, "t", DateTime.UtcNow, new Random(1)) { Heading = heading, TargetHeading = heading };

    private const float Dt = 1f / 30f;

    [Fact]
    public void Speeds_up_from_rest_instead_of_jumping_to_cruise()
    {
        var pet = MakePet();
        var far = new Vec2(1000, 0);
        var first = pet.Motion.Step(pet, far, 90f, Dt) / Dt;
        Assert.True(first < 90f * 0.5f);
        for (var i = 0; i < 15; i++) pet.Motion.Step(pet, far, 90f, Dt);
        Assert.Equal(90f, pet.Motion.Speed, precision: 1);
    }

    [Fact]
    public void Brakes_when_arriving()
    {
        var pet = MakePet();
        var target = new Vec2(150, 0);
        while (target.X - pet.Position.X > 4f)
            pet.Position += new Vec2(pet.Motion.Step(pet, target - pet.Position, 90f, Dt), 0);
        Assert.True(pet.Motion.Speed < 90f * 0.6f);
    }

    [Fact]
    public void Turns_before_setting_off_the_other_way()
    {
        var facingAway = MakePet(heading: 180f);
        var facingThere = MakePet(heading: 0f);
        for (var i = 0; i < 10; i++)
        {
            facingAway.Motion.Step(facingAway, new Vec2(1000, 0), 90f, Dt);
            facingThere.Motion.Step(facingThere, new Vec2(1000, 0), 90f, Dt);
        }
        Assert.True(facingAway.Motion.Speed < facingThere.Motion.Speed * 0.3f);
    }

    [Fact]
    public void Measures_ground_distance_and_ignores_carrying()
    {
        var pet = MakePet();
        pet.Motion.EndFrame(pet, Dt, 2f);
        pet.Position = new Vec2(20, 0);
        pet.Motion.EndFrame(pet, Dt, 2f);
        Assert.Equal(10f, pet.Motion.Distance, precision: 2);

        pet.Hold = HoldState.Carried;
        pet.Position = new Vec2(60, 0);
        pet.Motion.EndFrame(pet, Dt, 2f);
        Assert.Equal(10f, pet.Motion.Distance, precision: 2);
    }

    [Fact]
    public void Simulation_tick_measures_turning()
    {
        var pet = MakePet(heading: 0f);
        pet.TargetHeading = 180f;
        for (var i = 0; i < 3; i++)
        {
            Simulation.TurnToward(pet, Dt);
            pet.Motion.EndFrame(pet, Dt, 1f);
        }
        Assert.True(MathF.Abs(pet.Motion.TurnRate) > 100f);
    }
}
