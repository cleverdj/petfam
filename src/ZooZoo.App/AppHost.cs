using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using ZooZoo.Core;
using ZooZoo.Core.Geometry;
using ZooZoo.Core.Persistence;
using ZooZoo.Core.Pets;
using ZooZoo.Core.Platform;
using ZooZoo.Core.World;
using ZooZoo.Desktop.Rendering;
using ZooZoo.Desktop.Windows;
using ZooZoo.Platform.MacOS;
using ZooZoo.Platform.Windows;

namespace ZooZoo.Desktop;

/// <summary>
/// Owns the simulation, the two layer windows and the frame timer. Spike scope: one cell
/// (primary monitor, current desktop). Multi-monitor and virtual desktops come next.
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
    private readonly string _savePath = Path.Combine(AppLog.DataDirectory, "save.json");
    private readonly Stopwatch _clock = new();

    private BackyardWindow? _backyard;
    private OverlayWindow? _overlay;
    private readonly Dictionary<Guid, FrontWindow> _fronts = new();
    private Cell? _cell;
    private DispatcherTimer? _timer;
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
            _platform = mac;
            _layering = mac;
        }

        _sim = new Simulation(_platform);

        // Spike diagnostics.
        if (Environment.GetEnvironmentVariable("ZOOZOO_NO_PEEK") == "1") _sim.Debug.AllowPeekAlways = false;
        if (double.TryParse(Environment.GetEnvironmentVariable("ZOOZOO_CALM_FPS"), out var fps) && fps > 0)
            _calmFrameSeconds = 1.0 / fps;
    }

    private double _calmFrameSeconds = CalmFrameSeconds;

    public void Start()
    {
        AppLog.Write("---- ZooZoo starting ----");

        _backyard = new BackyardWindow();
        _overlay = new OverlayWindow();

        var screen = _backyard.Screens.Primary ?? _backyard.Screens.All[0];
        var bounds = ToRectI(screen.Bounds);
        var working = ToRectI(screen.WorkingArea);
        AppLog.Write($"Primary screen bounds={bounds} working={working} scaling={screen.Scaling}");

        var cell = _sim.World.Upsert(new Cell
        {
            Id = Cell.MakeId("desktop-0", "monitor-0"),
            DesktopId = "desktop-0",
            MonitorId = "monitor-0",
            Bounds = bounds,
            WorkingArea = working,
            Map = HabitatMap.Flat(bounds, working),
        });
        _cell = cell;

        LoadOrCreatePets(cell);

        // Test option: ZOOZOO_ADOPT=cat,dog brings extra pets at startup (same-species play).
        foreach (var kind in (Environment.GetEnvironmentVariable("ZOOZOO_ADOPT") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var species = kind.StartsWith("c", StringComparison.OrdinalIgnoreCase) ? PetSpecies.Cat : PetSpecies.Dog;
            if (_sim.AdoptPet(species, cell, DateTime.UtcNow) is { } extra) AppLog.Write($"ZOOZOO_ADOPT: {extra.Name}");
        }

        AnalyzeHabitat();
        if (Environment.GetEnvironmentVariable("ZOOZOO_SHOW_HABITAT") == "1") _sim.Debug.ShowHabitat = true;

        // Test option: ZOOZOO_BOND=<0..100> sets every pet's bond at startup.
        if (float.TryParse(Environment.GetEnvironmentVariable("ZOOZOO_BOND"), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var bond))
        {
            foreach (var p in _sim.Pets) p.Bond.DebugSet(bond, DateTime.UtcNow);
            AppLog.Write($"ZOOZOO_BOND={bond}: bond set for all pets");
        }

        var wallpaper = LoadWallpaper();
        _backyard.Configure(screen, cell, _sim, _platform, _layering, wallpaper, _platform.GetWallpaperFit());
        _overlay.Configure(screen, cell, _sim, _platform, _layering);

        _backyard.Show();
        // Overlay is shown on demand (peek / HUD) so an idle app has a single composited window.

        _clock.Start();
        _lastTime = 0;
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(_calmFrameSeconds), DispatcherPriority.Render, OnFrame);
        _timer.Start();

        if (Environment.GetEnvironmentVariable("ZOOZOO_POSE_SHEET") == "1")
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
        if (Environment.GetEnvironmentVariable("ZOOZOO_DEMO_MENU") is { Length: > 0 } demo)
        {
            DispatcherTimer.RunOnce(() =>
            {
                var pet = _sim.Pets[0];
                if (demo.Contains("queue"))
                {
                    foreach (var id in new[] { "ball", "pet", "treat" })
                        if (ZooZoo.Core.Interactions.InteractionCatalog.Find(id) is { } i) _sim.Enqueue(pet, i, DateTime.UtcNow);
                }
                OpenMenu(pet);
                pet.Say("야옹?");
            }, TimeSpan.FromSeconds(4));
        }

        if (Environment.GetEnvironmentVariable("ZOOZOO_SNAPSHOT") == "1")
        {
            var delay = double.TryParse(Environment.GetEnvironmentVariable("ZOOZOO_SNAPSHOT_DELAY"), out var d) ? d : 3;
            DispatcherTimer.RunOnce(() => SaveSnapshot(), TimeSpan.FromSeconds(delay));
        }
    }

    /// <summary>Renders the backyard scene to a PNG in the data folder, for checking what the layer draws.</summary>
    public string? SaveSnapshot()
    {
        if (_backyard is null) return null;
        try
        {
            var cell = _sim.World.AnyCell()!;
            var dpi = 96 * (_backyard.Screens.Primary?.Scaling ?? 1.0);
            using var rtb = new RenderTargetBitmap(new PixelSize(cell.Bounds.Width, cell.Bounds.Height), new Vector(dpi, dpi));
            rtb.Render(_backyard.RootVisual);
            var path = Path.Combine(AppLog.DataDirectory, $"snapshot-{DateTime.Now:yyyyMMdd-HHmmss}.png");
            rtb.Save(path);
            AppLog.Write("Snapshot saved: " + path);
            return path;
        }
        catch (Exception ex)
        {
            AppLog.Write("Snapshot failed: " + ex.Message);
            return null;
        }
    }

    public void Stop()
    {
        _timer?.Stop();
        Save();
        AppLog.Write("---- ZooZoo stopped ----");
    }

    // ---------------------------------------------------------------- tray actions

    public void FeedAll()
    {
        var now = DateTime.UtcNow;
        foreach (var p in _sim.Pets) _sim.Feed(p, now);
    }

    public void StrokeAll()
    {
        var now = DateTime.UtcNow;
        foreach (var p in _sim.Pets) _sim.Stroke(p, now);
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
            p.Say($"유대감 {value:F0}", 1.8f);
            AppLog.Write($"Debug bond: {p.Name} -> {value:F0} ({p.Bond.Stage})");
        }
        _panel?.Refresh();
        Save();
    }

    public void ToggleHud() => _sim.Debug.ShowHud = !_sim.Debug.ShowHud;

    public void ToggleHabitat() => _sim.Debug.ShowHabitat = !_sim.Debug.ShowHabitat;

    /// <summary>Tray: adopt another cat or dog (up to six pets) so same-species play can happen.</summary>
    public void AdoptPet(PetSpecies species)
    {
        if (_cell is null) return;
        var pet = _sim.AdoptPet(species, _cell, DateTime.UtcNow);
        if (pet is null) { AppLog.Write("Adopt: already at the maximum number of pets"); return; }
        pet.Say("안녕!", 2.5f);
        AppLog.Write($"Adopted {pet.Name} ({species})");
        Save();
    }

    /// <summary>Test: make the first two free pets start playing right now.</summary>
    public void DebugPlay()
    {
        var free = _sim.Pets.Where(ZooZoo.Core.Social.SocialDirector.Available).ToList();
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
    private string _analyzedKey = "";
    private bool _analyzing;
    private float _sinceWallpaperPoll;

    /// <summary>
    /// Analyses the current wallpaper on a worker thread and swaps the cell's map when done. Re-runs only when the
    /// wallpaper file (path, size, time) changed, unless forced.
    /// </summary>
    public void AnalyzeHabitat(bool force = false)
    {
        if (_cell is null || _analyzing) return;
        var path = WallpaperPath();
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
        if (!force && key == _analyzedKey) return;
        _analyzedKey = key;

        var cell = _cell;
        if (path is null)
        {
            ApplyHabitat(cell, null, "no wallpaper file (solid colour or slideshow API): flat floor");
            return;
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
    }

    /// <summary>Test option: ZOOZOO_WALLPAPER=&lt;image&gt; uses another picture (painted in the backyard) without touching the real wallpaper.</summary>
    private static readonly string? WallpaperOverride =
        Environment.GetEnvironmentVariable("ZOOZOO_WALLPAPER") is { Length: > 0 } p && File.Exists(p) ? p : null;

    private string? WallpaperPath() => WallpaperOverride ?? _platform.GetWallpaperPath();

    private void ApplyHabitat(Cell cell, WalkMask? mask, string report)
    {
        cell.Map = mask is null ? HabitatMap.Flat(cell.Bounds, cell.WorkingArea) : HabitatMap.FromMask(cell.Bounds, cell.WorkingArea, mask);
        foreach (var pet in _sim.Pets.Where(p => p.CellId == cell.Id))
        {
            if (pet.Hold == HoldState.Carried) continue;
            pet.Position = cell.Map.ClampToRoam(pet.Position);
            pet.Engine.ForceStop(new ZooZoo.Core.Behavior.BehaviorContext
            {
                Pet = pet, Cell = cell, User = _sim.User, Rng = new Random(), Debug = _sim.Debug, NowUtc = DateTime.UtcNow,
            });
        }
        foreach (var bowl in cell.Bowls) bowl.Position = cell.Map.ClampToRoam(bowl.Position);
        AppLog.Write($"Habitat: {report}; roam={cell.Map.RoamArea} mask={(mask is null ? "none" : mask.Count + " cells")}");
    }

    // ---------------------------------------------------------------- frame

    private void OnFrame(object? sender, EventArgs e)
    {
        var now = _clock.Elapsed.TotalSeconds;
        var dt = (float)(now - _lastTime);
        _lastTime = now;

        _sim.Tick(dt, DateTime.UtcNow);

        _sinceWallpaperPoll += dt;
        if (_sinceWallpaperPoll >= WallpaperPollSeconds)
        {
            _sinceWallpaperPoll = 0f;
            AnalyzeHabitat();
        }

        UpdateFrontWindows();
        UpdateSelection(dt);
        _backyard?.RenderFrame();
        UpdateOverlayVisibility();
        UpdateFrameRate();

        _sinceSave += dt;
        if (_sinceSave >= AutosaveSeconds)
        {
            _sinceSave = 0f;
            Save();
        }
    }

    private void UpdateOverlayVisibility()
    {
        if (_overlay is null) return;

        if (_overlay.HasContent)
        {
            if (!_overlay.IsVisible)
            {
                _overlay.Show();
                _overlay.ApplyClickThrough();
            }
            _overlay.RenderFrame();
        }
        else if (_overlay.IsVisible)
        {
            _overlay.Hide();
        }
    }

    /// <summary>
    /// 앞마당: when the cursor is over a visible pet (bare desktop under the cursor), or while the user holds
    /// one, that pet moves to a small topmost window that takes mouse input. Otherwise it lives in the backyard.
    /// </summary>
    private void UpdateFrontWindows()
    {
        if (_cell is null) return;
        var cursor = _sim.User.Cursor;

        foreach (var pet in _sim.Pets)
        {
            if (pet.CellId != _cell.Id || pet.Layer == PetLayer.WindowEdge) continue;

            _fronts.TryGetValue(pet.Id, out var front);
            if (pet.Hidden)
            {
                // Inside the house: not touchable.
                if (pet.Layer == PetLayer.Front) { pet.Layer = PetLayer.Backyard; front?.Hide(); }
                continue;
            }
            var held = pet.Hold != HoldState.None || (front?.IsHolding ?? false);
            var depth = _cell.Map.DepthScale(pet.Position.Y);
            var box = Expand(PetRenderer.HitBox(pet, depth), 20);
            var hovering = _sim.User.CursorOnDesktop && box.Contains(cursor);

            if (held || hovering)
            {
                if (front is null)
                {
                    front = new FrontWindow(pet, _sim, _cell, _platform, _layering, OpenMenu);
                    _fronts[pet.Id] = front;
                }
                if (!front.IsVisible) front.Show();
                pet.Layer = PetLayer.Front;
                front.Follow();
            }
            else if (pet.Layer == PetLayer.Front)
            {
                pet.Layer = PetLayer.Backyard;
                front?.Hide();
            }
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
        _menu?.Close();
        Select(pet);
        _sim.BeginAttention(pet);

        var depth = _cell.Map.DepthScale(pet.Position.Y);
        var box = PetRenderer.HitBox(pet, depth);
        var center = new PixelPoint(box.X + box.Width / 2, box.Y + (int)(box.Height * 0.3));
        var scaling = _backyard?.Screens.Primary?.Scaling ?? 1.0;

        var menu = new PieMenuWindow(pet, _sim.MenuFor(pet, DateTime.UtcNow), center, scaling, interaction =>
        {
            if (_sim.Enqueue(pet, interaction, DateTime.UtcNow))
                AppLog.Write($"Queued {interaction.Id} for {pet.Name}");
            _sinceSelectionActivity = 0f;
        });
        menu.Closed += (_, _) =>
        {
            _sim.EndAttention(pet);
            if (ReferenceEquals(_menu, menu)) _menu = null;
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
            };
        }
        _panel.ShowFor(pet);
    }

    /// <summary>"서열 1/3 · 단짝: 콩이 (친밀 72)" for the panel.</summary>
    private string SocialSummary(Pet pet)
    {
        if (_sim.Pets.Count < 2) return "";
        var rank = _sim.Social.Rank(pet, _sim.Pets);
        var friend = _sim.Social.BestFriend(pet, _sim.Pets);
        var text = $"서열 {rank}/{_sim.Pets.Count}";
        if (friend is not null) text += $" · 단짝: {friend.Name} (친밀 {_sim.Social.Friendship(pet, friend):F0})";
        if (pet.Session is { } s) text += $"\n지금: {(ReferenceEquals(s.Initiator, pet) ? s.Partner.Name : s.Initiator.Name)}와(과) 노는 중";
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

    private void UpdateFrameRate()
    {
        if (_timer is null) return;
        var active = _sim.Debug.ShowHud || (_cell?.Ball is not null) || _sim.Director.Active.Count > 0 || _sim.Pets.Any(p =>
            p.Selected || p.Bubble is not null || p.Queue.Count > 0 || p.Lift > 0f || p.Animation is "stalk" or "wiggle" or "bat" ||
            p.Layer != PetLayer.Backyard || p.Animation is "walk" or "run" or "shake" or "stretch" or "eat" or "dig" or "groom" or "approach-food");
        var wanted = TimeSpan.FromSeconds(active ? ActiveFrameSeconds : _calmFrameSeconds);
        if (_timer.Interval != wanted) _timer.Interval = wanted;
    }

    // ---------------------------------------------------------------- persistence

    private void LoadOrCreatePets(Cell cell)
    {
        var now = DateTime.UtcNow;
        var save = SaveStore.Read(_savePath);
        var rng = new Random();

        if (save is { Pets.Count: > 0 })
        {
            foreach (var ps in save.Pets)
            {
                var pet = SaveStore.Restore(ps, now, rng);
                if (_sim.World.Find(pet.CellId) is null) pet.CellId = cell.Id;
                pet.Position = cell.Map.ClampToRoam(pet.Position);
                _sim.Pets.Add(pet);
            }
            foreach (var r in save.Relations)
                _sim.Social.Restore(new ZooZoo.Core.Social.Relation { A = r.A, B = r.B, Friendship = r.Friendship, Dominance = r.Dominance });
            AppLog.Write($"Loaded {save.Pets.Count} pet(s), {save.Relations.Count} relation(s) from {_savePath}");
            return;
        }

        _sim.AddPet(PetSpecies.Cat, "모모", cell, now);
        var dog = _sim.AddPet(PetSpecies.Dog, "콩이", cell, now);
        dog.Position = cell.Map.ClampToRoam(new Vec2(dog.Position.X + 220, dog.Position.Y + 120));
        AppLog.Write("Created default pets 모모(고양이), 콩이(강아지)");
    }

    private void Save()
    {
        try
        {
            SaveStore.Write(_savePath, SaveStore.ToSave(_sim.Pets, DateTime.UtcNow, _sim.Social));
        }
        catch (Exception ex)
        {
            AppLog.Write("Save failed: " + ex.Message);
        }
    }

    private Bitmap? LoadWallpaper()
    {
        try
        {
            var path = WallpaperPath();
            AppLog.Write($"Wallpaper: {path ?? "(none)"} fit={_platform.GetWallpaperFit()}");

            // The desktop layer is transparent, so the real wallpaper shows through. Painting it ourselves
            // is only a fallback (ZOOZOO_PAINT_WALLPAPER=1) or for testing another picture (ZOOZOO_WALLPAPER).
            var paint = Environment.GetEnvironmentVariable("ZOOZOO_PAINT_WALLPAPER") == "1" || WallpaperOverride is not null;
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
