using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ServerRemote.App.Models;
using ServerRemote.App.Services;
using ServerRemote.App.Services.Hid;

namespace ServerRemote.App.ViewModels;

/// <summary>
/// ViewModel for the NanoKVM hub page. A thin wrapper around <see cref="NanoKvmMonitor"/>
/// (connection, LEDs, power/reset/reboot, device info, HDMI), the MJPEG live stream,
/// text paste/keyboard shortcuts, and the WebSocket live input (mouse/keyboard).
/// </summary>
public sealed partial class NanoKvmViewModel : ObservableObject
{
    /// <summary>Maximum paste length per the NanoKVM API.</summary>
    private const int MaxPasteLength = 1024;

    private readonly H264StreamService _stream;
    private readonly NanoKvmApiClient _api;
    private readonly ISettingsService _settings;

    public NanoKvmMonitor Monitor { get; }

    /// <summary>HID WebSocket service — bound directly to the <c>KvmInputSurface</c>.</summary>
    public NanoKvmHidService Hid { get; }

    /// <summary>H.264 frame source — bound directly to the native <c>KvmVideoView</c>.</summary>
    public H264StreamService Stream => _stream;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StreamButtonText))]
    private bool _isStreaming;
    [ObservableProperty] private bool _isConnecting;

    /// <summary>Label of the stream toggle button depending on state.</summary>
    public string StreamButtonText => IsStreaming ? "Stop stream" : "Start stream";
    [ObservableProperty] private string? _streamError;

    /// <summary>Aspect ratio (width/height) of the current video frame — 0 = not yet known.
    /// Drives the letterbox-correct mapping of mouse input (<see cref="Components.KvmInputSurface"/>).</summary>
    [ObservableProperty] private double _videoAspect;

    // Paste / Shortcuts (Phase 4)
    [ObservableProperty] private string _pasteText = "";
    [ObservableProperty] private string _pasteLanguage = "";
    [ObservableProperty] private string? _inputStatus;

    /// <summary>Visibility of the on-screen keyboard overlay in fullscreen.</summary>
    [ObservableProperty] private bool _keyboardVisible;

    // Live-Eingabe (Phase 5)
    [ObservableProperty] private bool _inputConnected;
    [ObservableProperty] private bool _ctrlActive;
    [ObservableProperty] private bool _shiftActive;
    [ObservableProperty] private bool _altActive;
    [ObservableProperty] private bool _metaActive;

    /// <summary>
    /// Touchpad mode: swiping moves the cursor relatively (instead of absolute tapping), tapping
    /// clicks, long press = right-click. Much more comfortable on touch devices.
    /// </summary>
    [ObservableProperty] private bool _relativeMouseMode = true;

    /// <summary>Host keyboard layout: on = German (QWERTZ), off = US. Determines the character→HID
    /// mapping of the live input and is persisted in the settings.</summary>
    [ObservableProperty] private bool _germanLayout;

    /// <summary>Selectable keyboard layouts for paste (empty = US QWERTY).</summary>
    public IReadOnlyList<string> PasteLanguages { get; } = new[] { "", "de", "fr" };

    /// <summary>Keyboard shortcuts stored on the device.</summary>
    public ObservableCollection<NanoKvmShortcut> Shortcuts { get; } = new();

    public NanoKvmViewModel(NanoKvmMonitor monitor, H264StreamService stream,
        NanoKvmApiClient api, NanoKvmHidService hid, ISettingsService settings)
    {
        Monitor = monitor;
        _stream = stream;
        _api = api;
        _settings = settings;
        Hid = hid;

        // Pre-fill the layout switch from the settings (backing field, so the setter
        // does not immediately save again).
        _germanLayout = settings.Current.NanoKvmKeyboardLayout != "us";
        Hid.StateChanged += () =>
            Application.Current?.Dispatcher.Dispatch(() => InputConnected = Hid.IsConnected);

        // Resolution (from the SPS) drives the letterbox-correct mouse mapping.
        _stream.DimensionsChanged += (w, h) =>
            Application.Current?.Dispatcher.Dispatch(() =>
            {
                if (h > 0)
                    VideoAspect = (double)w / h;
            });

        // Once the first frame arrives, the connection is up — hide the spinner.
        _stream.FrameReceived += _ =>
        {
            if (IsConnecting)
                Application.Current?.Dispatcher.Dispatch(() => IsConnecting = false);
        };
    }

    [RelayCommand]
    private static Task OpenSettings() => Shell.Current.GoToAsync("nanokvm-settings");

    /// <summary>
    /// Suppresses the stream/input teardown in <c>NanoKvmPage.OnDisappearing</c> ONCE.
    /// When switching to fullscreen the main page disappears, but it should not tear down
    /// the stream and HID — both pages share the same (singleton) ViewModel.
    /// </summary>
    public bool SuppressTeardownOnce { get; set; }

    [RelayCommand]
    private Task OpenFullscreen()
    {
        // Make sure the stream is started when switching to fullscreen.
        if (!IsStreaming)
            StartStream();
        SuppressTeardownOnce = true;
        return Shell.Current.GoToAsync("nanokvm-fullscreen");
    }

    [RelayCommand]
    private static Task CloseFullscreen() => Shell.Current.GoToAsync("..");

    [RelayCommand]
    private void ToggleStream()
    {
        if (IsStreaming)
            StopStream();
        else
            StartStream();
    }

    [RelayCommand]
    private void StartStream()
    {
        if (IsStreaming) return;
        StreamError = null;
        IsStreaming = true;
        IsConnecting = true;
        _stream.Start(OnStreamError);

        // Connect the input automatically so mouse/keyboard are usable right away.
        if (!Hid.IsConnected)
            _ = ConnectInputAsync();
    }

    [RelayCommand]
    private void StopStream()
    {
        _stream.Stop();
        IsStreaming = false;
        IsConnecting = false;
    }

    private void OnStreamError(Exception ex)
    {
        var dispatcher = Application.Current?.Dispatcher;
        dispatcher?.Dispatch(() =>
        {
            IsStreaming = false;
            IsConnecting = false;
            StreamError = ex.Message;
        });
    }

    // ----- Paste / Shortcuts (Phase 4) -----

    [RelayCommand]
    private async Task PasteAsync()
    {
        var text = PasteText ?? "";
        if (text.Length == 0)
            return;
        if (text.Length > MaxPasteLength)
            text = text[..MaxPasteLength];

        try
        {
            await _api.PasteAsync(text, PasteLanguage ?? "");
            InputStatus = "Text sent.";
        }
        catch (Exception ex)
        {
            InputStatus = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task LoadShortcutsAsync()
    {
        try
        {
            var data = await _api.GetShortcutsAsync();
            Shortcuts.Clear();
            foreach (var s in data?.Shortcuts ?? new List<NanoKvmShortcut>())
                Shortcuts.Add(s);
            InputStatus = Shortcuts.Count == 0 ? "No saved shortcuts." : null;
        }
        catch (Exception ex)
        {
            InputStatus = $"Error: {ex.Message}";
        }
    }

    // Run a saved shortcut: send the keys over the WebSocket (the server endpoint
    // only creates shortcuts, it does not execute them).
    [RelayCommand]
    private Task RunShortcut(NanoKvmShortcut shortcut)
        => SendChordAsync(shortcut.Keys.Select(k => k.Code));

    // Fixed standard combinations (JS KeyboardEvent codes).
    [RelayCommand]
    private Task CtrlAltDel() => SendChordAsync(new[] { "ControlLeft", "AltLeft", "Delete" });

    [RelayCommand]
    private Task F12() => SendChordAsync(new[] { "F12" });

    [RelayCommand]
    private Task AltF4() => SendChordAsync(new[] { "AltLeft", "F4" });

    // Sends a key combination as a single report (modifiers + one main key) over the WS.
    private async Task SendChordAsync(IEnumerable<string> codes)
    {
        if (!Hid.IsConnected)
        {
            InputStatus = "Please “Connect input” first.";
            return;
        }

        bool ctrl = false, shift = false, alt = false, meta = false;
        byte key = 0;

        foreach (var code in codes)
        {
            if (HidKeys.IsModifierCode(code, out var which))
            {
                switch (which)
                {
                    case 0: ctrl = true; break;
                    case 1: shift = true; break;
                    case 2: alt = true; break;
                    case 3: meta = true; break;
                }
            }
            else if (HidKeys.TryMapJsCode(code, out var kc))
            {
                key = kc;
            }
        }

        await Hid.SendKeyAsync(key, ctrl, shift, alt, meta);
        InputStatus = "Shortcut sent.";
    }

    // ----- Live-Eingabe / WebSocket (Phase 5) -----

    [RelayCommand]
    private async Task ConnectInputAsync()
    {
        InputStatus = "Connecting input …";
        var ok = await Hid.ConnectAsync();
        InputConnected = Hid.IsConnected;
        InputStatus = ok ? "Input connected." : "Input connection failed.";
    }

    /// <summary>
    /// Wakes the host's HDMI output (turned off via DPMS) with a relative
    /// mouse twitch. Connects the HID channel itself if needed.
    /// </summary>
    [RelayCommand]
    private async Task WakeDisplayAsync()
    {
        InputStatus = "Waking display …";
        var ok = await Hid.WakeAsync();
        InputConnected = Hid.IsConnected;
        InputStatus = ok ? "Wake signal sent." : "Wake-up failed (HID not connected).";
    }

    [RelayCommand]
    private async Task DisconnectInputAsync()
    {
        await Hid.DisconnectAsync();
        InputConnected = false;
        InputStatus = "Input disconnected.";
    }

    [RelayCommand]
    private Task RightClick() => Hid.MouseClickAsync(2);

    [RelayCommand]
    private Task MiddleClick() => Hid.MouseClickAsync(1);

    [RelayCommand]
    private Task ScrollUp() => Hid.ScrollAsync(1);

    [RelayCommand]
    private Task ScrollDown() => Hid.ScrollAsync(-1);

    // Special keys of the on-screen keyboard.
    // Windows/Meta key pressed on its own (e.g. open the Start menu).
    public Task SendWindowsKeyAsync() => Hid.SendKeyAsync(0, false, false, false, meta: true);

    [RelayCommand] private Task KeyEnter() => SendRawKeyAsync(HidKeys.Enter);
    [RelayCommand] private Task KeyBackspace() => SendRawKeyAsync(HidKeys.Backspace);
    [RelayCommand] private Task KeyTab() => SendRawKeyAsync(HidKeys.Tab);
    [RelayCommand] private Task KeyEscape() => SendRawKeyAsync(HidKeys.Escape);
    [RelayCommand] private Task KeyDelete() => SendRawKeyAsync(HidKeys.Delete);
    [RelayCommand] private Task KeyUp() => SendRawKeyAsync(HidKeys.ArrowUp);
    [RelayCommand] private Task KeyDown() => SendRawKeyAsync(HidKeys.ArrowDown);
    [RelayCommand] private Task KeyLeft() => SendRawKeyAsync(HidKeys.ArrowLeft);
    [RelayCommand] private Task KeyRight() => SendRawKeyAsync(HidKeys.ArrowRight);

    /// <summary>Persists the layout selection as soon as the switch is toggled.</summary>
    partial void OnGermanLayoutChanged(bool value)
    {
        _settings.Current.NanoKvmKeyboardLayout = value ? "de" : "us";
        _ = _settings.SaveAsync(_settings.Current);
    }

    /// <summary>Sends a typed character (from the on-screen keyboard) over the WS.</summary>
    public Task SendCharAsync(char c)
    {
        var layout = GermanLayout ? KvmKeyboardLayout.De : KvmKeyboardLayout.Us;
        if (!HidKeys.TryMap(c, layout, out var keycode, out var needsShift, out var altGr))
            return Task.CompletedTask;
        return Hid.SendKeyAsync(keycode, CtrlActive, needsShift || ShiftActive, AltActive, MetaActive, altGr);
    }

    /// <summary>Send Enter (for the fullscreen keyboard bridge).</summary>
    public Task SendEnterAsync() => SendRawKeyAsync(HidKeys.Enter);

    /// <summary>Send Backspace (for the fullscreen keyboard bridge).</summary>
    public Task SendBackspaceAsync() => SendRawKeyAsync(HidKeys.Backspace);

    // Sends a special key, taking the active modifiers into account.
    private Task SendRawKeyAsync(byte keycode)
        => Hid.SendKeyAsync(keycode, CtrlActive, ShiftActive, AltActive, MetaActive);
}
