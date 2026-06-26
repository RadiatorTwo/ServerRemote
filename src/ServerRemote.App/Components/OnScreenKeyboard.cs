using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Devices;
using ServerRemote.App.ViewModels;

namespace ServerRemote.App.Components;

/// <summary>
/// Semi-transparent on-screen keyboard as an overlay over the live image (fullscreen). Sends characters
/// and special keys to the NanoKVM host via the <see cref="NanoKvmViewModel"/> — entirely without the
/// Android soft keyboard, which would cover the image. The keys are translucent so the live image
/// behind them stays visible.
///
/// Deliberately built from <see cref="Border"/>+<see cref="Label"/> instead of <see cref="Button"/>: Android
/// buttons automatically uppercase the text (destroying lowercase) and enforce a minimum width.
/// The layout shows QWERTZ; the actual HID mapping is handled by
/// <see cref="NanoKvmViewModel.SendCharAsync"/> based on the configured host layout.
/// </summary>
public sealed class OnScreenKeyboard : ContentView
{
    private static readonly Color KeyBg = Color.FromRgba(255, 255, 255, 38);
    private static readonly Color KeyBgActive = Color.FromRgba(70, 150, 255, 190);
    private const double KeyW = 46;
    private const double WideW = 64;
    private const double KeyH = 46;

    public static readonly BindableProperty ViewModelProperty = BindableProperty.Create(
        nameof(ViewModel), typeof(NanoKvmViewModel), typeof(OnScreenKeyboard));

    /// <summary>Target ViewModel through which keys are sent.</summary>
    public NanoKvmViewModel? ViewModel
    {
        get => (NanoKvmViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    private const double RowSpacing = 4;

    private bool _shift;
    private Border? _shiftBorder, _ctrlBorder, _altBorder, _metaBorder;
    private readonly List<(Label label, string lower, string upper)> _dual = new();
    // All keys with their base width — allows orientation-dependent scaling without a rebuild.
    private readonly List<(Border border, Label label, double baseWidth)> _keys = new();
    // Per-row natural geometry, used to scale the keyboard down so it never overflows the screen width.
    private readonly List<(double scalableWidth, int keyCount)> _rows = new();

    // Which orientation the currently built layout was created for (null = nothing built yet).
    private bool? _layoutIsLandscape;

    public OnScreenKeyboard()
    {
        BackgroundColor = Colors.Transparent;
        Padding = new Thickness(6, 4);
        Rebuild(IsLandscape(DeviceDisplay.Current.MainDisplayInfo));

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        UpdateMetrics();
        DeviceDisplay.Current.MainDisplayInfoChanged += OnDisplayInfoChanged;
    }

    private void OnUnloaded(object? sender, EventArgs e)
        => DeviceDisplay.Current.MainDisplayInfoChanged -= OnDisplayInfoChanged;

    private void OnDisplayInfoChanged(object? sender, DisplayInfoChangedEventArgs e) => UpdateMetrics();

    private static bool IsLandscape(DisplayInfo info)
        => info.Orientation == DisplayOrientation.Landscape
           || (info.Orientation == DisplayOrientation.Unknown && info.Width > info.Height);

    // Landscape and portrait use genuinely different key arrangements (see BuildLandscape/BuildPortrait),
    // so when the orientation category flips we rebuild the layout rather than just rescaling it.
    private void UpdateMetrics()
    {
        var info = DeviceDisplay.Current.MainDisplayInfo;
        bool landscape = IsLandscape(info);

        if (_layoutIsLandscape != landscape)
            Rebuild(landscape);

        // Available width in device-independent units (MAUI layout space), minus the
        // ContentView padding (6 left + 6 right) and a small safety margin.
        double available = (info.Density > 0 ? info.Width / info.Density : info.Width) - 12 - 4;

        // Largest scale at which the widest row still fits the available width.
        double fitScale = double.PositiveInfinity;
        foreach (var (scalableWidth, keyCount) in _rows)
        {
            if (scalableWidth <= 0) continue;
            double spacing = (keyCount - 1) * RowSpacing;
            fitScale = Math.Min(fitScale, (available - spacing) / scalableWidth);
        }

        // Landscape keeps the compact look (slightly narrower keys, flatter, smaller font);
        // portrait keeps full-size, comfortable keys and only narrows the width if it must fit.
        double desiredScale = landscape ? 0.86 : 1.0;
        double scale = Math.Min(desiredScale, fitScale);
        if (scale <= 0 || double.IsInfinity(scale)) scale = desiredScale;

        // Height and font keep the key aspect ratio sensible. In landscape we deliberately
        // flatten; in portrait we keep a comfortable, fixed key height regardless of the
        // width scale so the keys never become tiny.
        double height = landscape ? 30 : 40;
        double font = landscape ? 13 : 14;

        foreach (var (border, label, baseWidth) in _keys)
        {
            border.WidthRequest = baseWidth * scale;
            border.HeightRequest = height;
            label.FontSize = font;
        }
    }

    private void Rebuild(bool landscape)
    {
        _keys.Clear();
        _rows.Clear();
        _dual.Clear();
        _shiftBorder = _ctrlBorder = _altBorder = _metaBorder = null;

        Content = landscape ? BuildLandscape() : BuildPortrait();
        _layoutIsLandscape = landscape;

        // Restore the persisted modifier state onto the freshly built keys.
        RefreshShiftLabels();
        SetActive(_shiftBorder, _shift);
        if (ViewModel is { } vm)
        {
            SetActive(_ctrlBorder, vm.CtrlActive);
            SetActive(_altBorder, vm.AltActive);
            SetActive(_metaBorder, vm.MetaActive);
        }
    }

    // Wide landscape layout: function and arrow keys share the bottom row, which fits the broad screen.
    private View BuildLandscape()
    {
        var rows = new VerticalStackLayout { Spacing = 4, HorizontalOptions = LayoutOptions.Center };

        rows.Add(Row(
            Dual("1", "!"), Dual("2", "\""), Dual("3", "§"), Dual("4", "$"), Dual("5", "%"),
            Dual("6", "&"), Dual("7", "/"), Dual("8", "("), Dual("9", ")"), Dual("0", "="),
            Special("⌫", WideW, Backspace)));

        rows.Add(Row(
            Dual("q", "Q"), Dual("w", "W"), Dual("e", "E"), Dual("r", "R"), Dual("t", "T"), Dual("z", "Z"),
            Dual("u", "U"), Dual("i", "I"), Dual("o", "O"), Dual("p", "P"), Dual("ü", "Ü")));

        rows.Add(Row(
            Dual("a", "A"), Dual("s", "S"), Dual("d", "D"), Dual("f", "F"), Dual("g", "G"), Dual("h", "H"),
            Dual("j", "J"), Dual("k", "K"), Dual("l", "L"), Dual("ö", "Ö"), Dual("ä", "Ä")));

        rows.Add(Row(
            ShiftKey(),
            Dual("y", "Y"), Dual("x", "X"), Dual("c", "C"), Dual("v", "V"), Dual("b", "B"), Dual("n", "N"), Dual("m", "M"),
            Dual(",", ";"), Dual(".", ":"), Dual("-", "_"),
            Special("↵", WideW, Enter)));

        rows.Add(Row(
            WinKey(), CtrlKey(), AltKey(),
            Special("Tab", WideW, () => SendChar('\t')),
            Special("Space", 220, () => SendChar(' ')),
            Special("Esc", 56, () => RunCommand(ViewModel?.KeyEscapeCommand)),
            Special("←", 48, () => RunCommand(ViewModel?.KeyLeftCommand)),
            Special("↑", 48, () => RunCommand(ViewModel?.KeyUpCommand)),
            Special("↓", 48, () => RunCommand(ViewModel?.KeyDownCommand)),
            Special("→", 48, () => RunCommand(ViewModel?.KeyRightCommand))));

        return rows;
    }

    // Portrait layout: the wide function/arrow row is split across two extra rows so the widest
    // row is just the 11-key letter rows. That keeps the fit-scale high (~0.7) and the keys large.
    private View BuildPortrait()
    {
        var rows = new VerticalStackLayout { Spacing = 4, HorizontalOptions = LayoutOptions.Center };

        rows.Add(Row(
            Dual("1", "!"), Dual("2", "\""), Dual("3", "§"), Dual("4", "$"), Dual("5", "%"),
            Dual("6", "&"), Dual("7", "/"), Dual("8", "("), Dual("9", ")"), Dual("0", "="),
            Special("⌫", WideW, Backspace)));

        rows.Add(Row(
            Dual("q", "Q"), Dual("w", "W"), Dual("e", "E"), Dual("r", "R"), Dual("t", "T"), Dual("z", "Z"),
            Dual("u", "U"), Dual("i", "I"), Dual("o", "O"), Dual("p", "P"), Dual("ü", "Ü")));

        rows.Add(Row(
            Dual("a", "A"), Dual("s", "S"), Dual("d", "D"), Dual("f", "F"), Dual("g", "G"), Dual("h", "H"),
            Dual("j", "J"), Dual("k", "K"), Dual("l", "L"), Dual("ö", "Ö"), Dual("ä", "Ä")));

        rows.Add(Row(
            ShiftKey(),
            Dual("y", "Y"), Dual("x", "X"), Dual("c", "C"), Dual("v", "V"), Dual("b", "B"), Dual("n", "N"), Dual("m", "M"),
            Dual(",", ";"), Dual(".", ":"), Dual("-", "_")));

        // Modifier / whitespace row.
        rows.Add(Row(
            WinKey(), CtrlKey(), AltKey(),
            Special("Tab", KeyW, () => SendChar('\t')),
            Special("Space", 120, () => SendChar(' ')),
            Special("↵", WideW, Enter)));

        // Navigation row.
        rows.Add(Row(
            Special("Esc", WideW, () => RunCommand(ViewModel?.KeyEscapeCommand)),
            Special("←", KeyW, () => RunCommand(ViewModel?.KeyLeftCommand)),
            Special("↑", KeyW, () => RunCommand(ViewModel?.KeyUpCommand)),
            Special("↓", KeyW, () => RunCommand(ViewModel?.KeyDownCommand)),
            Special("→", KeyW, () => RunCommand(ViewModel?.KeyRightCommand))));

        return rows;
    }

    private HorizontalStackLayout Row(params View[] keys)
    {
        var row = new HorizontalStackLayout { Spacing = RowSpacing, HorizontalOptions = LayoutOptions.Center };
        double scalableWidth = 0;
        foreach (var k in keys)
        {
            row.Add(k);
            // At build time each Border's WidthRequest equals its base width.
            if (k is Border b)
                scalableWidth += b.WidthRequest;
        }
        _rows.Add((scalableWidth, keys.Length));
        return row;
    }

    // ----- Key factory -----

    private (Border border, Label label) MakeKey(string text, double width)
    {
        var label = new Label
        {
            Text = text,
            TextColor = Colors.White,
            FontSize = 16,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };
        var border = new Border
        {
            WidthRequest = width,
            HeightRequest = KeyH,
            BackgroundColor = KeyBg,
            Stroke = Colors.Transparent,
            StrokeShape = new RoundRectangle { CornerRadius = 6 },
            Padding = 0,
            Content = label
        };
        _keys.Add((border, label, width));
        return (border, label);
    }

    private static void OnTap(Border b, Func<Task> action)
    {
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await action();
        b.GestureRecognizers.Add(tap);
    }

    // Like OnTap, but briefly highlights the key so a press is visibly acknowledged.
    // Used for character/special keys; modifier keys already signal via their persistent active state.
    private static void OnTapFlash(Border b, Func<Task> action)
    {
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) =>
        {
            b.BackgroundColor = KeyBgActive;
            try
            {
                await action();
            }
            finally
            {
                await Task.Delay(90);
                b.BackgroundColor = KeyBg;
            }
        };
        b.GestureRecognizers.Add(tap);
    }

    // Character key with lowercase/uppercase variant (depending on Shift).
    private View Dual(string lower, string upper)
    {
        var (border, label) = MakeKey(_shift ? upper : lower, KeyW);
        _dual.Add((label, lower, upper));
        OnTapFlash(border, async () =>
        {
            var s = _shift ? upper : lower;
            if (s.Length > 0)
                await SendChar(s[0]);
            ResetOneShot();
        });
        return border;
    }

    // Special key (sends something; afterwards resets the one-shot modifiers).
    private View Special(string label, double width, Func<Task> action)
    {
        var (border, _) = MakeKey(label, width);
        OnTapFlash(border, async () =>
        {
            await action();
            ResetOneShot();
        });
        return border;
    }

    private View ShiftKey()
    {
        var (border, _) = MakeKey("⇧", WideW);
        _shiftBorder = border;
        OnTap(border, () =>
        {
            _shift = !_shift;
            RefreshShiftLabels();
            SetActive(_shiftBorder, _shift);
            return Task.CompletedTask;
        });
        return border;
    }

    private View CtrlKey()
    {
        var (border, _) = MakeKey("Ctrl", WideW);
        _ctrlBorder = border;
        OnTap(border, () =>
        {
            if (ViewModel is { } vm)
            {
                vm.CtrlActive = !vm.CtrlActive;
                SetActive(_ctrlBorder, vm.CtrlActive);
            }
            return Task.CompletedTask;
        });
        return border;
    }

    private View AltKey()
    {
        var (border, _) = MakeKey("Alt", WideW);
        _altBorder = border;
        OnTap(border, () =>
        {
            if (ViewModel is { } vm)
            {
                vm.AltActive = !vm.AltActive;
                SetActive(_altBorder, vm.AltActive);
            }
            return Task.CompletedTask;
        });
        return border;
    }

    // Windows key: first tap arms it as a one-shot modifier (for combos like Win+R);
    // a second tap (while armed) fires the Windows key on its own (e.g. open the Start menu).
    private View WinKey()
    {
        var (border, _) = MakeKey("⊞", WideW);
        _metaBorder = border;
        OnTap(border, async () =>
        {
            if (ViewModel is not { } vm)
                return;

            if (vm.MetaActive)
            {
                // Second tap: send Win alone and disarm.
                vm.MetaActive = false;
                SetActive(_metaBorder, false);
                await vm.SendWindowsKeyAsync();
            }
            else
            {
                vm.MetaActive = true;
                SetActive(_metaBorder, true);
            }
        });
        return border;
    }

    // ----- Sending / state -----

    private Task SendChar(char c) => ViewModel?.SendCharAsync(c) ?? Task.CompletedTask;
    private Task Enter() => ViewModel?.SendEnterAsync() ?? Task.CompletedTask;
    private Task Backspace() => ViewModel?.SendBackspaceAsync() ?? Task.CompletedTask;

    private static Task RunCommand(System.Windows.Input.ICommand? command)
    {
        if (command?.CanExecute(null) == true)
            command.Execute(null);
        return Task.CompletedTask;
    }

    private void RefreshShiftLabels()
    {
        foreach (var (label, lower, upper) in _dual)
            label.Text = _shift ? upper : lower;
    }

    private static void SetActive(Border? border, bool active)
    {
        if (border is not null)
            border.BackgroundColor = active ? KeyBgActive : KeyBg;
    }

    // Shift/Ctrl/Alt/Win each apply only to the next key — reset after sending.
    private void ResetOneShot()
    {
        if (_shift)
        {
            _shift = false;
            RefreshShiftLabels();
            SetActive(_shiftBorder, false);
        }

        if (ViewModel is { } vm)
        {
            if (vm.CtrlActive) vm.CtrlActive = false;
            if (vm.AltActive) vm.AltActive = false;
            if (vm.MetaActive) vm.MetaActive = false;
        }
        SetActive(_ctrlBorder, false);
        SetActive(_altBorder, false);
        SetActive(_metaBorder, false);
    }
}
