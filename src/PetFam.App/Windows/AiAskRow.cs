using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using PetFam.Core;

namespace PetFam.Desktop.Windows;

/// <summary>
/// "Tell the AI what you want" row for the editors: a text box, a button, optionally "from a photo", and a status
/// line. The job gets the words (or null) and a photo path (or null) and returns what it did, or null if it failed.
/// </summary>
public sealed class AiAskRow : StackPanel
{
    private static readonly IBrush Muted = new SolidColorBrush(Color.FromRgb(0x70, 0x7A, 0x8A));
    private static readonly IBrush Bad = new SolidColorBrush(Color.FromRgb(0xB0, 0x4A, 0x3A));

    private readonly TextBox _words;
    private readonly Button _go;
    private readonly Button? _photo;
    private readonly TextBlock _status = new() { FontSize = 12, Foreground = Muted, TextWrapping = TextWrapping.Wrap };
    private readonly Func<string?, string?, Task<string?>> _job;

    public AiAskRow(string watermark, string button, Func<string?, string?, Task<string?>> job, bool photo = false)
    {
        _job = job;
        Spacing = 4;
        _words = new TextBox { Watermark = watermark, MaxLength = 200, MinWidth = 220 };
        _go = new Button { Content = "✨ " + button, Padding = new Thickness(12, 5), Background = new SolidColorBrush(Color.FromRgb(0xEE, 0xE6, 0xFF)) };
        _go.Click += async (_, _) => await Run(_words.Text, null);
        _words.KeyDown += async (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            await Run(_words.Text, null);
        };
        var row = new DockPanel();
        DockPanel.SetDock(_go, Dock.Right);
        _go.Margin = new Thickness(6, 0, 0, 0);
        row.Children.Add(_go);
        if (photo)
        {
            _photo = new Button { Content = L.S("📷 From a photo", "📷 사진으로"), Padding = new Thickness(10, 5), Margin = new Thickness(6, 0, 0, 0) };
            _photo.Click += async (_, _) => await PickPhoto();
            DockPanel.SetDock(_photo, Dock.Right);
            row.Children.Add(_photo);
        }
        row.Children.Add(_words);
        Children.Add(row);
        Children.Add(_status);
    }

    private async Task PickPhoto()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L.S("A photo of your pet", "우리 아이 사진"),
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType(L.S("Pictures", "사진")) { Patterns = new[] { "*.jpg", "*.jpeg", "*.png", "*.webp", "*.gif" } } },
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        await Run(string.IsNullOrWhiteSpace(_words.Text) ? null : _words.Text, path);
    }

    private async Task Run(string? words, string? photo)
    {
        words = string.IsNullOrWhiteSpace(words) ? null : words.Trim();
        if (words is null && photo is null) return;
        _go.IsEnabled = false;
        if (_photo is not null) _photo.IsEnabled = false;
        _status.Foreground = Muted;
        _status.Text = L.S("Asking your AI…", "AI에게 물어보는 중…");
        var result = await _job(words, photo);
        _status.Foreground = result is null ? Bad : Muted;
        _status.Text = result ?? L.S("The AI didn't come up with anything. Try other words?", "AI가 답을 못 했어요. 다른 말로 해 볼까요?");
        _go.IsEnabled = true;
        if (_photo is not null) _photo.IsEnabled = true;
    }
}
