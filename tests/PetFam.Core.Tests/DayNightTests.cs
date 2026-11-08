using PetFam.Core.World;

namespace PetFam.Core.Tests;

public class DayNightTests
{
    private static void Near(DateTime expectedUtc, DateTime actualUtc, double minutes = 4) =>
        Assert.True(Math.Abs((actualUtc - expectedUtc).TotalMinutes) <= minutes, $"expected {expectedUtc:HH:mm} UTC, got {actualUtc:HH:mm} UTC");

    [Fact]
    public void Sunrise_and_sunset_in_Seoul_in_November()
    {
        // 8 Nov 2026, Seoul: sunrise about 07:01 KST (22:01 UTC the day before), sunset about 17:31 KST (08:31 UTC).
        var t = Sun.Times(new DateOnly(2026, 11, 8), 37.5665, 126.9780)!.Value;
        Near(new DateTime(2026, 11, 7, 22, 1, 0, DateTimeKind.Utc), t.RiseUtc);
        Near(new DateTime(2026, 11, 8, 8, 31, 0, DateTimeKind.Utc), t.SetUtc);
    }

    [Fact]
    public void Long_summer_days_in_London_and_no_sunrise_in_the_arctic_winter()
    {
        // Midsummer in London: sunrise about 04:43 BST (03:43 UTC), sunset about 21:21 BST (20:21 UTC).
        var london = Sun.Times(new DateOnly(2026, 6, 21), 51.5072, -0.1276)!.Value;
        Near(new DateTime(2026, 6, 21, 3, 43, 0, DateTimeKind.Utc), london.RiseUtc);
        Near(new DateTime(2026, 6, 21, 20, 21, 0, DateTimeKind.Utc), london.SetUtc);

        Assert.Null(Sun.Times(new DateOnly(2026, 12, 21), 69.65, 18.96));   // Tromsø: polar night
    }

    [Fact]
    public void Daylight_follows_the_time_of_day()
    {
        // A place whose solar noon is the computer's noon, so the test works in any time zone.
        var now = DateTime.UtcNow;
        var offsetHours = TimeZoneInfo.Local.GetUtcOffset(now).TotalHours;
        var clock = new DayClock();
        clock.SetPlace(20, offsetHours * 15);

        clock.Pretend = TimeSpan.FromHours(12);
        Assert.Equal(1f, clock.Daylight(now));
        Assert.Equal(DayPhase.Day, clock.Phase(now));

        clock.Pretend = TimeSpan.FromHours(0.5);
        Assert.Equal(0f, clock.Daylight(now));
        Assert.Equal(DayPhase.Night, clock.Phase(now));

        // Right at sunset it is half light: dusk.
        clock.Pretend = null;
        var set = clock.SunToday(now)!.Value.Set;
        clock.Pretend = set.TimeOfDay;
        var d = clock.Daylight(now);
        Assert.InRange(d, 0.3f, 0.7f);
        Assert.Equal(DayPhase.Dusk, clock.Phase(now));
    }
}
