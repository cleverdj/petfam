using PetFam.Core;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using PetFam.Core.World;

namespace PetFam.Desktop;

/// <summary>
/// The real weather for one place, from Open-Meteo (free, no account or key). Only the place's coordinates are sent;
/// nothing about the user or the computer. Refreshes every 15 minutes, retries sooner after a failure, and keeps the
/// last good reading while offline.
/// </summary>
public sealed class WeatherService
{
    /// <summary>A place to get the weather for. Id is stable (saved); the label follows the language.</summary>
    public sealed record Place(string Id, Loc Label, double Latitude, double Longitude)
    {
        public string Name => Label.Text;
    }

    private static Place P(string id, string en, string ko, double lat, double lon) => new(id, new Loc(en, ko), lat, lon);

    /// <summary>Cities to pick from in the tray.</summary>
    public static readonly Place[] Places =
    {
        P("seoul", "Seoul", "서울", 37.5665, 126.9780),
        P("pangyo", "Pangyo", "판교", 37.3943, 127.1107),
        P("suwon", "Suwon", "수원", 37.2636, 127.0286),
        P("incheon", "Incheon", "인천", 37.4563, 126.7052),
        P("daejeon", "Daejeon", "대전", 36.3504, 127.3845),
        P("gwangju", "Gwangju", "광주", 35.1595, 126.8526),
        P("daegu", "Daegu", "대구", 35.8714, 128.6014),
        P("busan", "Busan", "부산", 35.1796, 129.0756),
        P("jeju", "Jeju", "제주", 33.4996, 126.5312),
        P("tokyo", "Tokyo", "도쿄", 35.6762, 139.6503),
        P("singapore", "Singapore", "싱가포르", 1.3521, 103.8198),
        P("sydney", "Sydney", "시드니", -33.8688, 151.2093),
        P("london", "London", "런던", 51.5072, -0.1276),
        P("paris", "Paris", "파리", 48.8566, 2.3522),
        P("berlin", "Berlin", "베를린", 52.5200, 13.4050),
        P("new-york", "New York", "뉴욕", 40.7128, -74.0060),
        P("san-francisco", "San Francisco", "샌프란시스코", 37.7749, -122.4194),
        P("los-angeles", "Los Angeles", "로스앤젤레스", 34.0522, -118.2437),
        P("toronto", "Toronto", "토론토", 43.6532, -79.3832),
    };

    /// <summary>By id, or by a name saved by an older version ("서울").</summary>
    public static Place? Find(string idOrName) =>
        Places.FirstOrDefault(p => p.Id == idOrName || p.Label.En == idOrName || p.Label.Ko == idOrName);

    /// <summary>A sensible first place from the computer's region (the user can change it in the tray).</summary>
    public static Place DefaultPlace()
    {
        string region;
        try { region = System.Globalization.RegionInfo.CurrentRegion.TwoLetterISORegionName; }
        catch { region = ""; }
        return Find(region switch
        {
            "KR" => "seoul", "JP" => "tokyo", "SG" => "singapore", "AU" => "sydney", "GB" => "london", "FR" => "paris",
            "DE" => "berlin", "US" => "new-york", "CA" => "toronto", _ => "seoul",
        })!;
    }

    public static readonly TimeSpan RefreshEvery = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(3);

    private static readonly HttpClient Http = CreateClient();

    private DateTime _nextFetchUtc = DateTime.MinValue;
    private bool _fetching;

    public Place Location { get; set; } = DefaultPlace();

    /// <summary>False: real weather is off (the desktop stays clear).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Last good reading, if any.</summary>
    public WeatherNow? Latest { get; private set; }

    public string? LastError { get; private set; }

    /// <summary>Raised on the UI thread's caller context when a new reading arrives.</summary>
    public event Action<WeatherNow>? Updated;

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("PetFam-desktop-pet/0.1");
        return c;
    }

    /// <summary>Call often (every few seconds); fetches only when due.</summary>
    public void Poll(Action<Action> post)
    {
        if (!Enabled || _fetching || DateTime.UtcNow < _nextFetchUtc) return;
        _fetching = true;
        var place = Location;
        Task.Run(async () =>
        {
            WeatherNow? reading = null;
            string? error = null;
            try { reading = await Fetch(place); }
            catch (Exception ex) { error = ex.Message; }
            post(() =>
            {
                _fetching = false;
                if (reading is not null && place == Location)
                {
                    Latest = reading;
                    LastError = null;
                    _nextFetchUtc = DateTime.UtcNow + RefreshEvery;
                    Updated?.Invoke(reading);
                }
                else
                {
                    LastError = error ?? "no reading";
                    _nextFetchUtc = DateTime.UtcNow + RetryAfter;
                    AppLog.Write($"Weather: fetch for {place.Name} failed: {LastError}");
                }
            });
        });
    }

    /// <summary>Fetch again now (place changed, or turned back on).</summary>
    public void Refresh() => _nextFetchUtc = DateTime.MinValue;

    public static async Task<WeatherNow> Fetch(Place place)
    {
        var url = string.Create(CultureInfo.InvariantCulture,
            $"https://api.open-meteo.com/v1/forecast?latitude={place.Latitude:0.####}&longitude={place.Longitude:0.####}&current=temperature_2m,weather_code,is_day&timezone=auto");
        using var stream = await Http.GetStreamAsync(url);
        using var doc = await JsonDocument.ParseAsync(stream);
        var current = doc.RootElement.GetProperty("current");
        var code = current.GetProperty("weather_code").GetInt32();
        var temp = (float)current.GetProperty("temperature_2m").GetDouble();
        var isDay = current.TryGetProperty("is_day", out var d) && d.GetInt32() == 1;
        return WeatherNow.FromWmo(code, temp, isDay, place.Name, DateTime.UtcNow);
    }
}
