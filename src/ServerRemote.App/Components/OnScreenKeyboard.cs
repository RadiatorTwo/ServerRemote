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

    private bool _shift;
    private Border? _shiftBorder, _ctrlBorder, _altBorder;
    private readonly List<(Label label, string lower, string upper)> _dual = new();
    // All keys with their base width — allows orientation-dependent scaling without a rebuild.
    private readonly List<(Border border, Label label, double baseWidth)> _keys = new();

    public OnScreenKeyboard()
    {
        BackgroundColor = Colors.Transparent;
        Padding = new Thickness(6, 4);
        Build();

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

    // In landscape, height is scarce → flatter, slightly narrower keys and smaller font,
    // so that the upper part of the image stays visible.
    private void UpdateMetrics()
    {
        var info = DeviceDisplay.Current.MainDisplayInfo;
        bool landscape = info.Orientation == DisplayOrientation.Landscape
            || (info.Orientation == DisplayOrientation.Unknown && info.Width > info.Height);

        double scale = landscape ? 0.86 : 1.0;
        double height = landscape ? 30 : KeyH;
        double font = landscape ? 13 : 16;

        foreach (var (border, label, baseWidth) in _keys)
        {
            border.WidthRequest = baseWidth * scale;
            border.HeightRequest = height;
            label.FontSize = font;
        }
    }

    private void Build()
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
            CtrlKey(), AltKey(),
            Special("Tab", WideW, () => SendChar('\t')),
            Special("Space", 240, () => SendChar(' ')),
            Special("Esc", 56, () => RunCommand(ViewModel?.KeyEscapeCommand)),
            Special("←", 48, () => RunCommand(ViewModel?.KeyLeftCommand)),
            Special("↑", 48, () => RunCommand(ViewModel?.KeyUpCommand)),
            Special("↓", 48, () => RunCommand(ViewModel?.KeyDownCommand)),
            Special("→", 48, () => RunCommand(ViewModel?.KeyRightCommand))));

        Content = rows;
    }

    private static HorizontalStackLayout Row(params View[] keys)
    {
        var row = new HorizontalStackLayout { Spacing = 4, HorizontalOptions = LayoutOptions.Center };
        foreach (var k in keys)
            row.Add(k);
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

    // Character key with lowercase/uppercase variant (depending on Shift).
    private View Dual(string lower, string upper)
    {
        var (border, label) = MakeKey(_shift ? upper : lower, KeyW);
        _dual.Add((label, lower, upper));
        OnTap(border, async () =>
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
        OnTap(border, async () =>
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

    // Shift/Ctrl/Alt each apply only to the next key — reset after sending.
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
        }
        SetActive(_ctrlBorder, false);
        SetActive(_altBorder, false);
    }
}
