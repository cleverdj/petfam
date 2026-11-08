using PetFam.Core.Geometry;
using PetFam.Core.Persistence;
using PetFam.Core.Pets;

namespace PetFam.Core.Tests;

public class GrowthTests
{
    private static readonly DateTime T0 = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Day0 = new(2026, 11, 8);

    [Fact]
    public void Adopted_pets_arrive_as_two_month_old_babies()
    {
        var pet = new Pet(PetSpecies.Dog, "Peanut", T0, new Random(1));
        Assert.Equal(LifeStage.Baby, pet.Stage);
        Assert.Equal("2 months old", pet.Growth.AgeLabel(pet.Species));
        Assert.Equal(0f, pet.Growth.BodyGrowth);
    }

    [Fact]
    public void A_day_counts_after_ten_active_minutes_and_only_once()
    {
        var g = new Growth(T0);
        Assert.Equal(Growth.Event.None, g.Tick(9 * 60f, userActive: true, Day0));
        Assert.Equal(0, g.Days);
        Assert.Equal(Growth.Event.NewDay, g.Tick(61f, userActive: true, Day0));
        Assert.Equal(1, g.Days);
        g.Tick(3 * 3600f, userActive: true, Day0);                 // the rest of the day does not count again
        Assert.Equal(1, g.Days);
    }

    [Fact]
    public void Days_away_from_the_computer_do_not_age_the_pet()
    {
        var g = new Growth(T0);
        for (var d = 0; d < 10; d++) g.Tick(8 * 3600f, userActive: false, Day0.AddDays(d));
        Assert.Equal(0, g.Days);
        g.Tick(11 * 60f, userActive: true, Day0.AddDays(30));
        Assert.Equal(1, g.Days);
    }

    [Theory]
    [InlineData(0, LifeStage.Baby)]
    [InlineData(14, LifeStage.Baby)]
    [InlineData(15, LifeStage.Junior)]
    [InlineData(45, LifeStage.Junior)]
    [InlineData(46, LifeStage.Adult)]
    [InlineData(365, LifeStage.Adult)]
    [InlineData(366, LifeStage.Senior)]
    public void Stages_follow_the_days_together(int days, LifeStage stage) => Assert.Equal(stage, Growth.StageAt(days));

    [Fact]
    public void Growing_up_is_celebrated_once_per_stage_and_on_anniversaries()
    {
        var g = new Growth(T0, Growth.JuniorAt - 1);
        var e = g.Tick(11 * 60f, true, Day0);
        Assert.Equal(Growth.Event.GrewUp, e);
        Assert.Equal(LifeStage.Junior, g.Stage);
        Assert.Equal(Growth.Event.NewDay, g.Tick(11 * 60f, true, Day0.AddDays(1)));

        var h = new Growth(T0, 29);
        Assert.Equal(Growth.Event.Anniversary, h.Tick(11 * 60f, true, Day0));
        Assert.True(h.IsAnniversaryToday);
    }

    [Fact]
    public void Babies_tire_faster_and_seniors_recover_slower_than_adults()
    {
        float Drain(LifeStage s)
        {
            var n = new Needs();
            n.Tick(3600f, true, sleeping: false, StageTraits.Of(s, PetSpecies.Dog));
            return 100f - n.Energy;
        }
        float Regen(LifeStage s)
        {
            var n = new Needs { Energy = 10f };
            n.Tick(600f, true, sleeping: true, StageTraits.Of(s, PetSpecies.Dog));
            return n.Energy - 10f;
        }
        Assert.True(Drain(LifeStage.Baby) > Drain(LifeStage.Adult) * 1.4f);
        Assert.True(Regen(LifeStage.Senior) < Regen(LifeStage.Adult) * 0.8f);
        Assert.True(StageTraits.Of(LifeStage.Adult, PetSpecies.Cat).EnergyDecay
                    > StageTraits.Of(LifeStage.Adult, PetSpecies.Dog).EnergyDecay);   // cats sleep more
    }

    [Fact]
    public void Age_changes_speed_and_jump_height()
    {
        var baby = new Pet(PetSpecies.Cat, "a", T0, new Random(1));
        var adult = new Pet(PetSpecies.Cat, "b", T0, new Random(1)) { Growth = new Growth(T0, Growth.AdultAt) };
        var senior = new Pet(PetSpecies.Cat, "c", T0, new Random(1)) { Growth = new Growth(T0, Growth.SeniorAt + 10) };
        Assert.True(baby.WalkSpeed() < adult.WalkSpeed());
        Assert.True(senior.RunMultiplier() < adult.RunMultiplier());
        baby.Lift = 50f;
        adult.Lift = 50f;
        Assert.True(baby.Lift < adult.Lift);
    }

    [Fact]
    public void Babies_cannot_say_meow_properly()
    {
        var baby = new Pet(PetSpecies.Cat, "a", T0, new Random(1));
        baby.Say("Meow!");
        Assert.Equal("Mew!", baby.Bubble);
    }

    [Fact]
    public void Age_is_saved_and_pets_from_old_saves_are_adults()
    {
        var pet = new Pet(PetSpecies.Dog, "Peanut", T0, new Random(1)) { Growth = new Growth(T0, 20) { ActiveMinutes = 4f, ActiveDate = Day0 } };
        var save = SaveStore.ToSave(new[] { pet }, T0);
        var back = SaveStore.Restore(save.Pets[0], T0, new Random(2));
        Assert.Equal(20, back.Growth.Days);
        Assert.Equal(4f, back.Growth.ActiveMinutes);
        Assert.Equal(LifeStage.Junior, back.Growth.AnnouncedStage);

        save.Pets[0].Days = null;   // a save from before growth existed
        var legacy = SaveStore.Restore(save.Pets[0], T0, new Random(2));
        Assert.Equal(LifeStage.Adult, legacy.Stage);
    }
}
