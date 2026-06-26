using System.Net.WebSockets;
using ServerRemote.App.Services.Hid;

namespace ServerRemote.App.Services;

/// <summary>
/// Wraps the NanoKVM HID WebSocket (<c>/api/ws</c>). Uses the <c>nano-kvm-token</c> cookie set
/// during login from <see cref="NanoKvmApiClient.Cookies"/>, keeps the session open via a
/// heartbeat, and encodes input through a pluggable <see cref="IHidProtocol"/>.
///
/// ⚠️ Riskiest component: verify framing/reconnect against the target firmware before production use.
/// </summary>
public sealed class NanoKvmHidService
{
    private const int HeartbeatSeconds = 10;

    private readonly NanoKvmApiClient _api;
    private readonly ISettingsService _settings;
    private readonly IHidProtocol _protocol;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private IDispatcherTimer? _heartbeat;

    // Last known mouse position + currently pressed buttons (bitmask). The absolute
    // mouse report must carry the pressed buttons on EVERY movement, otherwise a held
    // click (drag/selection) breaks off immediately.
    private double _lastFx = 0.5;
    private double _lastFy = 0.5;
    private byte _buttons;

    // Browser/app button index → HID button bit (as in web/src/lib/mouse.ts).
    private static byte ButtonBit(int button) => button switch
    {
        0 => 0x01, // left
        1 => 0x04, // middle
        2 => 0x02, // right
        3 => 0x08, // back
        4 => 0x10, // forward
        _ => 0
    };

    public NanoKvmHidService(NanoKvmApiClient api, ISettingsService settings, IHidProtocol protocol)
    {
        _api = api;
        _settings = settings;
        _protocol = protocol;
    }

    public bool IsConnected => _ws?.State == WebSocketState.Open;

    public event Action? StateChanged;

    public async Task<bool> ConnectAsync(CancellationToken ct = default)
    {
        await DisconnectAsync();

        if (!_api.IsAuthenticated)
            await _api.LoginAsync(ct);

        var host = _settings.Current.NanoKvmHost;
        if (string.IsNullOrWhiteSpace(host))
            return false;

        var ws = new ClientWebSocket();
        ws.Options.Cookies = _api.Cookies;

        try
        {
            await ws.ConnectAsync(new Uri($"ws://{host}/api/ws"), ct);
            _ws = ws;
            _cts = new CancellationTokenSource();
            StartHeartbeat();
            StateChanged?.Invoke();
            return true;
        }
        catch
        {
            ws.Dispose();
            _ws = null;
            StateChanged?.Invoke();
            return false;
        }
    }

    public async Task DisconnectAsync()
    {
        StopHeartbeat();
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        var ws = _ws;
        _ws = null;
        if (ws is not null)
        {
            try
            {
                if (ws.State == WebSocketState.Open)
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
            }
            catch { /* ignore */ }
            finally { ws.Dispose(); }
        }
        StateChanged?.Invoke();
    }

    private void StartHeartbeat()
    {
        _heartbeat = Application.Current?.Dispatcher.CreateTimer();
        if (_heartbeat is null) return;
        _heartbeat.Interval = TimeSpan.FromSeconds(HeartbeatSeconds);
        _heartbeat.Tick += async (_, _) => await SendAsync(_protocol.EncodeHeartbeat());
        _heartbeat.Start();
    }

    private void StopHeartbeat()
    {
        _heartbeat?.Stop();
        _heartbeat = null;
    }

    // ----- public input API -----

    public Task MouseMoveAsync(double fractionX, double fractionY)
    {
        _lastFx = fractionX;
        _lastFy = fractionY;
        return SendAsync(_protocol.EncodeMouseAbsolute(_buttons, fractionX, fractionY, 0));
    }

    /// <summary>Relative mouse movement (delta in HID units). Goes to the relative
    /// mouse device (HID1) — recognized as activity by the host more reliably than absolute reports.</summary>
    public Task MouseMoveRelativeAsync(int dx, int dy, int wheel = 0)
        => SendAsync(_protocol.EncodeMouseRelative(_buttons, dx, dy, wheel));

    /// <summary>
    /// Wakes a display switched off via DPMS: connects the HID channel if needed and
    /// sends a small relative mouse "twitch" (over and immediately back). Relative movement
    /// is treated by Windows as input activity and turns the HDMI output back on,
    /// without moving the cursor on net.
    /// </summary>
    public async Task<bool> WakeAsync(CancellationToken ct = default)
    {
        if (!IsConnected && !await ConnectAsync(ct))
            return false;

        // Over and back → noticeable movement, but the cursor stays put on net.
        await MouseMoveRelativeAsync(16, 0);
        await MouseMoveRelativeAsync(-16, 0);
        return true;
    }

    public Task MouseButtonAsync(int button, bool down)
    {
        var bit = ButtonBit(button);
        if (down)
            _buttons |= bit;
        else
            _buttons = (byte)(_buttons & ~bit);

        return SendAsync(_protocol.EncodeMouseAbsolute(_buttons, _lastFx, _lastFy, 0));
    }

    public async Task MouseClickAsync(int button)
    {
        await MouseButtonAsync(button, true);
        await MouseButtonAsync(button, false);
    }

    /// <summary>Press/release a mouse button via the RELATIVE device (HID1), without movement.
    /// Required in touchpad mode so the click lands on the relative device (not on HID2).</summary>
    public Task MouseButtonRelativeAsync(int button, bool down)
    {
        var bit = ButtonBit(button);
        if (down)
            _buttons |= bit;
        else
            _buttons = (byte)(_buttons & ~bit);

        return SendAsync(_protocol.EncodeMouseRelative(_buttons, 0, 0, 0));
    }

    public async Task MouseClickRelativeAsync(int button)
    {
        await MouseButtonRelativeAsync(button, true);
        await MouseButtonRelativeAsync(button, false);
    }

    public Task ScrollAsync(int delta)
        => SendAsync(_protocol.EncodeMouseAbsolute(_buttons, _lastFx, _lastFy, delta));

    public async Task SendKeyAsync(byte keycode, bool ctrl, bool shift, bool alt, bool meta, bool altGr = false)
    {
        await SendAsync(_protocol.EncodeKey(keycode, ctrl, shift, alt, meta, altGr));
        await SendAsync(_protocol.EncodeKeyRelease());
    }

    private async Task SendAsync(HidMessage msg)
    {
        var ws = _ws;
        if (ws is null || ws.State != WebSocketState.Open)
            return;

        await _sendLock.WaitAsync();
        try
        {
            var type = msg.IsText ? WebSocketMessageType.Text : WebSocketMessageType.Binary;
            await ws.SendAsync(msg.Data, type, endOfMessage: true, _cts?.Token ?? CancellationToken.None);
        }
        catch
        {
            // Connection lost — report state, no crash.
            StateChanged?.Invoke();
        }
        finally
        {
            _sendLock.Release();
        }
    }
}
