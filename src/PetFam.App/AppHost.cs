using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using PetFam.Core;
using PetFam.Core.Geometry;
using PetFam.Core.Persistence;
using PetFam.Core.Pets;
using PetFam.Core.Platform;
using PetFam.Core.World;
using PetFam.Desktop.Rendering;
using PetFam.Desktop.Windows;
using PetFam.Platform.MacOS;
using PetFam.Platform.Windows;

namespace PetFam.Desktop;

/// <summary>
/// Owns the simulation, the layer windows and the frame timer. There is a cell per virtual desktop and monitor;
/// each monitor has its own backyard and overlay window showing that monitor's cell of the current desktop.
/// The main monitor's cell (<see cref="_cell"/>) is where tray actions (food, houses, new pets) happen.
/// </summary>
public sealed class AppHost
{
    private const double ActiveFrameSeconds = 1.0 / 30.0;
    private const double CalmFrameSeconds = 1.0 / 12.0;
    private const float AutosaveSeconds = 60f;

    private readonly IClassicDesktopStyleApplicationLifetime _lifetime;
    private readonly IPlatform _platform;
    private readonly IWindowLayering _layering;
    private readonly Simulation _sim;
    /// <summary>Test option: PETFAM_SAVE=&lt;file&gt; uses another save file (leaves the real pets alone).</summary>
    private readonly string _savePath = Environment.GetEnvironmentVariable("PETFAM_SAVE") is { Length: > 0 } sp
        ? sp
        : Path.Combine(AppLog.DataDirectory, "save.json");

    private bool _needsOnboarding;
    private OnboardingWindow? _onboarding;
    private readonly Stopwatch _clock = new();

    private BackyardWindow? _backyard;   // main monitor
    private OverlayWindow? _overlay;     // main monitor

    /// <summary>One per monitor, the main monitor first: its windows and the cell they show.</summary>
    private sealed class MonitorView
    {
        public required string MonitorId { get; init; }
        public required Screen Screen { get; set; }
        public required BackyardWindow Backyard { get; init; }
        public required OverlayWindow Overlay { get; init; }
        public required Cell Cell { get; set; }
        public bool OverlayHadContent { get; set; }
    }

    private readonly List<MonitorView> _views = new();

    private sealed record Monitor(string Id, RectI Bounds, RectI Working, Screen Screen);

    private List<Monitor> _monitors = new();
    private string _monitorKey = "";
    private float _sinceMonitorPoll;
    public const string MainMonitorId = "primary";
    private readonly Dictionary<Guid, FrontWindow> _fronts = new();
    private Cell? _cell;
    private FrameClock? _timer;
    private double _lastTime;
    private float _sinceSave;

    public AppHost(IClassicDesktopStyleApplicationLifetime lifetime)
    {
        _lifetime = lifetime;

        if (OperatingSystem.IsWindows())
        {
            var win = new WindowsPlatform();
            win.Log += AppLog.Write;
            _platform = win;
            _layering = win;
        }
        else
        {
            var mac = new MacPlatform();
            mac.Log += AppLog.Write;
            _platform = mac;
            _layering = mac;
        }

        _sim = new Simulation(_platform);
        _sim.Debug.Log = AppLog.Write;
        // Test option: a made-up cursor resting on the first pet on screen (hover checks without moving the mouse).
        // PETFAM_FAKE_HOVER=1, or =<monitor id> (primary, mon-2560-0, ...) for a pet on that monitor.
        if (Environment.GetEnvironmentVariable("PETFAM_FAKE_HOVER") is { Length: > 0 } fakeHover)
            _sim.Debug.CursorOverride = () => _sim.Pets.FirstOrDefault(p => !p.Hidden && _sim.VisibleCellIds.Contains(p.CellId)
                                                                         && (fakeHover == "1" || p.CellId.EndsWith(":" + fakeHover))) is { } p
                ? new Vec2(p.Position.X + (Environment.TickCount64 / 1000 % 2 == 0 || Environment.GetEnvironmentVariable("PETFAM_FAKE_HOVER_BLINK") != "1" ? 0f : 600f), p.Position.Y - 30f) : null;

        // Spike diagnostics.
        if (Environment.GetEnvironmentVariable("PETFAM_NO_PEEK") == "1") _sim.Debug.AllowPeekAlways = false;
        if (double.TryParse(Environment.GetEnvironmentVariable("PETFAM_CALM_FPS"), out var fps) && fps > 0)
            _calmFrameSeconds = 1.0 / fps;
    }

    private double _calmFrameSeconds = CalmFrameSeconds;

    public void Start()
    {
        AppLog.Write("---- PetFam starting ----");
        SdfPetRenderer.Prewarm();
        PetRenderer.PrewarmFonts();

        _backyard = new BackyardWindow();
        _overlay = new OverlayWindow();

        ReadMonitors();
        var desktops = _platform.GetVirtualDesktops();
        var cell = SyncDesktops(desktops);
        _cell = cell;
        _sim.ShowCells(_monitors.Select(m => _sim.World.CellFor(cell.DesktopId, m.Id) ?? cell).ToList(), DateTime.UtcNow);
        AppLog.Write(desktops is null
            ? "Virtual desktops: none (one cell)"
            : $"Virtual desktops: {desktops.Desktops.Count}, on {_sim.World.DescribeDesktop(cell)} ({cell.DesktopId})");

        LoadOrCreatePets(cell);
        StartWeather();

        // Test option: PETFAM_ADOPT=cat,dog,shiba,ragdoll brings extra pets at startup (same-species play).
        foreach (var kind in (Environment.GetEnvironmentVariable("PETFAM_ADOPT") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // A breed id (shiba, ragdoll, ...) or just cat / dog.
            var breed = Breed.All.FirstOrDefault(b => b.Id.Equals(kind, StringComparison.OrdinalIgnoreCase))
                        ?? Breed.Default(kind.StartsWith("c", StringComparison.OrdinalIgnoreCase) ? PetSpecies.Cat : PetSpecies.Dog);
            if (_sim.AdoptPet(breed, cell, DateTime.UtcNow) is { } extra) AppLog.Write($"PETFAM_ADOPT: {extra.Name} ({breed.Name})");
        }

        AnalyzeHabitat();
        if (Environment.GetEnvironmentVariable("PETFAM_SHOW_HABITAT") == "1") _sim.Debug.ShowHabitat = true;

        if (_needsOnboarding || Environment.GetEnvironmentVariable("PETFAM_ONBOARDING") is "1" or "cat")
            DispatcherTimer.RunOnce(ShowOnboarding, TimeSpan.FromSeconds(1));

        // Test option: PETFAM_AGE_DAYS=<days> sets every pet's days together at startup.
        if (int.TryParse(Environment.GetEnvironmentVariable("PETFAM_AGE_DAYS"), out var ageDays))
            foreach (var p in _sim.Pets)
                p.Growth = new Growth(DateTime.UtcNow.AddDays(-ageDays), Math.Max(0, ageDays));

        // Test option: PETFAM_BOND=<0..100> sets every pet's bond at startup.
        if (float.TryParse(Environment.GetEnvironmentVariable("PETFAM_BOND"), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var bond))
        {
            foreach (var p in _sim.Pets) p.Bond.DebugSet(bond, DateTime.UtcNow);
            AppLog.Write($"PETFAM_BOND={bond}: bond set for all pets");
        }

        _wallpaperBitmap = LoadWallpaper();
        foreach (var m in _monitors)
            AddView(m, m.Id == MainMonitorId ? _backyard : new BackyardWindow(), m.Id == MainMonitorId ? _overlay : new OverlayWindow());

        _clock.Start();
        _lastTime = 0;
        _timer = new FrameClock(() => OnFrame(this, EventArgs.Empty), TimeSpan.FromSeconds(_calmFrameSeconds));

        if (Environment.GetEnvironmentVariable("PETFAM_DOOR_SHEET") == "1")
            DispatcherTimer.RunOnce(() =>
            {
                var path = Path.Combine(AppLog.DataDirectory, "door-sheet.png");
                try { SceneSheet.Save(path); AppLog.Write("Door/peek sheet saved: " + path); }
                catch (Exception ex) { AppLog.Write("Door/peek sheet failed: " + ex); }
            }, TimeSpan.FromSeconds(1));

        if (Environment.GetEnvironmentVariable("PETFAM_BREED_SHEET") == "1")
            PoseSheet.QueueBreedSheet(Path.Combine(AppLog.DataDirectory, "breed-sheet.png"));

        if (Environment.GetEnvironmentVariable("PETFAM_CLOSEUP_SHEET") == "1")
            PoseSheet.QueueCloseUpSheet(Path.Combine(AppLog.DataDirectory, "closeup-sheet.png"));

        if (Environment.GetEnvironmentVariable("PETFAM_GROWTH_SHEET") == "1")
            PoseSheet.QueueGrowthSheet(Path.Combine(AppLog.DataDirectory, "growth-sheet.png"));

        if (Environment.GetEnvironmentVariable("PETFAM_TURN_SHEET") is { Length: > 0 } turn)
            PoseSheet.QueueTurnSheet(Path.Combine(AppLog.DataDirectory, "turn-sheet.png"),
                turn.StartsWith("d", StringComparison.OrdinalIgnoreCase) ? PetSpecies.Dog : PetSpecies.Cat);

        // Dev: start a game of fetch with the first dog a few seconds after launch.
        if (Environment.GetEnvironmentVariable("PETFAM_DEMO") == "grow")
            DispatcherTimer.RunOnce(() => DebugAge(AgeStep.Stage), TimeSpan.FromSeconds(4));
        // Dev: the first pet walks off to the next desktop, and is called back a few seconds later.
        if (Environment.GetEnvironmentVariable("PETFAM_DEMO") == "travel")
            DispatcherTimer.RunOnce(() =>
            {
                if (_cell is null || _sim.Pets.FirstOrDefault(p => p.CellId == _cell.Id) is not { } pet) return;
                var to = _sim.World.Neighbor(_cell, +1) ?? _sim.World.Neighbor(_cell, -1);
                if (to is null) { AppLog.Write("Demo travel: only one desktop"); return; }
                PetFam.Core.World.TravelPlanner.Go(_sim, pet, to, TripReason.Visit, run: false, new Random());
                AppLog.Write($"Demo travel: {pet.Name} -> {_sim.World.DescribeDesktop(to)}");
                DispatcherTimer.RunOnce(() => CallPet(pet), TimeSpan.FromSeconds(14));
            }, TimeSpan.FromSeconds(3));
        // Dev: the first pet on the main monitor walks over to the next monitor (and the snapshots show both).
        if (Environment.GetEnvironmentVariable("PETFAM_DEMO") == "monitor")
            DispatcherTimer.RunOnce(() =>
            {
                if (_cell is null || _sim.Pets.FirstOrDefault(p => p.CellId == _cell.Id && !p.Hidden) is not { } pet) return;
                var to = _sim.World.MonitorNeighbor(_cell, +1) ?? _sim.World.MonitorNeighbor(_cell, -1);
                if (to is null) { AppLog.Write("Demo monitor: only one monitor"); return; }
                PetFam.Core.World.TravelPlanner.Go(_sim, pet, to, TripReason.Visit, run: false, new Random());
                AppLog.Write($"Demo monitor: {pet.Name} -> {_sim.World.DescribeCell(to)}");
            }, TimeSpan.FromSeconds(3));
        // Dev: opens the habitat editor and saves what it shows (its own window only) to habitat-editor.png.
        if (Environment.GetEnvironmentVariable("PETFAM_DEMO") is { } hd && hd.StartsWith("habitat"))
            DispatcherTimer.RunOnce(() =>
            {
                OpenHabitatEditor(hd.EndsWith("2") ? 1 : 0);
                DispatcherTimer.RunOnce(() =>
                {
                    if (_habitatEditor?.Content is not Visual v) return;
                    var px = new PixelSize((int)(_habitatEditor.Bounds.Width * _habitatEditor.RenderScaling), (int)(_habitatEditor.Bounds.Height * _habitatEditor.RenderScaling));
                    using var rtb = new RenderTargetBitmap(px, new Vector(96 * _habitatEditor.RenderScaling, 96 * _habitatEditor.RenderScaling));
                    rtb.Render(v);
                    var path = Path.Combine(AppLog.DataDirectory, "habitat-editor.png");
                    rtb.Save(path);
                    AppLog.Write("Habitat editor picture saved: " + path);
                }, TimeSpan.FromSeconds(3));
            }, TimeSpan.FromSeconds(4));
        // Dev: opens the colour editor for the first pet and saves a picture of it to coat-editor.png.
        // Dev: lays one of each hat and pair of glasses on the floor of the main monitor (to see how they look).
        if (Environment.GetEnvironmentVariable("PETFAM_DEMO") == "dropped")
            DispatcherTimer.RunOnce(() =>
            {
                if (_cell is null || _sim.Pets.FirstOrDefault() is not { } owner) return;
                var roam = _cell.Map.RoamArea;
                var ids = new[] { "beanie", "straw-hat", "crown", "ribbon", "flower", "glasses", "sunglasses" };
                for (var i = 0; i < ids.Length; i++)
                {
                    var item = OutfitCatalog.Find(ids[i])!;
                    _cell.Dropped.Add(new DroppedWear
                    {
                        OwnerId = owner.Id, Slot = item.Slot, Piece = new OutfitPiece(item.Id, item.Colors[0]),
                        Position = _cell.Map.ClampToRoam(new Vec2(roam.X + roam.Width * (0.15f + i * 0.11f), roam.Bottom - roam.Height * 0.25f)),
                    });
                }
                AppLog.Write("Demo: dropped wear on the floor");
            }, TimeSpan.FromSeconds(3));
        // Dev: opens the dress-up window for the first pet and saves a picture of it to outfit-editor.png.
        if (Environment.GetEnvironmentVariable("PETFAM_DEMO") == "outfit")
            DispatcherTimer.RunOnce(() =>
            {
                if (_sim.Pets.FirstOrDefault() is not { } pet) return;
                OpenOutfitEditor(pet);
                DispatcherTimer.RunOnce(() =>
                {
                    if (_outfitEditor?.Content is not Visual v) return;
                    var s = _outfitEditor.RenderScaling;
                    using var rtb = new RenderTargetBitmap(new PixelSize((int)(_outfitEditor.Bounds.Width * s), (int)(_outfitEditor.Bounds.Height * s)), new Vector(96 * s, 96 * s));
                    rtb.Render(v);
                    var path = Path.Combine(AppLog.DataDirectory, "outfit-editor.png");
                    rtb.Save(path);
                    AppLog.Write("Outfit editor picture saved: " + path);
                }, TimeSpan.FromSeconds(2));
            }, TimeSpan.FromSeconds(4));
        if (Environment.GetEnvironmentVariable("PETFAM_DEMO") == "colours")
            DispatcherTimer.RunOnce(() =>
            {
                if (_sim.Pets.FirstOrDefault() is not { } pet) return;
                OpenCoatEditor(pet);
                DispatcherTimer.RunOnce(() =>
                {
                    if (_coatEditor?.Content is not Visual v) return;
                    var s = _coatEditor.RenderScaling;
                    using var rtb = new RenderTargetBitmap(new PixelSize((int)(_coatEditor.Bounds.Width * s), (int)(_coatEditor.Bounds.Height * s)), new Vector(96 * s, 96 * s));
                    rtb.Render(v);
                    var path = Path.Combine(AppLog.DataDirectory, "coat-editor.png");
                    rtb.Save(path);
                    AppLog.Write("Colour editor picture saved: " + path);
                }, TimeSpan.FromSeconds(2));
            }, TimeSpan.FromSeconds(4));
        if (Environment.GetEnvironmentVariable("PETFAM_DEMO") == "house")
            DispatcherTimer.RunOnce(BuildHouse, TimeSpan.FromSeconds(3));
        if (Environment.GetEnvironmentVariable("PETFAM_DEMO") == "ball")
            DispatcherTimer.RunOnce(() =>
            {
                if (_sim.Pets.FirstOrDefault(p => p.Species == PetSpecies.Dog) is not { } dog) return;
                var ok = _sim.Enqueue(dog, PetFam.Core.Interactions.InteractionCatalog.Find("ball")!, DateTime.UtcNow);
                AppLog.Write($"Demo: ball for {dog.Name} at {dog.Position} queued={ok}");
            }, TimeSpan.FromSeconds(3));

        if (Environment.GetEnvironmentVariable("PETFAM_MOTION_SHEET") is { Length: > 0 } motion)
            PoseSheet.QueueMotionSheet(Path.Combine(AppLog.DataDirectory, "motion-sheet.png"),
                motion.StartsWith("d", StringComparison.OrdinalIgnoreCase) ? PetSpecies.Dog : PetSpecies.Cat);

        if (Environment.GetEnvironmentVariable("PETFAM_POSE_SHEET") == "1")
        {
            try
            {
                var path = Path.Combine(AppLog.DataDirectory, "pose-sheet.png");
                PoseSheet.Save(path);
                AppLog.Write("Pose sheet saved: " + path);
                PoseSheet.QueueSdfSheet(Path.Combine(AppLog.DataDirectory, "pose-sheet-3d.png"));
            }
            catch (Exception ex)
            {
                AppLog.Write("Pose sheet failed: " + ex);
            }
        }

        // Diagnostics: open the pie menu (and optionally queue actions) on the first pet without a mouse.
        if (Environment.GetEnvironmentVariable("PETFAM_DEMO_MENU") is { Length: > 0 } demo)
        {
            DispatcherTimer.RunOnce(() =>
            {
                var pet = _sim.Pets[0];
                if (demo.Contains("queue"))
                {
                    foreach (var id in new[] { "ball", "pet", "treat" })
                        if (PetFam.Core.Interactions.InteractionCatalog.Find(id) is { } i) _sim.Enqueue(pet, i, DateTime.UtcNow);
                }
                OpenMenu(pet);
                pet.Say(L.S("Meow?", "야옹?"));
            }, TimeSpan.FromSeconds(4));
        }

        if (Environment.GetEnvironmentVariable("PETFAM_SNAPSHOT") == "1")
        {
            var delay = double.TryParse(Environment.GetEnvironmentVariable("PETFAM_SNAPSHOT_DELAY"), out var d) ? d : 3;
            DispatcherTimer.RunOnce(() => SaveSnapshot(), TimeSpan.FromSeconds(delay));
        }
    }

    /// <summary>
    /// Renders each monitor's backyard scene (only what PetFam draws, never the screen) to a PNG in the data folder,
    /// for checking what the layer draws. Returns the main monitor's file.
    /// </summary>
    public string? SaveSnapshot()
    {
        string? first = null;
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        foreach (var v in _views)
        {
            try
            {
                var cell = v.Cell;
                var dpi = 96 * v.Screen.Scaling;
                using var rtb = new RenderTargetBitmap(new PixelSize(cell.Bounds.Width, cell.Bounds.Height), new Vector(dpi, dpi));
                SdfPetRenderer.Suspended = true;
                try { rtb.Render(v.Backyard.RootVisual); }
                finally { SdfPetRenderer.Suspended = false; }
                var suffix = v.MonitorId == MainMonitorId ? "" : "-" + v.MonitorId;
                var path = Path.Combine(AppLog.DataDirectory, $"snapshot-{stamp}{suffix}.png");
                rtb.Save(path);
                AppLog.Write("Snapshot saved: " + path);
                first ??= path;
            }
            catch (Exception ex)
            {
                AppLog.Write("Snapshot failed: " + ex.Message);
            }
        }
        return first;
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        Save(wait: true);
        AppLog.Write("---- PetFam stopped ----");
        AppLog.Flush();
    }

    // ---------------------------------------------------------------- tray actions

    public void FeedAll()
    {
        // The bowl goes on the desktop on screen; hungry pets on other desktops come over for it.
        var now = DateTime.UtcNow;
        var here = _sim.Pets.Where(_sim.IsVisible).ToList();
        foreach (var p in here) _sim.Feed(p, now);
        if (here.Count == 0 && _cell is not null)
        {
            var roam = _cell.Map.RoamArea;
            _sim.PlaceBowl(_cell, _cell.Map.ClampToRoam(new Vec2(roam.X + roam.Width / 2f, roam.Bottom - roam.Height * 0.3f)));
        }
    }

    public void StrokeAll()
    {
        var now = DateTime.UtcNow;
        foreach (var p in _sim.Pets.Where(_sim.IsVisible)) _sim.Stroke(p, now);
    }

    public void TowelAll()
    {
        var now = DateTime.UtcNow;
        foreach (var p in _sim.Pets) _sim.Towel(p, now);
    }

    public void DebugSoak()
    {
        foreach (var p in _sim.Pets)
        {
            p.Appearance.EnterWater();
            p.SetAnimation("shake");
        }
    }

    public void DebugMud()
    {
        foreach (var p in _sim.Pets) p.Appearance.AddMud(0.7f);
    }

    /// <summary>
    /// Test option: changes the bond of every pet (or of the selected one, if any) and saves right away.
    /// Pass set for an absolute value or add for a step.
    /// </summary>
    public void DebugBond(float? set = null, float add = 0f)
    {
        var now = DateTime.UtcNow;
        var targets = _selected is not null ? new[] { _selected } : _sim.Pets.ToArray();
        foreach (var p in targets)
        {
            var value = Math.Clamp(set ?? p.Bond.Value + add, 0f, 100f);
            p.Bond.DebugSet(value, now);
            p.Say(L.S($"Bond {value:F0}", $"유대감 {value:F0}"), 1.8f);
            AppLog.Write($"Debug bond: {p.Name} -> {value:F0} ({p.Bond.Stage})");
        }
        _panel?.Refresh();
        Save();
    }

    public void ToggleHud() => _sim.Debug.ShowHud = !_sim.Debug.ShowHud;

    /// <summary>Tray: English or Korean. Windows that show text are rebuilt in the new language.</summary>
    public void SetLanguage(Language language)
    {
        if (L.Current == language) return;
        _menu?.Close();
        Deselect();
        _panel?.Close();
        _panel = null;
        L.Set(language);
        AppLog.Write($"Language: {language}");
        Save();
        PetsMenuChanged?.Invoke();
    }

    public void ToggleHabitat() => _sim.Debug.ShowHabitat = !_sim.Debug.ShowHabitat;

    // ---------------------------------------------------------------- onboarding, goodbye

    /// <summary>Welcome window: pick the first pet's breed and name it.</summary>
    public void ShowOnboarding() => ShowAdoption(firstRun: true,
        Environment.GetEnvironmentVariable("PETFAM_ONBOARDING") == "cat" ? PetSpecies.Cat : PetSpecies.Dog);

    /// <summary>
    /// The adoption window: choose dogs or cats, a breed (with its persona), a name. On first run it welcomes you;
    /// from the tray it brings another member of the family.
    /// </summary>
    public void ShowAdoption(bool firstRun, PetSpecies species)
    {
        if (_cell is null) return;
        if (_onboarding is not null) { _onboarding.Activate(); return; }
        if (!firstRun && _sim.Pets.Count >= Simulation.MaxPets)
        {
            AppLog.Write("Adopt: already at the maximum number of pets");
            _sim.Pets.FirstOrDefault(_sim.IsVisible)?.Say(L.S($"Our home fits {Simulation.MaxPets} pets", $"우리 집은 {Simulation.MaxPets}마리까지예요"), 2.5f);
            return;
        }
        _onboarding = new OnboardingWindow((breed, name) =>
        {
            if (_cell is null) return;
            var pet = _sim.AdoptPet(breed, _cell, DateTime.UtcNow);
            if (pet is null) { AppLog.Write("Adopt: already at the maximum number of pets"); return; }
            pet.Name = name;
            var roam = _cell.Map.RoamArea;
            pet.Position = _cell.Map.ClampToRoam(new Vec2(roam.X + roam.Width / 2f, roam.Bottom - roam.Height * 0.25f));
            pet.TargetHeading = 90f;
            pet.Say(L.S($"I'm {name}! {breed.Voice.Greeting[0]}", $"나는 {name}! {breed.Voice.Greeting[0]}"), 4f);
            _needsOnboarding = false;
            AppLog.Write($"Adopted {name} ({breed.Name}, {breed.Id}){(firstRun ? " at onboarding" : "")}");
            Save();
            PetsMenuChanged?.Invoke();
        }, firstRun, species);
        _onboarding.Closed += (_, _) => _onboarding = null;
        _onboarding.Show();
        _onboarding.Activate();
    }

    /// <summary>Panel: the pet leaves for good. With nobody left, the welcome window comes back.</summary>
    public void DeletePet(Pet pet)
    {
        _menu?.Close();
        if (ReferenceEquals(_selected, pet)) Deselect();
        if (_fronts.Remove(pet.Id, out var front)) front.Close();
        if (!_sim.RemovePet(pet)) return;
        AppLog.Write($"Said goodbye to {pet.Name} ({pet.Species})");
        Save();
        PetsMenuChanged?.Invoke();
        if (_sim.Pets.Count == 0)
        {
            _needsOnboarding = true;
            DispatcherTimer.RunOnce(ShowOnboarding, TimeSpan.FromSeconds(0.6));
        }
    }

    // ---------------------------------------------------------------- houses

    private HouseFrontWindow? _houseFront;
    private HouseEditorWindow? _houseEditor;

    /// <summary>Tray: design a new house; it goes onto the floor when the user presses Build.</summary>
    public void BuildHouse()
    {
        if (_cell is null) return;
        if (_cell.Houses.Count >= Cell.MaxHouses)
        {
            AppLog.Write("Build house: already at the maximum number of houses");
            _sim.Pets.FirstOrDefault()?.Say(L.S($"Up to {Cell.MaxHouses} houses", $"집은 {Cell.MaxHouses}채까지예요"), 2.5f);
            return;
        }
        // For the first pet here that has no house of its own yet; otherwise everybody's.
        var homeless = _sim.Pets.Where(p => p.CellId == _cell.Id)
            .FirstOrDefault(p => _sim.World.Cells.All(c => c.Houses.All(h => h.OwnerId != p.Id)));
        var fresh = new PetHouse
        {
            OwnerId = homeless?.Id,
            Name = homeless is not null ? HouseNameFor(homeless.Name) : _cell.Houses.Count == 0 ? L.S("Our house", "우리 집") : L.S($"Our house {_cell.Houses.Count + 1}", $"우리 집 {_cell.Houses.Count + 1}"),
            RoofColor = _cell.Houses.Count % HouseArt.RoofColors.Length,
            Position = Simulation.SpotForNewHouse(_cell),
        };
        OpenHouseEditor(fresh, isNew: true);
    }

    private void OpenHouseEditor(PetHouse house, bool isNew)
    {
        if (_cell is null) return;
        _houseEditor?.Close();
        var cell = _cell;
        _houseEditor = new HouseEditorWindow(house, isNew,
            onSave: edited =>
            {
                if (isNew)
                {
                    if (!_sim.AddHouse(cell, edited)) return;
                    AppLog.Write($"Built house '{edited.Name}' ({edited.Shape}) at {edited.Position} for {OwnerName(edited)}");
                    if (_sim.Pets.FirstOrDefault(p => p.Id == edited.OwnerId) is { } owner) owner.Say(L.S("My own house!", "내 집이다!"), 2.5f);
                    else foreach (var p in _sim.Pets.Where(p => !p.Hidden).Take(2)) p.Say(L.S("Wow, a new house!", "우와, 새 집이다!"), 2.5f);
                }
                else
                {
                    house.Name = edited.Name;
                    house.Shape = edited.Shape;
                    house.RoofColor = edited.RoofColor;
                    house.WallColor = edited.WallColor;
                    house.Decorations = edited.Decorations;
                    if (house.OwnerId != edited.OwnerId && _sim.Pets.FirstOrDefault(p => p.Id == edited.OwnerId) is { } owner) owner.Say(L.S("My own house!", "내 집이다!"), 2.5f);
                    house.OwnerId = edited.OwnerId;
                    AppLog.Write($"Decorated house '{house.Name}' for {OwnerName(house)}: {house.Shape}, roof {house.RoofColor}, wall {house.WallColor}, {string.Join(",", house.Decorations)}");
                }
                Save();
            },
            onDemolish: isNew ? null : () =>
            {
                _sim.RemoveHouse(cell, house);
                AppLog.Write($"Demolished house '{house.Name}'");
                Save();
            },
            pets: _sim.Pets.Select(p => (p.Id, p.Name, p.Species)).ToList());
        _houseEditor.Closed += (_, _) => _houseEditor = null;
        _houseEditor.Show();
        _houseEditor.Activate();
    }

    /// <summary>While the cursor is on a built house, an invisible window over it takes clicks and drags.</summary>
    private void UpdateHouseWindow()
    {
        if (_cell is null) return;
        var holding = _houseFront?.IsHolding ?? false;
        PetHouse? target = holding ? _houseFront!.House : null;
        if (target is null && _sim.User.CursorOnDesktop)
        {
            // Nearest (front-most) house first: the one lowest on screen.
            target = _cell.Houses.OrderByDescending(h => h.Position.Y)
                .FirstOrDefault(h => HouseFrontWindow.BodyOnScreen(_cell, h).Contains(_sim.User.Cursor));
            // A pet in front of the house gets the click instead.
            if (target is not null && _sim.Pets.Any(p => _fronts.TryGetValue(p.Id, out var f) && f.IsVisible && p.Position.Y > target.Position.Y)) target = null;
        }

        if (target is not null && _cell.Houses.Contains(target))
        {
            _houseFront ??= new HouseFrontWindow(_cell, _platform, _layering,
                onClick: h => OpenHouseEditor(h, isNew: false),
                onMoved: h => { AppLog.Write($"Moved house '{h.Name}' to {h.Position}"); Save(); });
            _houseFront.Follow(target);
            if (!_houseFront.IsVisible) _houseFront.Show();
        }
        else if (_houseFront?.IsVisible == true)
        {
            _houseFront.Hide();
        }
    }

    /// <summary>"Momo's house" / "모모네".</summary>
    public static string HouseNameFor(string petName) => L.S($"{petName}'s house", $"{petName}네");

    private string OwnerName(PetHouse h) => _sim.Pets.FirstOrDefault(p => p.Id == h.OwnerId)?.Name ?? "everybody";

    private readonly Dictionary<Guid, string> _lastBehavior = new();

    /// <summary>Logs when a pet starts or ends sleeping or sheltering (where it went), for "why didn't it go in?".</summary>
    private void LogBehaviors()
    {
        foreach (var pet in _sim.Pets)
        {
            var name = pet.Engine.Current?.Name ?? "";
            if (_lastBehavior.TryGetValue(pet.Id, out var was) && was != name &&
                (name.StartsWith("shelter") || name.StartsWith("sleep") || was.StartsWith("shelter")))
            {
                var house = pet.InsideHouse is { } id ? _sim.World.Cells.SelectMany(c => c.Houses).FirstOrDefault(h => h.Id == id)?.Name : null;
                AppLog.Write($"Behavior: {pet.Name} {was} -> {name}{(house is null ? "" : $" (in '{house}')")} weather={_sim.Weather.Kind} at {pet.Position}");
            }
            _lastBehavior[pet.Id] = name;
        }
        // Where everybody is and what they're doing, once a minute: for "it's stuck" reports.
        if (DateTime.UtcNow - _lastPetDump < TimeSpan.FromMinutes(1)) return;
        _lastPetDump = DateTime.UtcNow;
        AppLog.Write("Pets: " + string.Join(" | ", _sim.Pets.Select(p =>
            $"{p.Name} {p.Engine.Current?.Name} {p.Animation} at {p.Position} in {p.CellId.Split(':').Last()}{(p.Hidden ? " hidden" : "")}{(p.Trip is null ? "" : " trip")}")));
    }

    private DateTime _lastPetDump;

    /// <summary>Tray: adopt another cat or dog (up to six pets) so same-species play can happen.</summary>
    public void AdoptPet(PetSpecies species) => ShowAdoption(firstRun: false, species);

    public enum AgeStep { Day, Stage, Baby }

    /// <summary>Test: age the selected pet (or every pet) by a day, to the next stage, or back to a baby.</summary>
    public void DebugAge(AgeStep step)
    {
        foreach (var p in _selected is { } sel ? new[] { sel } : _sim.Pets.ToArray())
        {
            var g = p.Growth;
            var e = step switch
            {
                AgeStep.Day => g.AddDays(1),
                AgeStep.Stage => g.AddDays(g.Stage switch
                {
                    LifeStage.Baby => Growth.JuniorAt - g.Days,
                    LifeStage.Junior => Growth.AdultAt - g.Days,
                    LifeStage.Adult => Growth.SeniorAt - g.Days,
                    _ => 60,
                }),
                _ => g.AddDays(-g.Days),
            };
            if (e is Growth.Event.GrewUp or Growth.Event.Anniversary) Simulation.Celebrate(p, e);
            AppLog.Write($"Debug age: {p.Name} day {g.Days} ({g.Stage}, {g.AgeLabel(p.Species)})");
        }
        _panel?.Refresh();
        Save();
    }

    /// <summary>Test: make the first two free pets start playing right now.</summary>
    public void DebugPlay()
    {
        var free = _sim.Pets.Where(PetFam.Core.Social.SocialDirector.Available).ToList();
        for (var i = 0; i < free.Count; i++)
        for (var j = 0; j < free.Count; j++)
        {
            if (i == j) continue;
            var a = free[i].Species == PetSpecies.Dog ? free[i] : free[j];
            var b = ReferenceEquals(a, free[i]) ? free[j] : free[i];
            if (_sim.StartPlay(a, b)) { AppLog.Write($"Debug play: {a.Name} -> {b.Name} ({a.Session?.Name})"); return; }
        }
        AppLog.Write("Debug play: no two free pets");
    }

    // ---------------------------------------------------------------- habitat recognition (plan §6.2 ③)

    private const float WallpaperPollSeconds = 5f;
    private readonly Dictionary<string, string> _analyzedKeys = new();
    private bool _analyzing;
    private float _sinceWallpaperPoll;

    /// <summary>
    /// Analyses the wallpaper of the desktop on screen on a worker thread and swaps that cell's map when done.
    /// Re-runs only when the wallpaper file (path, size, time) changed, unless forced. When that is up to date,
    /// desktops not yet seen get a first map from the wallpaper Windows remembers for them.
    /// </summary>
    public void AnalyzeHabitat(bool force = false)
    {
        if (_cell is null || _analyzing) return;
        if (force) foreach (var v in _views) _analyzedKeys.Remove(v.Cell.Id);
        foreach (var v in _views)
            if (AnalyzeCell(v.Cell, WallpaperPath(), force: false)) return;
        if (WallpaperOverride is not null) return;
        foreach (var other in _sim.World.Cells)
        {
            if (_views.Any(v => ReferenceEquals(v.Cell, other)) || _analyzedKeys.ContainsKey(other.Id)) continue;
            var path = _desktopWallpapers.GetValueOrDefault(other.DesktopId);
            if (path is null) { _analyzedKeys[other.Id] = "none"; continue; }
            if (AnalyzeCell(other, path, force: false)) return;
        }
    }

    /// <summary>Starts the analysis of one cell's wallpaper. False when it was already up to date.</summary>
    private bool AnalyzeCell(Cell cell, string? path, bool force)
    {
        var fit = WallpaperOverride is null ? _platform.GetWallpaperFit() : WallpaperFit.Fill;
        string key;
        try
        {
            key = path is null ? "none" : $"{path}|{new FileInfo(path).Length}|{File.GetLastWriteTimeUtc(path).Ticks}|{fit}";
        }
        catch
        {
            key = path ?? "none";
        }
        if (!force && _analyzedKeys.GetValueOrDefault(cell.Id) == key) return false;
        _analyzedKeys[cell.Id] = key;

        if (path is null)
        {
            ApplyHabitat(cell, null, "no wallpaper file (solid colour or slideshow API): flat floor");
            return true;
        }

        // PetFam's own wallpapers come with an exact habitat: no guessing needed.
        if (OfficialHabitat.FromPath(path) is { } official && OfficialRows(official.Id) is { } rows)
        {
            ApplyHabitat(cell, OfficialHabitat.MaskFor(rows, official.Width, official.Height, cell.Bounds, fit), $"official wallpaper '{official.Id}' habitat");
            return true;
        }

        _analyzing = true;
        Task.Run(() =>
        {
            HabitatAnalyzer.Result? result = null;
            string? error = null;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try { result = WallpaperAnalyzer.Analyze(path, fit, cell.Bounds, cell.WorkingArea); }
            catch (Exception ex) { error = ex.Message; }
            var ms = sw.ElapsedMilliseconds;
            Dispatcher.UIThread.Post(() =>
            {
                _analyzing = false;
                ApplyHabitat(cell, result?.Mask, error ?? $"{result?.Report ?? "could not read wallpaper"} ({ms} ms)");
            });
        });
        return true;
    }

    /// <summary>Test option: PETFAM_WALLPAPER=&lt;image&gt; uses another picture (painted in the backyard) without touching the real wallpaper.</summary>
    private static readonly string? WallpaperOverride =
        Environment.GetEnvironmentVariable("PETFAM_WALLPAPER") is { Length: > 0 } p && File.Exists(p) ? p : null;

    private string? WallpaperPath() => WallpaperOverride ?? _platform.GetWallpaperPath();

    // ---------------------------------------------------------------- official wallpapers

    private static string OfficialDir => Path.Combine(AppLog.DataDirectory, "Wallpapers");
    private static string PreviousWallpaperFile => Path.Combine(AppLog.DataDirectory, "previous-wallpaper.txt");
    private readonly Dictionary<string, string[]?> _officialRows = new();

    private string[]? OfficialRows(string id)
    {
        if (_officialRows.TryGetValue(id, out var rows)) return rows;
        try
        {
            using var s = AssetLoader.Open(new Uri($"avares://PetFam/Assets/Wallpapers/{id}.habitat.txt"));
            using var reader = new StreamReader(s);
            rows = reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
        catch (Exception ex)
        {
            AppLog.Write($"Official habitat '{id}' not found: {ex.Message}");
            rows = null;
        }
        return _officialRows[id] = rows;
    }

    /// <summary>Is a PetFam wallpaper on the desktop now (and the user's own one remembered)?</summary>
    public bool CanRestoreWallpaper => File.Exists(PreviousWallpaperFile);

    /// <summary>Tray: puts one of PetFam's own wallpapers on the desktop (remembering the user's own to go back to).</summary>
    public void UseOfficialWallpaper(string id)
    {
        if (OfficialHabitat.All.FirstOrDefault(w => w.Id == id) is not { } wall) return;
        try
        {
            Directory.CreateDirectory(OfficialDir);
            var dest = Path.Combine(OfficialDir, OfficialHabitat.FileName(wall));
            using (var src = AssetLoader.Open(new Uri($"avares://PetFam/Assets/Wallpapers/{id}.jpg")))
            using (var file = File.Create(dest))
                src.CopyTo(file);

            var current = _platform.GetWallpaperPath();
            if (OfficialHabitat.FromPath(current) is null && !File.Exists(PreviousWallpaperFile))
                File.WriteAllLines(PreviousWallpaperFile, new[] { current ?? "", _platform.GetWallpaperFit().ToString() });

            if (_platform.SetWallpaper(dest, WallpaperFit.Fill))
            {
                _sinceWallpaperPoll = WallpaperPollSeconds;   // read the new habitat on the next frame
                AppLog.Write($"Official wallpaper: {wall.Id}");
            }
        }
        catch (Exception ex) { AppLog.Write("Official wallpaper failed: " + ex.Message); }
        MenuChanged?.Invoke();
    }

    /// <summary>Tray: back to the wallpaper the user had before picking one of PetFam's.</summary>
    public void RestoreWallpaper()
    {
        try
        {
            if (!File.Exists(PreviousWallpaperFile)) return;
            var lines = File.ReadAllLines(PreviousWallpaperFile);
            var path = lines.ElementAtOrDefault(0) ?? "";
            var fit = Enum.TryParse<WallpaperFit>(lines.ElementAtOrDefault(1), out var f) ? f : WallpaperFit.Fill;
            if (path.Length > 0 && File.Exists(path) && _platform.SetWallpaper(path, fit))
            {
                File.Delete(PreviousWallpaperFile);
                _sinceWallpaperPoll = WallpaperPollSeconds;
                AppLog.Write("Wallpaper restored: " + path);
            }
            else AppLog.Write("Wallpaper not restored: the old picture is gone (" + path + ")");
        }
        catch (Exception ex) { AppLog.Write("Wallpaper restore failed: " + ex.Message); }
        MenuChanged?.Invoke();
    }

    // ---------------------------------------------------------------- habitat editor

    private static string EditsPath => Path.Combine(AppLog.DataDirectory, "habitat-edits.json");
    private HabitatEdits? _edits;
    private HabitatEdits Edits => _edits ??= HabitatEdits.Read(EditsPath);

    /// <summary>What the analysis found for each cell, before the user's fixes (null: no analysis, flat floor).</summary>
    private readonly Dictionary<string, WalkMask?> _autoMasks = new();

    private string EditKey(Cell cell) => HabitatEdits.KeyFor(_analyzedKeys.GetValueOrDefault(cell.Id) ?? "none", cell.Bounds);

    /// <summary>Monitors for the tray's habitat editor: (index, "main monitor" / "monitor 2").</summary>
    public IReadOnlyList<(int Index, string Name)> Monitors =>
        _views.Select((v, i) => (i, _sim.World.DescribeMonitor(v.Cell) is { Length: > 0 } n ? n : L.S("this screen", "이 화면"))).ToList();

    private HabitatEditorWindow? _habitatEditor;

    /// <summary>Tray: opens the habitat editor over one monitor (its cell of the current desktop).</summary>
    public void OpenHabitatEditor(int monitor = 0)
    {
        if (_views.Count == 0) return;
        _habitatEditor?.Close();
        var view = _views[Math.Clamp(monitor, 0, _views.Count - 1)];
        var cell = view.Cell;
        var auto = _autoMasks.GetValueOrDefault(cell.Id) ?? HabitatEdits.FlatMask(HabitatMap.Flat(cell.Bounds, cell.WorkingArea));
        var key = EditKey(cell);
        Bitmap? picture = null;
        try
        {
            if (WallpaperPath() is { } path)
            {
                using var full = new Bitmap(path);
                var k = Math.Min(1.0, Math.Max(cell.Bounds.Width / (double)full.PixelSize.Width, cell.Bounds.Height / (double)full.PixelSize.Height));
                picture = full.CreateScaledBitmap(new PixelSize(Math.Max(1, (int)(full.PixelSize.Width * k)), Math.Max(1, (int)(full.PixelSize.Height * k))));
            }
        }
        catch (Exception ex) { AppLog.Write("Habitat editor: wallpaper not shown: " + ex.Message); }

        var where = _sim.World.DescribeCell(cell) is { Length: > 0 } d ? d : L.S("This screen", "이 화면");
        var fit = WallpaperOverride is null ? _platform.GetWallpaperFit() : WallpaperFit.Fill;
        _habitatEditor = new HabitatEditorWindow(view.Screen, cell.Bounds, auto, Edits.For(key), picture, fit, where, strokes =>
        {
            Edits.Set(key, strokes);
            try { Edits.Write(EditsPath); }
            catch (Exception ex) { AppLog.Write("Habitat edits not saved: " + ex.Message); }
            ApplyHabitat(cell, _autoMasks.GetValueOrDefault(cell.Id), $"hand fixes saved ({strokes.Count} dabs)");
        });
        _habitatEditor.Closed += (_, _) => { _habitatEditor = null; picture?.Dispose(); };
        _habitatEditor.Show();
        _habitatEditor.Activate();
        AppLog.Write($"Habitat editor opened on {where} ({Edits.For(key).Count} dabs so far)");
    }

    private void ApplyHabitat(Cell cell, WalkMask? mask, string report)
    {
        // The user's fixes from the habitat editor go on top of what the analysis found.
        _autoMasks[cell.Id] = mask;
        var strokes = Edits.For(EditKey(cell));
        if (strokes.Count > 0)
        {
            mask = HabitatEdits.Apply(mask ?? HabitatEdits.FlatMask(HabitatMap.Flat(cell.Bounds, cell.WorkingArea)), strokes);
            report += $"; with {strokes.Count} hand fixes";
        }
        cell.Map = mask is null ? HabitatMap.Flat(cell.Bounds, cell.WorkingArea) : HabitatMap.FromMask(cell.Bounds, cell.WorkingArea, mask);
        foreach (var pet in _sim.Pets.Where(p => p.CellId == cell.Id))
        {
            if (pet.Hold == HoldState.Carried || pet.Trip is not null) continue;
            pet.Position = cell.Map.ClampToRoam(pet.Position);
            pet.Engine.ForceStop(new PetFam.Core.Behavior.BehaviorContext
            {
                Pet = pet, Cell = cell, User = _sim.User, Rng = new Random(), Debug = _sim.Debug, NowUtc = DateTime.UtcNow,
            });
        }
        foreach (var bowl in cell.Bowls) bowl.Position = cell.Map.ClampToRoam(bowl.Position);
        foreach (var house in cell.Houses) house.Position = cell.Map.ClampToRoam(house.Position);
        AppLog.Write($"Habitat ({_sim.World.DescribeCell(cell)}): {report}; roam={cell.Map.RoamArea} mask={(mask is null ? "none" : mask.Count + " cells")}");
    }

    // ---------------------------------------------------------------- frame

    // ---------------------------------------------------------------- frame timing (to find hitches)

    private static readonly string[] Sections = { "desktops", "sim", "polls", "fronts", "backyard", "overlay", "save" };
    private readonly double[] _sectionMs = new double[Sections.Length];
    private readonly double[] _sectionMax = new double[Sections.Length];
    private double _workTotal, _workMax, _gapMax;
    private int _frames, _hitches, _movingFrames, _evenFrames;
    private double _sinceStats, _lastHitchLog = -10;
    private int _gc0, _gc1, _gc2;

    private void OnFrame(object? sender, EventArgs e)
    {
        var now = _clock.Elapsed.TotalSeconds;
        var dt = (float)(now - _lastTime);
        _lastTime = now;
        var t0 = Stopwatch.GetTimestamp();
        var mark = t0;
        var frameMs = new double[Sections.Length];
        void Lap(int i)
        {
            var t = Stopwatch.GetTimestamp();
            frameMs[i] += (t - mark) * 1000.0 / Stopwatch.Frequency;
            mark = t;
        }

        _sinceDesktopPoll += dt;
        if (_sinceDesktopPoll >= DesktopPollSeconds)
        {
            _sinceDesktopPoll = 0f;
            UpdateDesktops();
        }
        _sinceMonitorPoll += dt;
        if (_sinceMonitorPoll >= 2f)
        {
            _sinceMonitorPoll = 0f;
            UpdateMonitors();
        }
        Lap(0);

        _sim.Tick(dt, DateTime.UtcNow);
        LogBehaviors();
        Lap(1);

        _sinceWallpaperPoll += dt;
        if (_sinceWallpaperPoll >= WallpaperPollSeconds)
        {
            _sinceWallpaperPoll = 0f;
            AnalyzeHabitat();
            if (_pretend is null) _weather.Poll(a => Dispatcher.UIThread.Post(a));
        }
        Lap(2);

        UpdateFrontWindows();
        UpdateBallWindow();
        UpdateHouseWindow();
        UpdateSelection(dt);
        Lap(3);
        foreach (var v in _views) v.Backyard.RenderFrame();
        Lap(4);
        UpdateOverlayVisibility();
        UpdateFrameRate();
        Lap(5);

        _sinceSave += dt;
        if (_sinceSave >= AutosaveSeconds)
        {
            _sinceSave = 0f;
            Save();
        }
        Lap(6);

        RecordFrame(now, dt, frameMs);
    }

    /// <summary>
    /// Logs a frame that came much later than the timer asked for (a visible hitch), with what this frame's own work
    /// cost and whether a garbage collection ran; and a periodic summary of frame work and pacing.
    /// </summary>
    private void RecordFrame(double now, float dt, double[] frameMs)
    {
        var work = frameMs.Sum();
        _frames++;
        _workTotal += work;
        _workMax = Math.Max(_workMax, work);
        for (var i = 0; i < frameMs.Length; i++) { _sectionMs[i] += frameMs[i]; _sectionMax[i] = Math.Max(_sectionMax[i], frameMs[i]); }
        var gapMs = dt * 1000.0;
        _gapMax = Math.Max(_gapMax, gapMs);
        if (_fast)
        {
            _movingFrames++;
            if (Math.Abs(gapMs - ActiveFrameSeconds * 1000) <= 5) _evenFrames++;
        }

        int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
        var expected = _timer?.Interval.TotalMilliseconds ?? 33;
        if (now > 5 && gapMs > expected * 1.8 + 25)
        {
            _hitches++;
            if (now - _lastHitchLog > 2)
            {
                _lastHitchLog = now;
                var parts = string.Join(" ", Sections.Select((n, i) => $"{n}={frameMs[i]:F1}"));
                AppLog.Write($"Hitch: {gapMs:F0} ms between frames (timer {expected:F0} ms); this frame's work {work:F1} ms [{parts}]; GC since last frame gen0+{g0 - _gc0} gen1+{g1 - _gc1} gen2+{g2 - _gc2}");
            }
        }
        _gc0 = g0; _gc1 = g1; _gc2 = g2;

        // Summary: every 30 s if there was a hitch, otherwise every 10 minutes (keeps the log small).
        _sinceStats += dt;
        if (_sinceStats >= 600 || (_sinceStats >= 30 && _hitches > 0))
        {
            var parts = string.Join(" ", Sections.Select((n, i) => $"{n}={_sectionMs[i] / _frames:F2}/{_sectionMax[i]:F0}"));
            AppLog.Write($"Frames: {_frames} in {_sinceStats:F0} s, work avg {_workTotal / _frames:F2} ms max {_workMax:F1} ms, longest gap {_gapMax:F0} ms, hitches {_hitches}, moving frames on the 33 ms beat {(_movingFrames == 0 ? 100 : 100.0 * _evenFrames / _movingFrames):F0}% of {_movingFrames} [avg/max ms: {parts}]");
            _sinceStats = 0; _frames = 0; _workTotal = 0; _workMax = 0; _gapMax = 0; _hitches = 0; _movingFrames = 0; _evenFrames = 0;
            Array.Clear(_sectionMs);
            Array.Clear(_sectionMax);
        }
    }

    /// <summary>The overlays are always up (see AddView); each is redrawn while it has something to show, and once more after.</summary>
    private void UpdateOverlayVisibility()
    {
        foreach (var v in _views)
        {
            if (!v.Overlay.IsVisible)
            {
                v.Overlay.Show();
                v.Overlay.ApplyClickThrough();
            }
            var has = v.Overlay.HasContent;
            if (has || v.OverlayHadContent) v.Overlay.RenderFrame();
            v.OverlayHadContent = has;
        }
    }

    /// <summary>
    /// Front yard: when the cursor is over a visible pet (bare desktop under the cursor), or while the user holds
    /// one, that pet moves to a small topmost window that takes mouse input. Otherwise it lives in the backyard.
    /// </summary>
    private void UpdateFrontWindows()
    {
        if (_cell is null) return;
        var cursor = _sim.User.Cursor;

        foreach (var pet in _sim.Pets)
        {
            pet.Hovered = false;
            if (!_sim.VisibleCellIds.Contains(pet.CellId) || pet.Layer == PetLayer.WindowEdge) continue;
            if (_sim.World.Find(pet.CellId) is not { } petCell) continue;

            _fronts.TryGetValue(pet.Id, out var front);
            if (pet.Hidden)
            {
                // Inside the house: not touchable.
                if (pet.Layer == PetLayer.Front) pet.Layer = PetLayer.Backyard;
                if (front?.IsVisible == true) front.Hide();
                _handover.Remove(pet.Id);
                continue;
            }
            var held = pet.Hold != HoldState.None || (front?.IsHolding ?? false);
            var depth = petCell.Map.DepthScale(pet.Position.Y);
            // A wider box to leave than to come in, so a cursor resting near the edge of a pet that sways or turns its
            // head doesn't flip its mouse window on and off.
            var inFront = front?.IsVisible ?? false;
            var box = Expand(PetRenderer.HitBox(pet, depth), inFront ? 40 : 20);
            var hovering = _sim.User.CursorOnDesktop && box.Contains(cursor);
            var want = held || hovering;
            if (_sim.Debug.TraceHover && want != (_lastWant.TryGetValue(pet.Id, out var lw) && lw))
                AppLog.Write($"Hover: {pet.Name} {(want ? "in" : "out")} held={held} onDesktop={_sim.User.CursorOnDesktop} inBox={box.Contains(cursor)} cursor={cursor} box={box} layer={pet.Layer} frontVisible={front?.IsVisible}");
            _lastWant[pet.Id] = want;
            pet.Hovered = hovering;

            // Hovering only puts an invisible window over the pet to take the mouse: the pet stays drawn in the backyard,
            // so nothing changes on screen (moving the drawing between windows made it blink). Only a carried pet is
            // drawn in the front window, above other windows. Handing it from one window to the other, both draw it
            // for a few frames, so it is never missing while the other window catches up.
            var carried = pet.Hold == HoldState.Carried;
            if (want)
            {
                if (front is null)
                {
                    front = new FrontWindow(pet, _sim, petCell, _platform, _layering, OpenMenu);
                    _fronts[pet.Id] = front;
                }
                if (carried && pet.Layer != PetLayer.Front && !_handover.ContainsKey(pet.Id)) _handover[pet.Id] = HandoverFrames;
                else if (!carried && pet.Layer == PetLayer.Front) { pet.Layer = PetLayer.Backyard; _handover[pet.Id] = HandoverFrames; }
                var handing = _handover.TryGetValue(pet.Id, out var n);
                if (handing)
                {
                    if (n > 0) _handover[pet.Id] = n - 1;
                    else
                    {
                        _handover.Remove(pet.Id);
                        handing = false;
                        if (carried) pet.Layer = PetLayer.Front;
                    }
                }
                front.DrawPet = pet.Layer == PetLayer.Front || handing;
                front.Follow();
                if (!front.IsVisible) front.Show();
            }
            else if (front?.IsVisible == true)
            {
                if (pet.Layer == PetLayer.Front) { pet.Layer = PetLayer.Backyard; _handover[pet.Id] = HandoverFrames; }
                if (_handover.TryGetValue(pet.Id, out var n) && n > 0)
                {
                    _handover[pet.Id] = n - 1;
                    front.Follow();
                }
                else
                {
                    _handover.Remove(pet.Id);
                    front.DrawPet = false;
                    front.Hide();
                }
            }
            else if (pet.Layer == PetLayer.Front)
            {
                pet.Layer = PetLayer.Backyard;
            }
        }
    }

    /// <summary>Frames a pet is drawn in both windows while it moves between the backyard and its front window.</summary>
    private const int HandoverFrames = 3;

    private readonly Dictionary<Guid, int> _handover = new();
    private readonly Dictionary<Guid, bool> _lastWant = new();

    private BallFrontWindow? _ballFront;

    /// <summary>
    /// The fetch ball moves to its own topmost window while the cursor is over it (or it is in hand),
    /// so it can be picked up and thrown. Otherwise it is drawn in the backyard.
    /// </summary>
    private void UpdateBallWindow()
    {
        if (_cell is null) return;
        var ball = _cell.Ball;
        var holding = _ballFront?.IsHolding ?? false;
        var show = false;
        if (ball is not null && ball.CarriedBy is null)
        {
            var depth = _cell.Map.DepthScale(ball.Position.Y);
            var center = new Vec2(ball.Position.X, ball.Position.Y - (BallSprite.BallR + ball.Height * 0.91f) * depth);
            var hovering = _sim.User.CursorOnDesktop && Vec2.Distance(_sim.User.Cursor, center) <= 26f * depth;
            show = hovering || holding || (ball.InFront && Vec2.Distance(_sim.User.Cursor, center) <= 34f * depth);
        }

        if (show)
        {
            _ballFront ??= new BallFrontWindow(_sim, _cell, _platform, _layering);
            ball!.InFront = true;
            _ballFront.Follow(ball);
            if (!_ballFront.IsVisible) _ballFront.Show();
        }
        else
        {
            if (ball is not null) ball.InFront = false;
            if (_ballFront?.IsVisible == true) _ballFront.Hide();
        }
    }

    private static RectI Expand(RectI r, int by) => new(r.X - by, r.Y - by, r.Width + by * 2, r.Height + by * 2);

    /// <summary>30 fps while something moves, 12 fps while every pet is idle/sitting/sleeping.</summary>
    // ---------------------------------------------------------------- Sims-style selection, pie menu, panel

    private const float SelectionTimeout = 30f;
    private PieMenuWindow? _menu;
    private PetPanelWindow? _panel;
    private Pet? _selected;
    private float _sinceSelectionActivity;
    private float _sincePanelRefresh;

    /// <summary>Click on a pet: select it, stop it to look at you, and show the pie menu around its head.</summary>
    public void OpenMenu(Pet pet)
    {
        if (_cell is null) return;
        // Swapping menus keeps the panel up; only the open menu closing takes the panel with it.
        var old = _menu;
        _menu = null;
        old?.Close();
        Select(pet);
        _sim.BeginAttention(pet);

        var petCell = _sim.World.Find(pet.CellId) ?? _cell;
        var depth = petCell.Map.DepthScale(pet.Position.Y);
        var box = PetRenderer.HitBox(pet, depth);
        var center = new PixelPoint(box.X + box.Width / 2, box.Y + (int)(box.Height * 0.3));
        var scaling = ScalingAt(center);

        var menu = new PieMenuWindow(pet, _sim.MenuFor(pet, DateTime.UtcNow), center, scaling, interaction =>
        {
            if (_sim.Enqueue(pet, interaction, DateTime.UtcNow))
                AppLog.Write($"Queued {interaction.Id} for {pet.Name}");
            _sinceSelectionActivity = 0f;
        });
        menu.Closed += (_, _) =>
        {
            _sim.EndAttention(pet);
            if (!ReferenceEquals(_menu, menu)) return;
            _menu = null;
            // The details panel opens with the pie menu and goes away with it.
            Deselect();
        };
        _menu = menu;
        menu.Show();
    }

    private void Select(Pet pet)
    {
        foreach (var p in _sim.Pets) p.Selected = ReferenceEquals(p, pet);
        _selected = pet;
        _sinceSelectionActivity = 0f;

        if (_panel is null)
        {
            var screen = _backyard?.Screens.Primary;
            var work = screen?.WorkingArea ?? new PixelRect(0, 0, 1920, 1040);
            _panel = new PetPanelWindow(_platform, _layering, work,
                onCancel: (p, action) => { _sim.Cancel(p, action); _sinceSelectionActivity = 0f; },
                onClose: Deselect)
            {
                SocialLine = SocialSummary,
                OnDelete = DeletePet,
                OnColours = OpenCoatEditor,
                OnOutfit = OpenOutfitEditor,
                OnPutBack = p => { var n = _sim.PutBack(p); AppLog.Write($"Put back on {p.Name}: {n} piece(s)"); },
            };
        }
        _panel.ShowFor(pet);
    }

    private OutfitWindow? _outfitEditor;

    /// <summary>Tray / panel: dress the pet up. On Save it reacts in character (delighted, fine, or grumbling).</summary>
    public void OpenOutfitEditor(Pet pet)
    {
        _outfitEditor?.Close();
        _outfitEditor = new OutfitWindow(pet, _sim.Pets.Where(p => p != pet).ToList(), (outfit, partners) =>
        {
            var hadSomething = pet.Outfit.Pieces.Count > 0;
            pet.Doffed.Clear();
            foreach (var cell in _sim.World.Cells) cell.Dropped.RemoveAll(w => w.OwnerId == pet.Id && w.CarriedBy is null);
            pet.Outfit = outfit;
            _sim.ReactToOutfit(pet, undressed: hadSomething && outfit.Pieces.Count == 0);
            // Couple look: the same pieces on the chosen pets too (those they have unlocked).
            foreach (var partner in partners)
            {
                foreach (var (slot, piece) in outfit.Pieces)
                    if (piece.Item is { } item && OutfitCatalog.IsUnlocked(partner, item)) partner.Outfit.Set(slot, piece);
                partner.Doffed.Clear();
                _sim.ReactToOutfit(partner, undressed: false);
                AppLog.Write($"Couple look: {partner.Name} now matches {pet.Name} ({OutfitCatalog.Matching(pet, partner)} pieces)");
            }
            AppLog.Write($"Outfit of {pet.Name}: {(outfit.Pieces.Count == 0 ? "nothing" : string.Join(", ", outfit.Pieces.Select(kv => $"{kv.Key}={kv.Value.ItemId}#{kv.Value.Color:X6}")))}{(outfit.DressForWeather ? " (dresses for the weather)" : "")}");
            Save();
        });
        _outfitEditor.Closed += (_, _) => _outfitEditor = null;
        _outfitEditor.Show();
        _outfitEditor.Activate();
    }

    private CoatColorWindow? _coatEditor;

    /// <summary>Tray / panel: the coat colour editor for this pet.</summary>
    public void OpenCoatEditor(Pet pet)
    {
        _coatEditor?.Close();
        _coatEditor = new CoatColorWindow(pet, coat =>
        {
            pet.Coat = coat;
            AppLog.Write($"Colours of {pet.Name}: {(coat is null ? "breed colours" : $"coat={coat.Fur:X6} light={coat.Light:X6} marking={coat.Marking:X6} eyes={coat.Eyes:X6}")}");
            Save();
        });
        _coatEditor.Closed += (_, _) => _coatEditor = null;
        _coatEditor.Show();
        _coatEditor.Activate();
    }

    /// <summary>"Rank 1/3 · Best friend: Peanut (72)" for the panel.</summary>
    private string SocialSummary(Pet pet)
    {
        if (_sim.Pets.Count < 2) return "";
        var rank = _sim.Social.Rank(pet, _sim.Pets);
        var friend = _sim.Social.BestFriend(pet, _sim.Pets);
        var text = L.S($"Rank {rank}/{_sim.Pets.Count}", $"서열 {rank}/{_sim.Pets.Count}");
        if (friend is not null) text += L.S($" · Best friend: {friend.Name} ({_sim.Social.Friendship(pet, friend):F0})", $" · 단짝: {friend.Name} (친밀 {_sim.Social.Friendship(pet, friend):F0})");
        if (pet.Session is { } s)
        {
            var mate = ReferenceEquals(s.Initiator, pet) ? s.Partner.Name : s.Initiator.Name;
            text += L.S($"\nNow: playing with {mate}", $"\n지금: {mate}와(과) 노는 중");
        }
        return text;
    }

    private void Deselect()
    {
        foreach (var p in _sim.Pets) p.Selected = false;
        _selected = null;
        _panel?.Hide();
    }

    private void UpdateSelection(float dt)
    {
        if (_selected is null) return;

        var busy = _menu is not null || _selected.Queue.Count > 0 || _selected.Hold != HoldState.None;
        _sinceSelectionActivity = busy ? 0f : _sinceSelectionActivity + dt;
        if (_sinceSelectionActivity > SelectionTimeout)
        {
            Deselect();
            return;
        }

        _sincePanelRefresh += dt;
        if (_sincePanelRefresh >= 0.2f)
        {
            _sincePanelRefresh = 0f;
            _panel?.Refresh();
        }
    }

    /// <summary>
    /// 30 fps while something moves, 12 fps while every pet is idle/sitting/sleeping (see <see cref="FrameClock"/>
    /// for why frames come from their own clock). Turning, a fresh pose blending in and anything mid-air count as
    /// moving too, so those never play at 12 fps.
    /// </summary>
    private void UpdateFrameRate()
    {
        if (_timer is null) return;
        var active = _sim.Debug.ShowHud || (_cell?.Ball is not null) || _sim.Weather.Raining || _sim.Weather.Snowing || _sim.SinceLightning < 0.6f || _sim.Director.Active.Count > 0 || _sim.Pets.Any(p =>
            p.Selected || p.Bubble is not null || p.Queue.Count > 0 || p.Lift > 0f || p.Animation is "stalk" or "wiggle" or "bat" or "knead" or "scratch" or "playbow" or "hiss" || p.Butterfly is not null || p.ZBoost != 0f ||
            p.Layer != PetLayer.Backyard || p.Trip is not null || p.Doorway is not null || p.Celebration != Growth.Event.None ||
            Math.Abs(p.Heading - p.TargetHeading) > 0.5f || p.AnimationTime < 1.2f ||
            p.Animation is "walk" or "run" or "shake" or "stretch" or "eat" or "dig" or "groom" or "approach-food");
        _fast = active;
        var wanted = TimeSpan.FromSeconds(active ? ActiveFrameSeconds : _calmFrameSeconds);
        if (_timer.Interval != wanted) _timer.Interval = wanted;
    }

    private bool _fast;

    // ---------------------------------------------------------------- weather (real weather where the user is)

    private readonly WeatherService _weather = new();

    /// <summary>A made-up weather (tray test menu, or PETFAM_WEATHER=rain|heavyrain|thunder|snow|fog|cloudy|clear).</summary>
    private WeatherNow? _pretend;

    private void StartWeather()
    {
        // Sunrise and sunset are worked out for the weather place (on this computer, no network needed).
        _sim.Clock.SetPlace(_weather.Location.Latitude, _weather.Location.Longitude);
        if (Environment.GetEnvironmentVariable("PETFAM_TIME") is { Length: > 0 } fake && TimeSpan.TryParse(fake, out var tod))
        {
            _sim.Clock.Pretend = tod;
            AppLog.Write($"Time of day (PETFAM_TIME): {tod:hh\\:mm}");
        }
        if (_sim.Clock.SunToday(DateTime.UtcNow) is { } sun)
            AppLog.Write($"Sun at {_weather.Location.Name}: rises {sun.Rise:HH:mm}, sets {sun.Set:HH:mm} (computer time)");

        _weather.Updated += w =>
        {
            AppLog.Write($"Weather: {w.Describe()} ({(w.IsDay ? "day" : "night")})");
            if (_pretend is null && _weather.Enabled) _sim.SetWeather(w);
            PetsMenuChanged?.Invoke();
        };
        if (Environment.GetEnvironmentVariable("PETFAM_WEATHER") is { Length: > 0 } test && ParseWeather(test) is { } w)
        {
            _pretend = w;
            _sim.SetWeather(w);
            AppLog.Write($"Weather (PETFAM_WEATHER): {w.Describe()}");
        }
        else
        {
            AppLog.Write($"Weather: {(_weather.Enabled ? "real weather for " + _weather.Location.Name : "off")}");
            _weather.Poll(a => Dispatcher.UIThread.Post(a));   // right away, not at the first 5-second poll
        }
        if (_pretend is { Kind: WeatherKind.Snow, Intensity: >= 1f }) _sim.DebugSnowCover(0.8f);
    }

    private static WeatherNow? ParseWeather(string s) => s.ToLowerInvariant() switch
    {
        "rain" => WeatherNow.Pretend(WeatherKind.Rain, 0.6f),
        "heavyrain" => WeatherNow.Pretend(WeatherKind.Rain, 1f),
        "drizzle" => WeatherNow.Pretend(WeatherKind.Rain, 0.25f),
        "thunder" => WeatherNow.Pretend(WeatherKind.Thunder, 0.9f),
        "snow" => WeatherNow.Pretend(WeatherKind.Snow, 0.7f),
        "heavysnow" => WeatherNow.Pretend(WeatherKind.Snow, 1f),
        "fog" => WeatherNow.Pretend(WeatherKind.Fog, 0.7f),
        "cloudy" => WeatherNow.Pretend(WeatherKind.Cloudy, 0.6f),
        "clear" => WeatherNow.Pretend(WeatherKind.Clear, 0f),
        _ => null,
    };

    /// <summary>"Seoul · Rain 12°C (as of 15:05)" for the tray.</summary>
    public string WeatherLine
    {
        get
        {
            if (_pretend is { } p) return L.S($"Test weather: {p.Describe()}", $"테스트 날씨: {p.Describe()}");
            if (!_weather.Enabled) return L.S("Real weather off (always clear)", "실제 날씨 꺼짐 (항상 맑음)");
            if (_weather.Latest is { } w) return L.S($"{w.Describe()} (as of {w.FetchedUtc.ToLocalTime():HH:mm})", $"{w.Describe()} ({w.FetchedUtc.ToLocalTime():HH:mm} 기준)");
            return _weather.LastError is null
                ? L.S($"Getting the weather for {_weather.Location.Name}…", $"{_weather.Location.Name} 날씨 가져오는 중…")
                : L.S($"Couldn't get the weather for {_weather.Location.Name} (retrying)", $"{_weather.Location.Name} 날씨를 못 가져옴 (다시 시도 중)");
        }
    }

    public string WeatherPlaceId => _weather.Location.Id;

    /// <summary>"Sunrise 07:01 · Sunset 17:31" (computer time) for the tray.</summary>
    public string SunLine
    {
        get
        {
            if (_sim.Clock.SunToday(DateTime.UtcNow) is not { } sun) return L.S("No sunrise today (polar night)", "오늘은 해가 뜨지 않아요 (극야)");
            var line = L.S($"Sunrise {sun.Rise:HH:mm} · Sunset {sun.Set:HH:mm}", $"해 뜸 {sun.Rise:HH:mm} · 해 짐 {sun.Set:HH:mm}");
            return _sim.Clock.Pretend is { } t ? line + L.S($" (test time {t:hh\\:mm})", $" (테스트 시간 {t:hh\\:mm})") : line;
        }
    }

    /// <summary>Tray test: pretend a time of day (null: the real clock).</summary>
    public void PretendTime(TimeSpan? timeOfDay)
    {
        _sim.Clock.Pretend = timeOfDay;
        AppLog.Write(timeOfDay is { } t ? $"Time of day: pretend {t:hh\\:mm}" : "Time of day: back to the real clock");
        PetsMenuChanged?.Invoke();
    }

    /// <summary>Today's sunset as a time of day (for the tray's test menu).</summary>
    public TimeSpan? SunsetToday => _sim.Clock.SunToday(DateTime.UtcNow)?.Set.TimeOfDay;

    public bool WeatherEnabled => _weather.Enabled;

    public void SetWeatherPlace(WeatherService.Place place)
    {
        _weather.Location = place;
        _sim.Clock.SetPlace(place.Latitude, place.Longitude);
        _weather.Refresh();
        AppLog.Write($"Weather: place set to {place.Name}");
        Save();
        PetsMenuChanged?.Invoke();
    }

    public void ToggleRealWeather()
    {
        _weather.Enabled = !_weather.Enabled;
        if (_weather.Enabled) _weather.Refresh();
        if (_pretend is null) _sim.SetWeather(_weather.Enabled && _weather.Latest is { } w ? w : WeatherNow.Clear);
        AppLog.Write($"Weather: real weather {(_weather.Enabled ? "on" : "off")}");
        Save();
        PetsMenuChanged?.Invoke();
    }

    /// <summary>Tray test: pretend a weather (null: back to the real one).</summary>
    public void PretendWeather(string? kind)
    {
        _pretend = kind is null ? null : ParseWeather(kind);
        if (kind == "heavysnow") _sim.DebugSnowCover(0.8f);   // test: already lying, to see it at once
        _sim.SetWeather(_pretend ?? (_weather.Enabled && _weather.Latest is { } w ? w : WeatherNow.Clear));
        if (_pretend is null) _weather.Refresh();
        AppLog.Write($"Weather: {(_pretend is null ? "back to real weather" : "pretend " + _pretend.Describe())}");
        PetsMenuChanged?.Invoke();
    }

    // ---------------------------------------------------------------- virtual desktops (plan §6.4)

    private const float DesktopPollSeconds = 0.25f;
    private float _sinceDesktopPoll;
    private Bitmap? _wallpaperBitmap;
    private readonly Dictionary<string, string> _desktopWallpapers = new();
    private const string SingleDesktopId = "desktop-0";

    /// <summary>
    /// Makes the world match the OS desktops and monitors: one cell per desktop and monitor, desktops in OS order.
    /// Pets, houses and bowls on a desktop that was closed move to the desktop on screen; those on a monitor that was
    /// unplugged move to the main monitor of their desktop. Returns the main monitor's cell of the current desktop.
    /// </summary>
    private Cell SyncDesktops(VirtualDesktopState? state)
    {
        var world = _sim.World;
        var ids = state?.Desktops.Select(d => d.Id).ToList() ?? new List<string> { SingleDesktopId };
        var currentId = state?.CurrentId ?? SingleDesktopId;

        if (!world.DesktopOrder.SequenceEqual(ids))
        {
            world.DesktopOrder.Clear();
            world.DesktopOrder.AddRange(ids);
        }
        world.DesktopNames.Clear();
        _desktopWallpapers.Clear();
        foreach (var d in state?.Desktops ?? Array.Empty<VirtualDesktop>())
        {
            if (d.Name.Length > 0) world.DesktopNames[d.Id] = d.Name;
            if (d.WallpaperPath is { } w) _desktopWallpapers[d.Id] = w;
        }

        var monitorIds = _monitors.Select(m => m.Id).ToList();
        foreach (var id in ids)
        foreach (var m in _monitors)
        {
            var existing = world.CellFor(id, m.Id);
            if (existing is not null && existing.Bounds == m.Bounds && existing.WorkingArea == m.Working) continue;
            var cell = world.Upsert(new Cell
            {
                Id = Cell.MakeId(id, m.Id),
                DesktopId = id,
                MonitorId = m.Id,
                Bounds = m.Bounds,
                WorkingArea = m.Working,
                Map = HabitatMap.Flat(m.Bounds, m.Working),
            });
            if (existing is not null)
            {
                // The screen changed size or place: same houses and bowls, kept on the new floor; analyse again.
                foreach (var h in existing.Houses) { h.Position = cell.Map.ClampToRoam(h.Position + Shift(existing, cell)); cell.Houses.Add(h); }
                foreach (var b in existing.Bowls) { b.Position = cell.Map.ClampToRoam(b.Position + Shift(existing, cell)); cell.Bowls.Add(b); }
                foreach (var pet in _sim.Pets.Where(p => p.CellId == cell.Id)) pet.Position = cell.Map.ClampToRoam(pet.Position + Shift(existing, cell));
                _analyzedKeys.Remove(cell.Id);
                AppLog.Write($"Monitor changed: {m.Id} now {m.Bounds}");
            }
            else if (state is not null && _cell is not null) AppLog.Write($"Cell added: {id} on {m.Id}");
        }

        var current = world.CellFor(currentId, MainMonitorId) ?? world.CellForDesktop(currentId)!;

        // Desktops that were closed, or monitors that were unplugged: everyone and everything on them comes to the
        // main monitor of their desktop, or to the desktop on screen.
        foreach (var gone in world.Cells.Where(c => !ids.Contains(c.DesktopId) || !monitorIds.Contains(c.MonitorId)).ToList())
        {
            var to = ids.Contains(gone.DesktopId) ? world.CellFor(gone.DesktopId, MainMonitorId) ?? current : current;
            foreach (var pet in _sim.Pets.Where(p => p.CellId == gone.Id || p.Trip?.To == gone))
            {
                if (pet.Trip is not null) pet.Trip = null;
                pet.Hidden = false;
                pet.CellId = to.Id;
                pet.Position = to.Map.ClampToRoam(pet.Position + Shift(gone, to));
                pet.Engine.ForceStop(new PetFam.Core.Behavior.BehaviorContext
                {
                    Pet = pet, Cell = to, User = _sim.User, Rng = new Random(), Debug = _sim.Debug, NowUtc = DateTime.UtcNow,
                });
            }
            foreach (var pet in _sim.Pets.Where(p => p.HomeCellId == gone.Id)) pet.HomeCellId = to.Id;
            foreach (var house in gone.Houses.Where(_ => to.Houses.Count < Cell.MaxHouses))
            {
                house.Position = to.Map.ClampToRoam(house.Position + Shift(gone, to));
                to.Houses.Add(house);
            }
            foreach (var bowl in gone.Bowls)
            {
                bowl.Position = to.Map.ClampToRoam(bowl.Position + Shift(gone, to));
                to.Bowls.Add(bowl);
            }
            world.Remove(gone.Id);
            _analyzedKeys.Remove(gone.Id);
            AppLog.Write(ids.Contains(gone.DesktopId)
                ? $"Monitor gone: {gone.MonitorId}; its pets and houses moved to {world.DescribeCell(to)}"
                : $"Desktop closed: {gone.DesktopId}; its pets and houses moved to {world.DescribeCell(to)}");
            Save();
        }
        return current;
    }

    /// <summary>How far things move when they go from one screen to the matching spot on another.</summary>
    private static Vec2 Shift(Cell from, Cell to) =>
        new(to.Bounds.X + to.Bounds.Width / 2f - (from.Bounds.X + from.Bounds.Width / 2f), to.Bounds.Y + to.Bounds.Height / 2f - (from.Bounds.Y + from.Bounds.Height / 2f));

    // ---------------------------------------------------------------- monitors

    /// <summary>Reads the monitors: the main one first (id "primary", as before multi-monitor), then left to right.</summary>
    private void ReadMonitors()
    {
        var screens = _backyard!.Screens;
        var main = screens.Primary ?? screens.All[0];
        _monitors = screens.All
            .OrderBy(s => s == main ? 0 : 1).ThenBy(s => s.Bounds.X).ThenBy(s => s.Bounds.Y)
            .Select(s => new Monitor(s == main ? MainMonitorId : $"mon-{s.Bounds.X}-{s.Bounds.Y}", ToRectI(s.Bounds), ToRectI(s.WorkingArea), s))
            .ToList();
        _monitorKey = string.Join(";", _monitors.Select(m => $"{m.Id}={m.Bounds}/{m.Working}"));
        _sim.World.MonitorOrder.Clear();
        _sim.World.MonitorOrder.AddRange(_monitors.Select(m => m.Id));
        foreach (var m in _monitors)
            AppLog.Write($"Monitor {m.Id}: bounds={m.Bounds} working={m.Working} scaling={m.Screen.Scaling}");
    }

    /// <summary>A monitor was plugged in or unplugged, or changed resolution: rebuild cells and windows to match.</summary>
    private void UpdateMonitors()
    {
        if (_backyard is null) return;
        var screens = _backyard.Screens.All;
        var key = string.Join(";", screens
            .OrderBy(s => s == (_backyard.Screens.Primary ?? screens[0]) ? 0 : 1).ThenBy(s => s.Bounds.X).ThenBy(s => s.Bounds.Y)
            .Select(s => $"{(s == (_backyard.Screens.Primary ?? screens[0]) ? MainMonitorId : $"mon-{s.Bounds.X}-{s.Bounds.Y}")}={ToRectI(s.Bounds)}/{ToRectI(s.WorkingArea)}"));
        if (key == _monitorKey) return;
        AppLog.Write("Monitors changed: " + key);
        ReadMonitors();
        var current = SyncDesktops(Volatile.Read(ref _desktopState));

        foreach (var v in _views.Where(v => _monitors.All(m => m.Id != v.MonitorId)).ToList())
        {
            v.Backyard.Close();
            v.Overlay.Close();
            _views.Remove(v);
        }
        foreach (var m in _monitors)
        {
            if (_views.FirstOrDefault(v => v.MonitorId == m.Id) is { } view)
            {
                view.Screen = m.Screen;
                view.Cell = _sim.World.CellFor(current.DesktopId, m.Id) ?? current;
                view.Backyard.ShowCell(view.Cell);
                view.Overlay.ShowCell(view.Cell);
                view.Backyard.ApplyBounds(m.Screen);
            }
            else
            {
                AddView(m, new BackyardWindow(), new OverlayWindow());
            }
        }
        SwitchTo(current, Volatile.Read(ref _desktopState)?.CurrentId);
        AnalyzeHabitat(force: true);
    }

    /// <summary>Opens the backyard and overlay windows for one monitor, showing its cell of the current desktop.</summary>
    private void AddView(Monitor m, BackyardWindow backyard, OverlayWindow overlay)
    {
        var cell = _sim.World.CellFor((_cell ?? _sim.World.AnyCell()!).DesktopId, m.Id) ?? _cell!;
        var view = new MonitorView { MonitorId = m.Id, Screen = m.Screen, Backyard = backyard, Overlay = overlay, Cell = cell };
        _views.Add(view);
        backyard.Configure(m.Screen, cell, _sim, _platform, _layering, _wallpaperBitmap, _platform.GetWallpaperFit());
        overlay.Configure(m.Screen, cell, _sim, _platform, _layering);
        backyard.Show();
        // The overlay (window edges, HUD) stays up, empty and click-through, for the whole run: showing it when a pet
        // first peeked set up its drawing surface on the spot and stalled the screen for about two seconds, and so
        // did showing it again after hiding. An empty transparent window costs next to nothing to compose.
        overlay.Show();
        overlay.ApplyClickThrough();
    }

    /// <summary>Display scaling of the monitor under this screen point.</summary>
    private double ScalingAt(PixelPoint p) =>
        _monitors.FirstOrDefault(m => m.Bounds.Contains(new Vec2(p.X, p.Y)))?.Screen.Scaling
        ?? _backyard?.Screens.Primary?.Scaling ?? 1.0;

    private readonly Dictionary<Guid, string> _lastCellOf = new();

    /// <summary>Logs every pet that changed desktop since the last check.</summary>
    private void LogTravel()
    {
        foreach (var pet in _sim.Pets)
        {
            if (_lastCellOf.TryGetValue(pet.Id, out var was) && was != pet.CellId)
            {
                var from = _sim.World.Find(was);
                var to = _sim.World.Find(pet.CellId);
                AppLog.Write($"Travel: {pet.Name} {(from is null ? was : _sim.World.DescribeDesktop(from))} -> " +
                             $"{(to is null ? pet.CellId : _sim.World.DescribeCell(to))} ({pet.Trip?.Reason.ToString() ?? "moved"}, mode {pet.Travel})");
                PetsMenuChanged?.Invoke();
            }
            _lastCellOf[pet.Id] = pet.CellId;
        }
    }

    private VirtualDesktopState? _desktopState;
    private bool _desktopStateKnown;
    private int _desktopPollRunning;

    /// <summary>
    /// Reading the desktops means registry reads and a call into Explorer, which now and then takes tens of
    /// milliseconds: done on a worker thread, and the frame applies the latest result.
    /// </summary>
    private void UpdateDesktops()
    {
        LogTravel();
        if (_cell is null) return;
        if (Interlocked.Exchange(ref _desktopPollRunning, 1) == 0)
            Task.Run(() =>
            {
                try
                {
                    var state = _platform.GetVirtualDesktops();
                    Volatile.Write(ref _desktopState, state);
                    _desktopStateKnown = true;
                }
                catch (Exception ex) { AppLog.Write("Desktop poll failed: " + ex.Message); }
                finally { Volatile.Write(ref _desktopPollRunning, 0); }
            });
        if (!_desktopStateKnown) return;
        var latest = Volatile.Read(ref _desktopState);
        var current = SyncDesktops(latest);
        if (!ReferenceEquals(current, _cell)) SwitchTo(current, latest?.CurrentId);
    }

    /// <summary>The user switched virtual desktops: show that desktop's pets, houses and bowls.</summary>
    private void SwitchTo(Cell cell, string? desktopId)
    {
        _menu?.Close();
        Deselect();
        foreach (var f in _fronts.Values) f.Close();
        _fronts.Clear();
        _ballFront?.Close();
        _ballFront = null;
        _houseFront?.Close();
        _houseFront = null;

        _cell = cell;
        foreach (var v in _views)
        {
            v.Cell = _sim.World.CellFor(cell.DesktopId, v.MonitorId) ?? cell;
            v.Backyard.ShowCell(v.Cell);
            v.Overlay.ShowCell(v.Cell);
        }
        _sim.ShowCells(_views.Count > 0 ? _views.Select(v => v.Cell).ToList() : new List<Cell> { cell }, DateTime.UtcNow);
        if (desktopId is not null) _layering.MoveOwnWindowsToDesktop(desktopId);

        var here = string.Join(", ", _sim.Pets.Where(_sim.IsVisible).Select(p => p.Name));
        AppLog.Write($"Desktop: switched to {_sim.World.DescribeDesktop(cell)} (here: {(here.Length > 0 ? here : "nobody")})");
        _sinceWallpaperPoll = WallpaperPollSeconds;   // analyse its wallpaper on the next frame if new
        PetsMenuChanged?.Invoke();
    }

    /// <summary>"Momo · sleeping on Desktop 2" for the tray.</summary>
    public string WhereIs(Pet pet)
    {
        var cell = _sim.World.Find(pet.CellId);
        var place = cell is null || _sim.World.Cells.Count < 2 ? "" :
            _sim.VisibleCellIds.Contains(cell.Id) ? (_sim.World.MonitorOrder.Count > 1 && !ReferenceEquals(cell, _cell) ? _sim.World.DescribeMonitor(cell) : L.S("here", "여기"))
            : _sim.World.DescribeCell(cell);
        var doing = pet.Trip is not null ? L.S("travelling", "이동 중") :
            pet.Engine.Current is PetFam.Core.Behavior.Behaviors.SleepBehavior ? L.S("sleeping", "자는 중") :
            pet.Engine.Current is PetFam.Core.Behavior.Behaviors.EatBehavior ? L.S("eating", "먹는 중") :
            pet.Session is not null ? L.S("playing", "노는 중") : "";
        var text = place.Length == 0 ? doing : doing.Length == 0 ? place : L.S($"{doing} {(place == "here" ? "here" : "on " + place)}", $"{place}에서 {doing}");
        return text.Length == 0 ? pet.Name : $"{pet.Name} · {text}";
    }

    /// <summary>Raised when the tray's pet list should be rebuilt.</summary>
    public event Action? PetsMenuChanged;

    /// <summary>Raised when the whole tray menu should be rebuilt (e.g. the wallpaper menu changed).</summary>
    public event Action? MenuChanged;

    public IReadOnlyList<Pet> Pets => _sim.Pets;

    public bool HasDesktops => _sim.World.DesktopOrder.Count > 1;

    public bool IsHere(Pet pet) => _cell is not null && _sim.VisibleCellIds.Contains(pet.CellId);

    /// <summary>Tray: call a pet on another desktop.</summary>
    public void CallPet(Pet pet)
    {
        var from = WhereIs(pet);
        AppLog.Write(_sim.CallPet(pet) ? $"Called {from}" : $"Call ignored: {from}");
    }

    /// <summary>Tray: free / follow / home. Home means the desktop it is on now.</summary>
    public void SetTravelMode(Pet pet, TravelMode mode)
    {
        pet.Travel = mode;
        pet.HomeCellId = mode == TravelMode.Home ? pet.Trip?.To.Id ?? pet.CellId : null;
        AppLog.Write($"Travel mode: {pet.Name} -> {mode}{(pet.HomeCellId is { } h && _sim.World.Find(h) is { } c ? " (" + _sim.World.DescribeDesktop(c) + ")" : "")}");
        Save();
    }

    // ---------------------------------------------------------------- persistence

    private void LoadOrCreatePets(Cell cell)
    {
        var now = DateTime.UtcNow;
        var save = SaveStore.Read(_savePath);
        var rng = new Random();

        // Language: English unless the user chose Korean (PETFAM_LANG=ko|en overrides, for testing).
        var lang = Environment.GetEnvironmentVariable("PETFAM_LANG") is { Length: > 0 } envLang ? envLang : save?.Language;
        if (lang is not null) L.Set(L.Parse(lang));

        if (save?.Weather is { } ws)
        {
            _weather.Location = WeatherService.Find(ws.Place)
                                ?? (ws.Place.Length > 0 ? new WeatherService.Place(ws.Place, new Loc(ws.Place, ws.Place), ws.Latitude, ws.Longitude) : WeatherService.DefaultPlace());
            _weather.Enabled = ws.Enabled;
        }

        foreach (var hs in save?.Houses ?? new())
        {
            var target = _sim.World.Find(hs.CellId) ?? cell;
            if (target.Houses.Count < Cell.MaxHouses) target.Houses.Add(SaveStore.Restore(hs));
        }

        if (save is { Pets.Count: > 0 })
        {
            foreach (var ps in save.Pets)
            {
                var pet = SaveStore.Restore(ps, now, rng);
                if (_sim.World.Find(pet.CellId) is null) pet.CellId = cell.Id;
                pet.Position = (_sim.World.Find(pet.CellId) ?? cell).Map.ClampToRoam(pet.Position);
                _sim.Pets.Add(pet);
            }
            foreach (var r in save.Relations)
                _sim.Social.Restore(new PetFam.Core.Social.Relation { A = r.A, B = r.B, Friendship = r.Friendship, Dominance = r.Dominance });
            AppLog.Write($"Loaded {save.Pets.Count} pet(s), {save.Relations.Count} relation(s) from {_savePath}");
            return;
        }

        // First run: the user picks the first pet in the onboarding window.
        _needsOnboarding = true;
        AppLog.Write("No pets yet: onboarding");
    }

    private Task _saving = Task.CompletedTask;

    /// <summary>
    /// Takes a snapshot of the world on the UI thread and writes it to disk on a background thread (one write at a
    /// time, in order), so saving never stalls the animation. <paramref name="wait"/>: finish before returning (exit).
    /// </summary>
    private void Save(bool wait = false)
    {
        SaveData data;
        try
        {
            data = SaveStore.ToSave(_sim.Pets, DateTime.UtcNow, _sim.Social, _sim.World.Cells);
            data.Language = L.Code(L.Current);
            data.Weather = new WeatherSettingsSave
            {
                Place = _weather.Location.Id, Latitude = _weather.Location.Latitude, Longitude = _weather.Location.Longitude,
                Enabled = _weather.Enabled,
            };
        }
        catch (Exception ex)
        {
            AppLog.Write("Save failed: " + ex.Message);
            return;
        }
        var path = _savePath;
        _saving = _saving.ContinueWith(_ =>
        {
            try { SaveStore.Write(path, data); }
            catch (Exception ex) { AppLog.Write("Save failed: " + ex.Message); }
        }, TaskScheduler.Default);
        if (wait) _saving.Wait(TimeSpan.FromSeconds(5));
    }

    private Bitmap? LoadWallpaper()
    {
        try
        {
            var path = WallpaperPath();
            AppLog.Write($"Wallpaper: {path ?? "(none)"} fit={_platform.GetWallpaperFit()}");

            // The desktop layer is transparent, so the real wallpaper shows through. Painting it ourselves
            // is only a fallback (PETFAM_PAINT_WALLPAPER=1) or for testing another picture (PETFAM_WALLPAPER).
            var paint = Environment.GetEnvironmentVariable("PETFAM_PAINT_WALLPAPER") == "1" || WallpaperOverride is not null;
            if (path is null || !paint) return null;

            using var full = new Bitmap(path);
            var cell = _sim.World.AnyCell()!;
            var scale = Math.Max((double)cell.Bounds.Width / full.PixelSize.Width, (double)cell.Bounds.Height / full.PixelSize.Height);
            var size = new PixelSize((int)Math.Ceiling(full.PixelSize.Width * scale), (int)Math.Ceiling(full.PixelSize.Height * scale));
            return full.CreateScaledBitmap(size);
        }
        catch (Exception ex)
        {
            AppLog.Write("Wallpaper load failed: " + ex.Message);
            return null;
        }
    }

    private static RectI ToRectI(PixelRect r) => new(r.X, r.Y, r.Width, r.Height);
}
