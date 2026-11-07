using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace ZooZoo.Desktop;

public partial class App : Application
{
    private AppHost? _host;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _host = new AppHost(desktop);
            desktop.Exit += (_, _) => _host.Stop();
            _host.Start();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnFeed(object? sender, EventArgs e) => _host?.FeedAll();

    private void OnStroke(object? sender, EventArgs e) => _host?.StrokeAll();

    private void OnTowel(object? sender, EventArgs e) => _host?.TowelAll();

    private void OnDebugWet(object? sender, EventArgs e) => _host?.DebugSoak();

    private void OnDebugMud(object? sender, EventArgs e) => _host?.DebugMud();

    private void OnBondUp(object? sender, EventArgs e) => _host?.DebugBond(add: 20f);
    private void OnBondDown(object? sender, EventArgs e) => _host?.DebugBond(add: -20f);
    private void OnBondWary(object? sender, EventArgs e) => _host?.DebugBond(set: 15f);
    private void OnBondCurious(object? sender, EventArgs e) => _host?.DebugBond(set: 30f);
    private void OnBondFamiliar(object? sender, EventArgs e) => _host?.DebugBond(set: 50f);
    private void OnBondTrusting(object? sender, EventArgs e) => _host?.DebugBond(set: 70f);
    private void OnBondFamily(object? sender, EventArgs e) => _host?.DebugBond(set: 100f);
    private void OnBondReset(object? sender, EventArgs e) => _host?.DebugBond(set: 0f);

    private void OnToggleHud(object? sender, EventArgs e) => _host?.ToggleHud();

    private void OnToggleHabitat(object? sender, EventArgs e) => _host?.ToggleHabitat();

    private void OnAdoptCat(object? sender, EventArgs e) => _host?.AdoptPet(ZooZoo.Core.Pets.PetSpecies.Cat);

    private void OnAdoptDog(object? sender, EventArgs e) => _host?.AdoptPet(ZooZoo.Core.Pets.PetSpecies.Dog);

    private void OnDebugPlay(object? sender, EventArgs e) => _host?.DebugPlay();

    private void OnReanalyze(object? sender, EventArgs e) => _host?.AnalyzeHabitat(force: true);

    private void OnSnapshot(object? sender, EventArgs e) => _host?.SaveSnapshot();

    private void OnExit(object? sender, EventArgs e) =>
        (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
}
