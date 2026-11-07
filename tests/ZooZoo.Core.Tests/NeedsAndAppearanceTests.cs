using ZooZoo.Core.Pets;

namespace ZooZoo.Core.Tests;

public class NeedsAndAppearanceTests
{
    [Fact]
    public void Hunger_reaches_zero_in_about_twelve_hours()
    {
        var n = new Needs();
        n.Tick(12 * 3600f, userActive: false, sleeping: false);
        Assert.Equal(0f, n.Hunger, precision: 1);
    }

    [Fact]
    public void Curiosity_only_rises_while_user_is_active()
    {
        var n = new Needs();
        n.Tick(60f, userActive: false, sleeping: false);
        Assert.Equal(0f, n.Curiosity);
        n.Tick(60f, userActive: true, sleeping: false);
        Assert.Equal(15f, n.Curiosity, precision: 1);
    }

    [Fact]
    public void Sleeping_regenerates_energy()
    {
        var n = new Needs { Energy = 10f };
        n.Tick(3600f, userActive: false, sleeping: true);
        Assert.Equal(45f, n.Energy, precision: 1);
    }

    [Fact]
    public void Water_washes_mud_and_soaks()
    {
        var a = new Appearance();
        a.AddMud(0.8f);
        a.EnterWater();
        Assert.Equal(0f, a.Mud);
        Assert.Equal(1f, a.Wet);
    }

    [Fact]
    public void Fur_dries_in_twenty_minutes_by_day_and_twice_as_fast_in_sun()
    {
        var a = new Appearance();
        a.EnterWater();
        a.Tick(10 * 60f, isDaytime: true, inSunZone: false);
        Assert.Equal(0.5f, a.Wet, precision: 2);

        var sunny = new Appearance();
        sunny.EnterWater();
        sunny.Tick(10 * 60f, isDaytime: true, inSunZone: true);
        Assert.Equal(0f, sunny.Wet, precision: 2);
    }

    [Fact]
    public void Shake_halves_wetness_and_towel_clears_it()
    {
        var a = new Appearance();
        a.EnterWater();
        a.Shake();
        Assert.Equal(0.5f, a.Wet);
        a.AddMud(1f);
        a.Towel();
        Assert.Equal(0f, a.Wet);
        Assert.Equal(0.5f, a.Mud);
    }

    [Fact]
    public void Mud_dries_once_fur_is_dry_then_flakes_off_over_a_day()
    {
        var a = new Appearance();
        a.AddMud(1f);
        a.Tick(Appearance.DaytimeDrySeconds + 1, isDaytime: true, inSunZone: false);
        Assert.True(a.MudDried);
        a.Tick(Appearance.DriedMudFlakeSeconds / 2, isDaytime: true, inSunZone: false);
        Assert.Equal(0.5f, a.Mud, precision: 2);
    }
}
