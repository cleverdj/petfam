namespace PetFam.Core.World;

/// <summary>What the sky is doing, simplified for the desktop (rain covers drizzle and showers, snow covers sleet).</summary>
public enum WeatherKind
{
    Clear,
    Cloudy,
    Fog,
    Rain,
    Thunder,
    Snow,
}

/// <summary>The weather now, from the real weather where the user lives (or a test override).</summary>
public sealed record WeatherNow(WeatherKind Kind, float Intensity, float TemperatureC, bool IsDay, string Place, DateTime FetchedUtc)
{
    public static readonly WeatherNow Clear = new(WeatherKind.Clear, 0f, 20f, true, "", DateTime.MinValue);

    /// <summary>Rain or a thunderstorm: fur gets wet outdoors.</summary>
    public bool Raining => Kind is WeatherKind.Rain or WeatherKind.Thunder;

    public bool Snowing => Kind == WeatherKind.Snow;

    /// <summary>
    /// From a WMO weather code (what Open-Meteo reports): 0 clear, 1-3 clouds, 45/48 fog, 51-67 drizzle and rain,
    /// 71-77 snow, 80-82 rain showers, 85/86 snow showers, 95-99 thunderstorms.
    /// </summary>
    public static WeatherNow FromWmo(int code, float temperatureC, bool isDay, string place, DateTime nowUtc)
    {
        var (kind, intensity) = code switch
        {
            0 or 1 => (WeatherKind.Clear, 0f),
            2 or 3 => (WeatherKind.Cloudy, code == 3 ? 0.6f : 0.3f),
            45 or 48 => (WeatherKind.Fog, 0.7f),
            51 or 53 or 55 => (WeatherKind.Rain, 0.2f + (code - 51) * 0.05f),
            56 or 57 => (WeatherKind.Rain, 0.35f),
            61 or 66 => (WeatherKind.Rain, 0.4f),
            63 => (WeatherKind.Rain, 0.7f),
            65 or 67 => (WeatherKind.Rain, 1f),
            71 => (WeatherKind.Snow, 0.4f),
            73 => (WeatherKind.Snow, 0.7f),
            75 => (WeatherKind.Snow, 1f),
            77 => (WeatherKind.Snow, 0.3f),
            80 => (WeatherKind.Rain, 0.5f),
            81 => (WeatherKind.Rain, 0.75f),
            82 => (WeatherKind.Rain, 1f),
            85 => (WeatherKind.Snow, 0.5f),
            86 => (WeatherKind.Snow, 0.9f),
            95 => (WeatherKind.Thunder, 0.8f),
            96 or 99 => (WeatherKind.Thunder, 1f),
            _ => (WeatherKind.Cloudy, 0.3f),
        };
        return new WeatherNow(kind, intensity, temperatureC, isDay, place, nowUtc);
    }

    /// <summary>Test option and tray: a made-up weather.</summary>
    public static WeatherNow Pretend(WeatherKind kind, float intensity, string? place = null) =>
        new(kind, intensity, kind == WeatherKind.Snow ? -2f : 14f, true, place ?? L.S("Test", "테스트"), DateTime.UtcNow);

    public static string Name(WeatherKind kind) => kind switch
    {
        WeatherKind.Clear => L.S("Clear", "맑음"),
        WeatherKind.Cloudy => L.S("Cloudy", "흐림"),
        WeatherKind.Fog => L.S("Fog", "안개"),
        WeatherKind.Rain => L.S("Rain", "비"),
        WeatherKind.Thunder => L.S("Thunderstorm", "뇌우"),
        _ => L.S("Snow", "눈"),
    };

    /// <summary>"Seoul · Rain 12°C" for the tray.</summary>
    public string Describe()
    {
        var what = Kind switch
        {
            WeatherKind.Rain when Intensity >= 0.9f => L.S("Heavy rain", "폭우"),
            WeatherKind.Rain when Intensity <= 0.3f => L.S("Drizzle", "이슬비"),
            WeatherKind.Snow when Intensity >= 0.9f => L.S("Heavy snow", "폭설"),
            _ => Name(Kind),
        };
        var place = Place.Length > 0 ? Place + " · " : "";
        return $"{place}{what} {TemperatureC:0}°C";
    }
}
