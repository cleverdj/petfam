using ZooZoo.Core.Pets;

namespace ZooZoo.Core.Tests;

public class BondTests
{
    private static readonly DateTime T0 = new(2026, 11, 7, 9, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(0, BondStage.Stranger)]
    [InlineData(14.9f, BondStage.Stranger)]
    [InlineData(15, BondStage.Wary)]
    [InlineData(30, BondStage.Curious)]
    [InlineData(50, BondStage.Familiar)]
    [InlineData(70, BondStage.Trusting)]
    [InlineData(90, BondStage.Family)]
    [InlineData(100, BondStage.Family)]
    public void Stage_matches_plan_thresholds(float value, BondStage expected)
    {
        var bond = new Bond(T0, value);
        Assert.Equal(expected, bond.Stage);
    }

    [Fact]
    public void Feeding_adds_two()
    {
        var bond = new Bond(T0);
        bond.Feed(T0);
        Assert.Equal(2f, bond.Value);
    }

    [Fact]
    public void Petting_is_capped_per_day()
    {
        var bond = new Bond(T0);
        for (var i = 0; i < Bond.DailyPetCap + 5; i++)
            bond.Pet(T0.AddMinutes(i));

        Assert.Equal(Bond.DailyPetCap, bond.Value);

        // Next day the cap resets.
        Assert.True(bond.Pet(T0.AddDays(1)));
        Assert.Equal(Bond.DailyPetCap + 1, bond.Value);
    }

    [Fact]
    public void No_decay_within_24h_of_contact()
    {
        var bond = new Bond(T0, 50);
        bond.Tick(T0.AddHours(23), userPresent: false);
        Assert.Equal(50f, bond.Value);
    }

    [Fact]
    public void Decays_one_per_day_after_24h_then_three_per_day()
    {
        var bond = new Bond(T0, 50);
        bond.Tick(T0.AddHours(24), userPresent: false);   // decay starts, nothing elapsed yet
        bond.Tick(T0.AddHours(36), userPresent: false);   // 12h at -1/day
        Assert.Equal(49.5f, bond.Value, precision: 2);

        bond.Tick(T0.AddHours(60), userPresent: false);   // 24h more, now in the -3/day band
        Assert.Equal(46.5f, bond.Value, precision: 2);
    }

    [Fact]
    public void Presence_freezes_decay_and_gives_daily_point()
    {
        var bond = new Bond(T0, 50);
        bond.Tick(T0, userPresent: true);                  // first daily presence: +1
        Assert.Equal(51f, bond.Value);

        bond.Tick(T0.AddHours(1), userPresent: true);      // same day: no extra
        Assert.Equal(51f, bond.Value);

        // Two days of silence, then the user shows up: decay happened in between is not applied retroactively
        // because Tick was not called; the presence itself must not reduce the value.
        bond.Tick(T0.AddDays(2), userPresent: true);
        Assert.True(bond.Value >= 51f);
    }

    [Fact]
    public void DebugSet_sets_value_clamps_and_counts_as_fresh_contact()
    {
        var bond = new Bond(T0, 10);
        var later = T0.AddDays(3);
        bond.DebugSet(70, later);
        Assert.Equal(70f, bond.Value);
        Assert.Equal(BondStage.Trusting, bond.Stage);

        bond.Tick(later.AddHours(23), userPresent: false);   // no decay within 24h of the set
        Assert.Equal(70f, bond.Value);

        bond.DebugSet(250, later);
        Assert.Equal(100f, bond.Value);
        bond.DebugSet(-5, later);
        Assert.Equal(0f, bond.Value);
    }
}
