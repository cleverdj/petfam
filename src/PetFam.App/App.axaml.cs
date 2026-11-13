using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using PetFam.Core;
using PetFam.Core.Pets;
using PetFam.Core.World;

namespace PetFam.Desktop;

/// <summary>
/// The app and its tray menu. The menu is built in code so it can be rebuilt in the other language
/// (English by default, Korean as an option).
/// </summary>
public partial class App : Application
{
    private AppHost? _host;
    private NativeMenu? _menu;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _host = new AppHost(desktop);
            desktop.Exit += (_, _) => _host.Stop();
            _host.Start();
            _menu = TrayIcon.GetIcons(this)?.FirstOrDefault()?.Menu;
            BuildMenu();
            L.Changed += BuildMenu;
            _host.PetsMenuChanged += RefreshPetsMenu;
            _host.MenuChanged += () => Dispatcher.UIThread.Post(BuildMenu);
            // Locations change as pets move around: refresh the labels every couple of seconds.
            new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => RefreshPetsMenu()).Start();
        }

        base.OnFrameworkInitializationCompleted();
    }

    // ---------------------------------------------------------------- the menu

    private static NativeMenuItem Item(string header, Action click)
    {
        var item = new NativeMenuItem(header);
        item.Click += (_, _) => click();
        return item;
    }

    private static NativeMenuItem Sub(string header, params NativeMenuItemBase[] items)
    {
        var menu = new NativeMenu();
        foreach (var i in items) menu.Items.Add(i);
        return new NativeMenuItem(header) { Menu = menu };
    }

    private void BuildMenu()
    {
        if (_host is null || _menu is null) return;
        var h = _host;
        _menu.Items.Clear();
        _petMenus.Clear();
        _petsKey = "";
        _placeItems.Clear();

        _petsItem = new NativeMenuItem(L.S("My pets (where they are · call)", "우리 아이들 (어디 있나 · 부르기)")) { Menu = new NativeMenu() };
        _menu.Items.Add(_petsItem);
        _menu.Items.Add(BuildWeatherMenu());
        _menu.Items.Add(new NativeMenuItemSeparator());

        _menu.Items.Add(Item(L.S("Put out a food bowl", "밥그릇 놓기"), h.FeedAll));
        _menu.Items.Add(Item(L.S("Build a new house (click a house to decorate)", "새 집 짓기 (집을 누르면 꾸미기)"), h.BuildHouse));
        var walls = OfficialHabitat.All.Select(w => (NativeMenuItemBase)Item(w.Name.Text, () => h.UseOfficialWallpaper(w.Id))).ToList();
        if (h.CanRestoreWallpaper)
        {
            walls.Add(new NativeMenuItemSeparator());
            walls.Add(Item(L.S("Back to my own wallpaper", "원래 바탕화면으로"), h.RestoreWallpaper));
        }
        _menu.Items.Add(Sub(L.S("PetFam wallpapers (pets know every spot)", "PetFam 바탕화면 (펫이 구석구석 알아요)"), walls.ToArray()));
        var fixLabel = L.S("Fix the habitat (floor, water, …)", "서식지 고치기 (바닥, 물, …)");
        if (h.Monitors.Count > 1)
            _menu.Items.Add(Sub(fixLabel, h.Monitors.Select(m => Item(m.Name, () => h.OpenHabitatEditor(m.Index))).ToArray()));
        else
            _menu.Items.Add(Item(fixLabel, () => h.OpenHabitatEditor(0)));
        _menu.Items.Add(Item(L.S("Pet everyone", "쓰다듬기"), h.StrokeAll));
        _menu.Items.Add(Item(L.S("Towel dry", "수건으로 닦아주기"), h.TowelAll));
        _menu.Items.Add(Sub(L.S("Add a pet", "펫 추가하기"),
            Item(L.S("Adopt a dog (choose a breed)", "강아지 데려오기 (품종 고르기)"), () => h.AdoptPet(PetSpecies.Dog)),
            Item(L.S("Adopt a cat (choose a breed)", "고양이 데려오기 (품종 고르기)"), () => h.AdoptPet(PetSpecies.Cat))));

        var english = new NativeMenuItem("English") { ToggleType = NativeMenuItemToggleType.Radio, IsChecked = !L.Ko };
        english.Click += (_, _) => h.SetLanguage(Language.English);
        var korean = new NativeMenuItem("한국어") { ToggleType = NativeMenuItemToggleType.Radio, IsChecked = L.Ko };
        korean.Click += (_, _) => h.SetLanguage(Language.Korean);
        _menu.Items.Add(Sub("Language / 언어", english, korean));
        _menu.Items.Add(new NativeMenuItemSeparator());

        _menu.Items.Add(Item(L.S("(Test) Soak in water", "(테스트) 물에 빠뜨리기"), h.DebugSoak));
        _menu.Items.Add(Item(L.S("(Test) Cover in mud", "(테스트) 진흙 묻히기"), h.DebugMud));
        _menu.Items.Add(Sub(L.S("(Test) Bond", "(테스트) 유대감"),
            Item(L.S("Raise by 20", "+20 올리기"), () => h.DebugBond(add: 20f)),
            Item(L.S("Lower by 20", "-20 내리기"), () => h.DebugBond(add: -20f)),
            new NativeMenuItemSeparator(),
            Item(L.S("Wary (15)", "경계 (15)"), () => h.DebugBond(set: 15f)),
            Item(L.S("Curious (30)", "호기심 (30)"), () => h.DebugBond(set: 30f)),
            Item(L.S("Familiar (50)", "친숙 (50)"), () => h.DebugBond(set: 50f)),
            Item(L.S("Trusting (70)", "신뢰 (70)"), () => h.DebugBond(set: 70f)),
            Item(L.S("Family (100, everything unlocked)", "가족 (100, 전부 열림)"), () => h.DebugBond(set: 100f)),
            Item(L.S("Reset (0)", "초기화 (0)"), () => h.DebugBond(set: 0f))));
        _menu.Items.Add(Sub(L.S("(Test) Age", "(테스트) 나이"),
            Item(L.S("A day passes (+1 day)", "하루 지나기 (+1일)"), () => h.DebugAge(AppHost.AgeStep.Day)),
            Item(L.S("Next life stage", "다음 단계로"), () => h.DebugAge(AppHost.AgeStep.Stage)),
            Item(L.S("Back to a baby", "새끼로 되돌리기"), () => h.DebugAge(AppHost.AgeStep.Baby))));
        _menu.Items.Add(Item(L.S("(Test) Make the pets play", "(테스트) 펫끼리 놀게 하기"), h.DebugPlay));
        _menu.Items.Add(Item(L.S("(Test) Show habitat detection", "(테스트) 서식지 인식 결과 보기"), h.ToggleHabitat));
        _menu.Items.Add(Item(L.S("(Test) Re-detect habitat", "(테스트) 서식지 다시 인식"), () => h.AnalyzeHabitat(force: true)));
        _menu.Items.Add(Item(L.S("Show/hide debug info", "디버그 정보 표시/숨기기"), h.ToggleHud));
        _menu.Items.Add(Item(L.S("(Test) Save a backyard snapshot", "(테스트) 뒷마당 스냅샷 저장"), () => h.SaveSnapshot()));
        _menu.Items.Add(new NativeMenuItemSeparator());
        _menu.Items.Add(Item(L.S("Quit", "종료"), () => (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown()));

        RefreshPetsMenu();
    }

    // ---------------------------------------------------------------- my pets: where each pet is, call, travel mode

    private NativeMenuItem? _petsItem;
    private readonly Dictionary<Guid, PetMenu> _petMenus = new();
    private string _petsKey = "";

    private sealed record PetMenu(NativeMenuItem Item, NativeMenuItem Call, (TravelMode Mode, NativeMenuItem Item)[] Modes);

    private static (TravelMode Mode, string Label)[] TravelLabels() => new[]
    {
        (TravelMode.Follow, L.S("Follow me (comes along when I switch desktops)", "따라다니기 (데스크톱을 바꾸면 따라와요)")),
        (TravelMode.Free, L.S("Roam freely (wanders, drops by now and then)", "자유롭게 (마음대로 다니다 가끔 놀러 와요)")),
        (TravelMode.Home, L.S("Stay home (only on its current desktop)", "집 지키기 (지금 있는 데스크톱에서만 지내요)")),
    };

    private void RefreshPetsMenu()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        RefreshPetsMenuCore();
        if (sw.ElapsedMilliseconds > 15) AppLog.Write($"Tray menu refresh took {sw.ElapsedMilliseconds} ms");
    }

    private void RefreshPetsMenuCore()
    {
        RefreshWeatherMenu();
        if (_host is null || _petsItem?.Menu is not { } sub) return;
        var key = string.Join(",", _host.Pets.Select(p => p.Id));
        if (key != _petsKey)
        {
            _petsKey = key;
            sub.Items.Clear();
            _petMenus.Clear();
            if (_host.Pets.Count == 0) sub.Items.Add(new NativeMenuItem(L.S("Nobody yet", "아직 아무도 없어요")) { IsEnabled = false });
            foreach (var pet in _host.Pets)
            {
                var item = new NativeMenuItem(pet.Name) { Menu = new NativeMenu() };
                var call = new NativeMenuItem(L.S("Call to this desktop", "이 데스크톱으로 부르기"));
                call.Click += (_, _) => { _host?.CallPet(pet); RefreshPetsMenu(); };
                item.Menu.Items.Add(call);
                item.Menu.Items.Add(new NativeMenuItemSeparator());
                var modes = TravelLabels().Select(t =>
                {
                    var m = new NativeMenuItem(t.Label) { ToggleType = NativeMenuItemToggleType.Radio };
                    m.Click += (_, _) => { _host?.SetTravelMode(pet, t.Mode); RefreshPetsMenu(); };
                    item.Menu.Items.Add(m);
                    return (t.Mode, m);
                }).ToArray();
                sub.Items.Add(item);
                _petMenus[pet.Id] = new PetMenu(item, call, modes);
            }
        }
        foreach (var pet in _host.Pets)
        {
            if (!_petMenus.TryGetValue(pet.Id, out var pm)) continue;
            var header = _host.WhereIs(pet);
            if (pm.Item.Header != header) pm.Item.Header = header;
            var canCall = _host.HasDesktops && !_host.IsHere(pet) && pet.Trip is null;
            if (pm.Call.IsEnabled != canCall) pm.Call.IsEnabled = canCall;
            foreach (var (mode, m) in pm.Modes)
                if (m.IsChecked != (pet.Travel == mode)) m.IsChecked = pet.Travel == mode;
        }
    }

    // ---------------------------------------------------------------- weather

    private NativeMenuItem? _weatherItem;
    private NativeMenuItem? _weatherNow;
    private NativeMenuItem? _sunNow;
    private NativeMenuItem? _weatherToggle;
    private readonly List<(WeatherService.Place Place, NativeMenuItem Item)> _placeItems = new();

    private NativeMenuItem BuildWeatherMenu()
    {
        var sub = new NativeMenu();
        _weatherItem = new NativeMenuItem(L.S("Weather", "날씨")) { Menu = sub };
        _weatherNow = new NativeMenuItem("") { IsEnabled = false };
        sub.Items.Add(_weatherNow);
        _sunNow = new NativeMenuItem("") { IsEnabled = false };
        sub.Items.Add(_sunNow);
        _weatherToggle = new NativeMenuItem(L.S("Use the real weather", "실제 날씨 반영하기")) { ToggleType = NativeMenuItemToggleType.CheckBox };
        _weatherToggle.Click += (_, _) => _host?.ToggleRealWeather();
        sub.Items.Add(_weatherToggle);

        var places = new NativeMenu();
        foreach (var place in WeatherService.Places)
        {
            var item = new NativeMenuItem(place.Name) { ToggleType = NativeMenuItemToggleType.Radio };
            item.Click += (_, _) => _host?.SetWeatherPlace(place);
            places.Items.Add(item);
            _placeItems.Add((place, item));
        }
        sub.Items.Add(new NativeMenuItem(L.S("Place", "지역")) { Menu = places });

        var test = new NativeMenu();
        foreach (var (label, kind) in new (string, string?)[]
                 {
                     (L.S("Back to the real weather", "실제 날씨로 돌아가기"), null), (L.S("Clear", "맑음"), "clear"), (L.S("Cloudy", "흐림"), "cloudy"),
                     (L.S("Fog", "안개"), "fog"), (L.S("Drizzle", "이슬비"), "drizzle"), (L.S("Rain", "비"), "rain"), (L.S("Heavy rain", "폭우"), "heavyrain"),
                     (L.S("Thunderstorm", "뇌우"), "thunder"), (L.S("Snow", "눈"), "snow"), (L.S("Heavy snow", "폭설"), "heavysnow"),
                 })
            test.Items.Add(Item(label, () => _host?.PretendWeather(kind)));
        sub.Items.Add(new NativeMenuItemSeparator());
        sub.Items.Add(new NativeMenuItem(L.S("(Test) Try a weather", "(테스트) 날씨 바꿔 보기")) { Menu = test });

        var times = new NativeMenu();
        times.Items.Add(Item(L.S("Back to the real time", "실제 시간으로 돌아가기"), () => _host?.PretendTime(null)));
        times.Items.Add(Item(L.S("Morning (08:00)", "아침 (08:00)"), () => _host?.PretendTime(TimeSpan.FromHours(8))));
        times.Items.Add(Item(L.S("Noon (12:00)", "한낮 (12:00)"), () => _host?.PretendTime(TimeSpan.FromHours(12))));
        times.Items.Add(Item(L.S("Sunset", "해 질 녘"), () => _host?.PretendTime(_host?.SunsetToday)));
        times.Items.Add(Item(L.S("Night (23:00)", "밤 (23:00)"), () => _host?.PretendTime(TimeSpan.FromHours(23))));
        sub.Items.Add(new NativeMenuItem(L.S("(Test) Try a time of day", "(테스트) 시간대 바꿔 보기")) { Menu = times });
        RefreshWeatherMenu();
        return _weatherItem;
    }

    private void RefreshWeatherMenu()
    {
        if (_host is null || _weatherNow is null || _weatherToggle is null) return;
        var line = _host.WeatherLine;
        if (_weatherNow.Header != line) _weatherNow.Header = line;
        var sunLine = _host.SunLine;
        if (_sunNow is not null && _sunNow.Header != sunLine) _sunNow.Header = sunLine;
        var head = L.S($"Weather: {line}", $"날씨: {line}");
        if (_weatherItem is not null && _weatherItem.Header != head) _weatherItem.Header = head;
        if (_weatherToggle.IsChecked != _host.WeatherEnabled) _weatherToggle.IsChecked = _host.WeatherEnabled;
        foreach (var (place, item) in _placeItems)
            if (item.IsChecked != (place.Id == _host.WeatherPlaceId)) item.IsChecked = place.Id == _host.WeatherPlaceId;
    }
}
