using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ZooZoo.Core.Interactions;
using ZooZoo.Core.Pets;

namespace ZooZoo.Desktop.Windows;

/// <summary>
/// Sims-style pie menu: interaction buttons arranged in a ring around the pet's head. Locked items are
/// greyed out with the reason underneath. Picking one queues it; clicking outside or Esc closes the menu.
/// </summary>
public sealed class PieMenuWindow : Window
{
    private const double W = 480;
    private const double H = 400;
    private const double RadiusX = 170;
    private const double RadiusY = 130;

    public static readonly FontFamily UiFont = new("Malgun Gothic, Segoe UI, Segoe UI Emoji");
    public static readonly FontFamily EmojiFont = new("Segoe UI Emoji, Malgun Gothic");
    public static readonly IBrush SimsBlue = new SolidColorBrush(Color.FromRgb(0x2F, 0x6F, 0xD6));

    private readonly Action<Interaction> _onPick;
    private bool _closing;

    public PieMenuWindow(Pet pet, IReadOnlyList<(Interaction Interaction, Availability Availability)> items,
        PixelPoint centerPx, double scaling, Action<Interaction> onPick)
    {
        _onPick = onPick;

        SystemDecorations = SystemDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        Topmost = true;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = W;
        Height = H;
        Position = new PixelPoint((int)(centerPx.X - W * scaling / 2), (int)(centerPx.Y - H * scaling / 2));

        var canvas = new Canvas { Width = W, Height = H, Background = Brushes.Transparent };
        canvas.PointerPressed += (_, e) =>
        {
            if (ReferenceEquals(e.Source, canvas)) SafeClose();
        };

        // The centre stays empty so the pet itself is visible (Sims style). Name tag + close sit below its feet.
        var nameTag = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 3, 8, 3),
            Background = new SolidColorBrush(Color.FromArgb(0xEE, 0x24, 0x2C, 0x3A)),
            BorderBrush = SimsBlue,
            BorderThickness = new Thickness(1.5),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = pet.Name, Foreground = Brushes.White, FontFamily = UiFont, FontSize = 13, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center },
                    new TextBlock { Text = "✕", Foreground = new SolidColorBrush(Color.FromRgb(0xB8, 0xC4, 0xD6)), FontFamily = UiFont, FontSize = 12, VerticalAlignment = VerticalAlignment.Center },
                },
            },
        };
        ToolTip.SetTip(nameTag, "닫기");
        nameTag.PointerPressed += (_, e) => { e.Handled = true; SafeClose(); };
        Place(canvas, nameTag, W / 2, H / 2 + 78);

        var n = items.Count;
        for (var i = 0; i < n; i++)
        {
            var (interaction, availability) = items[i];
            var angle = (-90 + i * 360.0 / n) * Math.PI / 180;
            var button = BuildItem(interaction, availability);
            Place(canvas, button, W / 2 + Math.Cos(angle) * RadiusX, H / 2 + Math.Sin(angle) * RadiusY);
        }

        Content = canvas;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) SafeClose(); };
        Deactivated += (_, _) => SafeClose();
        Opened += (_, _) => Activate();
    }

    private Border BuildItem(Interaction interaction, Availability availability)
    {
        var enabled = availability.Enabled;
        var normalBg = new SolidColorBrush(enabled ? Color.FromArgb(0xF4, 0xF7, 0xFA, 0xFF) : Color.FromArgb(0xE8, 0xE4, 0xE6, 0xEA));
        var hoverBg = new SolidColorBrush(Color.FromArgb(0xFA, 0xD8, 0xE8, 0xFF));
        var textColor = new SolidColorBrush(enabled ? Color.FromRgb(0x1C, 0x2A, 0x40) : Color.FromRgb(0x8A, 0x90, 0x9A));

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = interaction.Label, FontFamily = UiFont, FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = textColor });
        if (!enabled && availability.Reason is { } reason)
            text.Children.Add(new TextBlock { Text = reason, FontFamily = UiFont, FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0x5A, 0x50)) });

        var border = new Border
        {
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(12, 7, 14, 7),
            Background = normalBg,
            BorderBrush = enabled ? SimsBlue : new SolidColorBrush(Color.FromRgb(0xB8, 0xBE, 0xC8)),
            BorderThickness = new Thickness(2),
            BoxShadow = BoxShadows.Parse("0 3 8 0 #50000000"),
            Cursor = new Cursor(enabled ? StandardCursorType.Hand : StandardCursorType.No),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = interaction.Icon, FontFamily = EmojiFont, FontSize = 18, VerticalAlignment = VerticalAlignment.Center, Opacity = enabled ? 1 : 0.45 },
                    text,
                },
            },
        };

        if (enabled)
        {
            border.PointerEntered += (_, _) => border.Background = hoverBg;
            border.PointerExited += (_, _) => border.Background = normalBg;
            border.PointerPressed += (_, e) =>
            {
                e.Handled = true;
                _onPick(interaction);
                SafeClose();
            };
        }
        else
        {
            border.PointerPressed += (_, e) => e.Handled = true;
        }
        return border;
    }

    private static void Place(Canvas canvas, Control control, double cx, double cy)
    {
        control.Measure(Size.Infinity);
        var size = control.DesiredSize;
        Canvas.SetLeft(control, Math.Clamp(cx - size.Width / 2, 2, W - size.Width - 2));
        Canvas.SetTop(control, Math.Clamp(cy - size.Height / 2, 2, H - size.Height - 2));
        canvas.Children.Add(control);
    }

    private void SafeClose()
    {
        if (_closing) return;
        _closing = true;
        Close();
    }
}
