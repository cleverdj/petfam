namespace PetFam.Core.World;

/// <summary>
/// Sunrise and sunset for a place and date, computed on the computer (the standard sunrise equation, good to a
/// minute or two). No network: works with the weather off.
/// </summary>
public static class Sun
{
    private static readonly DateTime J2000 = new(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Sunrise and sunset (UTC) on the given UTC date at the place. Null when the sun does not rise (polar night);
    /// whole-day values when it does not set (midnight sun).
    /// </summary>
    public static (DateTime RiseUtc, DateTime SetUtc)? Times(DateOnly dateUtc, double latitude, double longitude)
    {
        static double Rad(double d) => d * Math.PI / 180;
        static double Deg(double r) => r * 180 / Math.PI;

        var midnight = new DateTime(dateUtc.Year, dateUtc.Month, dateUtc.Day, 0, 0, 0, DateTimeKind.Utc);
        var n = Math.Ceiling((midnight - J2000).TotalDays + 0.0008);
        var jStar = n - longitude / 360.0;
        var m = (357.5291 + 0.98560028 * jStar) % 360;
        var c = 1.9148 * Math.Sin(Rad(m)) + 0.02 * Math.Sin(Rad(2 * m)) + 0.0003 * Math.Sin(Rad(3 * m));
        var lambda = (m + c + 180 + 102.9372) % 360;
        var transit = jStar + 0.0053 * Math.Sin(Rad(m)) - 0.0069 * Math.Sin(Rad(2 * lambda));
        var sinDecl = Math.Sin(Rad(lambda)) * Math.Sin(Rad(23.4397));
        var cosDecl = Math.Cos(Math.Asin(sinDecl));
        var cosHour = (Math.Sin(Rad(-0.833)) - Math.Sin(Rad(latitude)) * sinDecl) / (Math.Cos(Rad(latitude)) * cosDecl);
        if (cosHour > 1) return null;                                   // polar night
        var hourAngle = cosHour < -1 ? 180 : Deg(Math.Acos(cosHour));   // < -1: midnight sun
        return (J2000.AddDays(transit - hourAngle / 360), J2000.AddDays(transit + hourAngle / 360));
    }
}

/// <summary>Where in the day it is, for the look of the desktop and the pets' habits.</summary>
public enum DayPhase
{
    Night,
    Dawn,
    Day,
    Dusk,
}

/// <summary>
/// Daylight from the computer's clock and the sun at the user's place: 1 in full day, 0 at night, easing through
/// about forty minutes of twilight around sunrise and sunset.
/// </summary>
public sealed class DayClock
{
    public const double TwilightMinutes = 40;

    private DateOnly _cachedFor;
    private (DateTime RiseUtc, DateTime SetUtc)? _sun;

    /// <summary>Where the sun times are computed for (the weather place). Default: Seoul.</summary>
    public double Latitude { get; private set; } = 37.5665;
    public double Longitude { get; private set; } = 126.9780;

    /// <summary>Test option and tray: pretend it is this local time of day (null: the real clock).</summary>
    public TimeSpan? Pretend { get; set; }

    public void SetPlace(double latitude, double longitude)
    {
        Latitude = latitude;
        Longitude = longitude;
        _cachedFor = default;
    }

    /// <summary>Today's sunrise and sunset in the computer's local time (null in polar night).</summary>
    public (DateTime Rise, DateTime Set)? SunToday(DateTime nowUtc)
    {
        var s = SunAround(nowUtc);
        return s is { } v ? (v.RiseUtc.ToLocalTime(), v.SetUtc.ToLocalTime()) : null;
    }

    private (DateTime RiseUtc, DateTime SetUtc)? SunAround(DateTime nowUtc)
    {
        // The local day decides which sunrise/sunset pair is "today".
        var localDate = DateOnly.FromDateTime(nowUtc.ToLocalTime());
        if (localDate != _cachedFor)
        {
            _cachedFor = localDate;
            // Compute for the UTC date of local noon, which is the right solar day for the local calendar day.
            var localNoon = new DateTime(localDate.Year, localDate.Month, localDate.Day, 12, 0, 0, DateTimeKind.Local);
            _sun = Sun.Times(DateOnly.FromDateTime(localNoon.ToUniversalTime()), Latitude, Longitude);
        }
        return _sun;
    }

    /// <summary>The time used for day and night: the real clock, or the pretend time of day today.</summary>
    public DateTime EffectiveUtc(DateTime nowUtc)
    {
        if (Pretend is not { } t) return nowUtc;
        var local = nowUtc.ToLocalTime();
        return DateTime.SpecifyKind(local.Date + t, DateTimeKind.Local).ToUniversalTime();
    }

    /// <summary>0 at night … 1 in full day.</summary>
    public float Daylight(DateTime nowUtc)
    {
        var now = EffectiveUtc(nowUtc);
        var sun = SunAround(now);
        if (sun is not { } s) return 0f;
        var half = TimeSpan.FromMinutes(TwilightMinutes / 2);
        var up = Ramp(now, s.RiseUtc - half, s.RiseUtc + half);
        var down = 1f - Ramp(now, s.SetUtc - half, s.SetUtc + half);
        return Math.Min(up, down);
    }

    public DayPhase Phase(DateTime nowUtc)
    {
        var now = EffectiveUtc(nowUtc);
        var d = Daylight(nowUtc);
        if (d >= 0.999f) return DayPhase.Day;
        if (d <= 0.001f) return DayPhase.Night;
        var local = now.ToLocalTime();
        return local.Hour < 12 ? DayPhase.Dawn : DayPhase.Dusk;
    }

    private static float Ramp(DateTime t, DateTime from, DateTime to)
    {
        if (t <= from) return 0f;
        if (t >= to) return 1f;
        var x = (float)((t - from).TotalSeconds / (to - from).TotalSeconds);
        return x * x * (3 - 2 * x);
    }
}
