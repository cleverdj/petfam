using Avalonia;
using Avalonia.Controls;

namespace PetFam.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // One PetFam at a time: a second copy (the desktop shortcut clicked while it already runs at sign-in) would
        // show every pet twice and fight over the save file. PETFAM_MULTI=1 allows more (developer sheets).
        using var single = new Mutex(true, @"Local\PetFam.SingleInstance", out var first);
        if (!first && Environment.GetEnvironmentVariable("PETFAM_MULTI") != "1")
        {
            AppLog.Write("Already running; this copy exits.");
            return;
        }

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
        }
        catch (Exception ex)
        {
            AppLog.Write("FATAL " + ex);
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();

        // Spike diagnostics: PETFAM_RENDER=software|angle|wgl forces a Win32 rendering backend,
        // PETFAM_COMP=winui|dcomp|redirect forces a composition mode.
        if (OperatingSystem.IsWindows())
        {
            var opts = new Win32PlatformOptions();
            var render = Environment.GetEnvironmentVariable("PETFAM_RENDER");
            if (!string.IsNullOrEmpty(render))
            {
                var mode = render.ToLowerInvariant() switch
                {
                    "software" => Win32RenderingMode.Software,
                    "wgl" => Win32RenderingMode.Wgl,
                    _ => Win32RenderingMode.AngleEgl,
                };
                opts.RenderingMode = new[] { mode };
                AppLog.Write($"Forced rendering mode: {mode}");
            }

            var comp = Environment.GetEnvironmentVariable("PETFAM_COMP");
            if (!string.IsNullOrEmpty(comp))
            {
                var mode = comp.ToLowerInvariant() switch
                {
                    "winui" => Win32CompositionMode.WinUIComposition,
                    "dcomp" => Win32CompositionMode.DirectComposition,
                    _ => Win32CompositionMode.RedirectionSurface,
                };
                opts.CompositionMode = new[] { mode };
                AppLog.Write($"Forced composition mode: {mode}");
            }

            if (Environment.GetEnvironmentVariable("PETFAM_UI_RENDER") == "1")
            {
                opts.ShouldRenderOnUIThread = true;
                AppLog.Write("Rendering on UI thread");
            }

            builder = builder.With(opts);
        }

        return builder;
    }
}
