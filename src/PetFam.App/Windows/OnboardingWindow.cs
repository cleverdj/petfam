using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using PetFam.Core;
using PetFam.Core.Geometry;
using PetFam.Core.Pets;
using PetFam.Desktop.Rendering;

namespace PetFam.Desktop.Windows;

/// <summary>
/// Adoption: pick dogs or cats, then a breed (each drawn live with the desktop renderer), read who it is (its
/// persona and temperament), name it and adopt it as a baby. Shown on first run (or after the last pet left) and
/// from the tray ("Add a pet").
/// </summary>
public sealed class OnboardingWindow : Window
{
    private static readonly IBrush Ink = new SolidColorBrush(Color.FromRgb(0x1C, 0x2A, 0x40));
    private static readonly IBrush Muted = new SolidColorBrush(Color.FromRgb(0x6A, 0x74, 0x84));
    private static readonly IBrush Line = new SolidColorBrush(Color.FromRgb(0xE3, 0xE8, 0xF0));
    private static readonly IBrush Accent = PieMenuWindow.SimsBlue;

    private readonly List<(Breed Breed, Border Card, PetPreview View, Pet Pet)> _cards = new();
    private readonly WrapPanel _grid = new() { HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Dictionary<PetSpecies, ToggleButton> _tabs = new();
    private readonly PetPreview _bigView;
    private Pet _bigPet;
    private readonly TextBlock _breedName = new() { FontSize = 20, FontWeight = FontWeight.Bold, Foreground = Ink };
    private readonly TextBlock _tagline = new() { FontSize = 13, Foreground = Accent, FontWeight = FontWeight.SemiBold };
    private readonly TextBlock _persona = new() { FontSize = 13, Foreground = Ink, TextWrapping = TextWrapping.Wrap, LineHeight = 20 };
    private readonly StackPanel _traits = new() { Spacing = 3 };
    private readonly TextBox _name;
    private readonly Button _adopt;
    private readonly DispatcherTimer _timer;
    private Breed? _chosen;
    private bool _nameEdited;
    private bool _done;

    /// <param name="firstRun">Welcome text for the very first pet; otherwise "Welcome a new family member".</param>
    /// <param name="species">Which tab to open on.</param>
    public OnboardingWindow(Action<Breed, string> onAdopt, bool firstRun = true, PetSpecies species = PetSpecies.Dog)
    {
        Title = firstRun ? L.S("PetFam — Adopt your first friend", "PetFam — 첫 친구 데려오기") : L.S("PetFam — Welcome a new family member", "PetFam — 새 가족 맞이하기");
        Width = 900;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = PieMenuWindow.UiFont;
        Topmost = true;
        Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromRgb(0xFF, 0xF7, 0xEC), 0), new GradientStop(Color.FromRgb(0xEE, 0xF5, 0xFF), 1) },
        };

        var title = new TextBlock
        {
            Text = firstRun ? L.S("Welcome to PetFam!", "PetFam에 오신 걸 환영해요!") : L.S("Welcome a new family member", "새 가족을 맞이해요"),
            FontSize = 24, FontWeight = FontWeight.Bold, Foreground = Ink, HorizontalAlignment = HorizontalAlignment.Center,
        };
        var subtitle = new TextBlock
        {
            Text = L.S("Choose a friend to live on your desktop. Every breed has its own personality, way of talking and favourite things.\nThey arrive as babies and grow up with you.", "바탕화면에서 함께 지낼 친구를 골라 주세요. 품종마다 성격과 말투, 좋아하는 것이 달라요.\n아기 때 데려와서 함께 키워요."),
            FontSize = 14, Foreground = Muted, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
        };

        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var (sp, label) in new[] { (PetSpecies.Dog, L.S("🐶 Dogs", "🐶 강아지")), (PetSpecies.Cat, L.S("🐱 Cats", "🐱 고양이")) })
        {
            var tab = new ToggleButton
            {
                Content = label, FontSize = 15, FontWeight = FontWeight.Bold, Padding = new Thickness(22, 8), CornerRadius = new CornerRadius(20),
            };
            tab.Click += (_, _) => ShowSpecies(sp);
            _tabs[sp] = tab;
            tabs.Children.Add(tab);
        }

        // The chosen breed, big, with who it is.
        _bigPet = new Pet(species, "", DateTime.UtcNow, new Random(5));
        _bigView = new PetPreview(_bigPet) { Width = 250, Height = 210 };
        _name = new TextBox { Watermark = L.S("Give it a name", "이름을 지어 주세요"), MaxLength = 12, FontSize = 16, Width = 200 };
        _name.TextChanged += (_, _) => { _nameEdited = _chosen is not null && (_name.Text ?? "") != Suggest(_chosen); Update(); };
        _adopt = new Button
        {
            Content = L.S("Adopt", "입양하기"),
            FontSize = 16, FontWeight = FontWeight.Bold,
            Padding = new Thickness(28, 10),
            CornerRadius = new CornerRadius(12),
            Background = Accent, Foreground = Brushes.White,
            IsEnabled = false,
        };
        _adopt.Click += (_, _) =>
        {
            if (_chosen is not { } breed) return;
            var name = string.IsNullOrWhiteSpace(_name.Text) ? Suggest(breed) : _name.Text!.Trim();
            _done = true;
            onAdopt(breed, name);
            Close();
        };

        var detailText = new StackPanel
        {
            Spacing = 6, Width = 520, VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { _breedName, new Border { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 3), Child = _tagline } } },
                _persona,
                _traits,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 8, 0, 0),
                    Children = { new TextBlock { Text = L.S("Name", "이름"), FontSize = 14, Foreground = Ink, VerticalAlignment = VerticalAlignment.Center }, _name, _adopt },
                },
            },
        };
        var detail = new Border
        {
            Background = Brushes.White, CornerRadius = new CornerRadius(18), Padding = new Thickness(16), BorderBrush = Line, BorderThickness = new Thickness(1),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20, Children = { _bigView, detailText } },
        };

        var later = new TextBlock
        {
            Text = L.S("You can bring more friends later with \"Add a pet\" in the tray menu (up to 6).", "나중에 트레이 메뉴의 \"펫 추가하기\"로 친구를 더 데려올 수 있어요 (최대 6마리)."),
            FontSize = 12, Foreground = Muted, HorizontalAlignment = HorizontalAlignment.Center,
        };

        Content = new StackPanel
        {
            Margin = new Thickness(24, 20),
            Spacing = 12,
            Children = { title, subtitle, tabs, _grid, detail, later },
        };

        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(40), DispatcherPriority.Render, (_, _) => Tick());
        _timer.Start();
        Closed += (_, _) => _timer.Stop();
        // Closing without adopting is fine: the window comes back next time there are no pets.
        Closing += (_, _) => { if (!_done) AppLog.Write("Adoption window closed without adopting"); };

        ShowSpecies(species);
    }

    private static string Suggest(Breed b) => b.Names[0];

    private void ShowSpecies(PetSpecies species)
    {
        foreach (var (sp, tab) in _tabs)
        {
            tab.IsChecked = sp == species;
            tab.Background = sp == species ? Accent : Brushes.White;
            tab.Foreground = sp == species ? Brushes.White : Ink;
        }
        _grid.Children.Clear();
        _cards.Clear();
        foreach (var breed in Breed.Of(species)) _grid.Children.Add(Card(breed));
        Choose(Breed.Of(species).First(), greet: true);
    }

    private Border Card(Breed breed)
    {
        var pet = new Pet(breed.Species, "", DateTime.UtcNow, new Random(breed.Id.Length), breed) { Heading = 35f, TargetHeading = 35f };
        pet.Growth = new Growth(DateTime.UtcNow, 8);   // a baby, as it will arrive
        pet.SetAnimation(breed.Species == PetSpecies.Cat ? "sit" : "idle");
        pet.AnimationTime = breed.Id.Length * 0.37f;
        var view = new PetPreview(pet) { Width = 98, Height = 84 };
        var card = new Border
        {
            Width = 112,
            Margin = new Thickness(3),
            Padding = new Thickness(6),
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(3),
            Background = Brushes.White,
            Cursor = new Cursor(StandardCursorType.Hand),
            BoxShadow = BoxShadows.Parse("0 3 10 0 #18000000"),
            Child = new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    view,
                    new TextBlock { Text = breed.Name, FontSize = 13, FontWeight = FontWeight.Bold, Foreground = Ink, HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center },
                    new TextBlock { Text = breed.Tagline, FontSize = 10.5, Foreground = Muted, HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center },
                },
            },
        };
        card.PointerPressed += (_, e) => { Choose(breed, greet: true); e.Handled = true; };
        _cards.Add((breed, card, view, pet));
        return card;
    }

    private void Choose(Breed breed, bool greet)
    {
        var previous = _chosen;
        _chosen = breed;
        if (!_nameEdited || previous is null) { _name.Text = Suggest(breed); _nameEdited = false; }

        _breedName.Text = breed.Name;
        _tagline.Text = breed.Tagline;
        _persona.Text = breed.Persona;
        _traits.Children.Clear();
        _traits.Children.Add(Trait(L.S("Energy", "활동량"), breed.Activity));
        _traits.Children.Add(Trait(L.S("Playfulness", "장난기"), breed.Playfulness));
        _traits.Children.Add(Trait(L.S("Shyness", "낯가림"), breed.Shyness));
        _traits.Children.Add(Trait(L.S("Homebody", "집순이"), breed.Homebody));
        _traits.Children.Add(Trait(L.S("Warms up", "친해지는 속도"), Math.Clamp((breed.BondRate - 0.6f) / 0.8f, 0f, 1f)));
        _traits.Children.Add(new TextBlock
        {
            Text = L.S($"Sounds like: \"{breed.Voice.Idle[0]}\"  ·  {Habits(breed)}", $"이렇게 말해요: \"{breed.Voice.Idle[0]}\"  ·  {Habits(breed)}"),
            FontSize = 12, Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0),
        });

        // The big preview shows the chosen breed as the baby you will get; it greets you.
        _bigPet = new Pet(breed.Species, "", DateTime.UtcNow, new Random(7), breed) { Heading = 50f, TargetHeading = 50f };
        _bigPet.Growth = new Growth(DateTime.UtcNow, 8);
        _bigPet.SetAnimation(greet ? (breed.Species == PetSpecies.Dog ? "playbow" : "petted") : "idle");
        if (greet) _bigPet.Say(breed.Voice.Greeting[0], 4f);
        _bigView.Pet = _bigPet;
        Update();
    }

    /// <summary>A few habits that follow from the breed's numbers, in words.</summary>
    private static string Habits(Breed b)
    {
        var list = new List<string>();
        if (b.Appetite >= 1.2f) list.Add(L.S("really loves food", "먹는 걸 정말 좋아해요"));
        if (b.Sleepiness >= 0.1f) list.Add(L.S("sleeps a lot", "잠이 많아요"));
        if (b.Leap >= 1.25f) list.Add(L.S("a great jumper", "점프를 잘해요"));
        if (b.Leap <= 0.7f) list.Add(L.S("can't jump high", "높이 뛰진 못해요"));
        if (b.RainDislike >= 0.3f) list.Add(L.S("hates getting rained on", "비 맞는 걸 싫어해요"));
        if (b.RainDislike < 0f) list.Add(L.S("shrugs off the rain", "웬만한 비는 끄떡없어요"));
        if (b.Species == PetSpecies.Dog && b.SnowLove >= 1.2f) list.Add(L.S("loves snowy days", "눈 오는 날을 좋아해요"));
        if (b.Species == PetSpecies.Dog && b.WaterLove >= 0.6f) list.Add(L.S("loves water", "물놀이를 좋아해요"));
        list.Add(b.DefaultTravel switch
        {
            TravelMode.Follow => L.S("follows you across desktops", "데스크톱을 옮기면 따라와요"),
            TravelMode.Free => L.S("roams where it likes", "마음 내키는 대로 돌아다녀요"),
            _ => L.S("stays home", "집을 지켜요"),
        });
        return string.Join(" · ", list);
    }

    private static Control Trait(string label, float value)
    {
        var dots = (int)Math.Round(Math.Clamp(value, 0f, 1f) * 5);
        return new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            Children =
            {
                new TextBlock { Text = label, Width = 92, FontSize = 12, Foreground = Muted },
                new TextBlock { Text = new string('●', dots) + new string('○', 5 - dots), FontSize = 12, Foreground = Accent },
            },
        };
    }

    private void Update()
    {
        foreach (var c in _cards)
            c.Card.BorderBrush = ReferenceEquals(c.Breed, _chosen) ? Accent : Line;
        _adopt.IsEnabled = _chosen is not null;
        var who = _chosen is { } b ? (string.IsNullOrWhiteSpace(_name.Text) ? Suggest(b) : _name.Text!.Trim()) : null;
        _adopt.Content = who is null ? L.S("Adopt", "입양하기") : L.S($"Adopt {who}", $"{who} 입양하기");
    }

    private void Tick()
    {
        foreach (var c in _cards)
        {
            c.Pet.AnimationTime += 0.04f;
            c.View.InvalidateVisual();
        }
        _bigPet.AnimationTime += 0.04f;
        _bigPet.TickBubble(0.04f);
        if (_bigPet.Bubble is null && _bigPet.Animation is "playbow" or "petted" && _bigPet.AnimationTime > 2.5f) _bigPet.SetAnimation("idle");
        _bigView.InvalidateVisual();
    }
}

/// <summary>Draws one pet (not part of the simulation) with the desktop renderer, centred in the control.</summary>
public sealed class PetPreview : Control
{
    public Pet Pet { get; set; }

    public PetPreview(Pet pet) => Pet = pet;

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var b = Bounds;
        context.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xF3, 0xF7, 0xEE)), null, new Rect(b.Size), 12, 12);
        context.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xDC, 0xEB, 0xD0)), null, new Point(b.Width / 2, b.Height * 0.86), b.Width * 0.36, b.Height * 0.07);
        var s = Math.Min(b.Width / PetSprite.BoxWidth, b.Height / PetSprite.BoxHeight) * 1.6;
        var ox = b.Width / 2 - PetSprite.FootX * s;
        var oy = b.Height * 0.86 - PetSprite.FootY * s;
        using (context.PushTransform(Matrix.CreateScale(s, s) * Matrix.CreateTranslation(ox, oy)))
        {
            PetSprite.Paint(context, Pet, 1.3f, inside: true);
            PetRenderer.DrawOverlays(context, Pet, new Vec2(PetSprite.FootX, PetSprite.FootY), bubble: false);
        }

        // The speech bubble at its normal size (not blown up with the pet), wrapped to fit the preview.
        if (Pet.Bubble is { } text)
        {
            var tipY = oy + (PetSprite.FootY - PetRenderer.HeadTopOffset(Pet) * 1.3) * s - 4;
            PetRenderer.DrawBubble(context, new Point(b.Width / 2, tipY), text, Pet.BubbleTimer,
                maxWidth: Math.Min(220, b.Width - 12), fontSize: 12.5, within: new Rect(b.Size));
        }
    }
}
