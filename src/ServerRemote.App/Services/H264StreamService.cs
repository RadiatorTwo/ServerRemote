using System.Buffers.Binary;
using System.Net.WebSockets;
using ServerRemote.App.Services.H264;

namespace ServerRemote.App.Services;

/// <summary>
/// Obtains the NanoKVM live H.264 stream via <c>direct</c> mode
/// (<c>ws://{host}/api/stream/h264/direct</c>) — verified against the official
/// Sipeed firmware (<c>web/src/pages/desktop/screen/h264-direct.tsx</c> + <c>direct.worker.ts</c>).
///
/// Each WebSocket binary message carries exactly one frame:
/// <code>
///   Byte 0      Keyframe flag (1 = key, 0 = delta)
///   Byte 1..8   Timestamp in µs, little-endian
///   Byte 9..    H.264 NAL payload (Annex-B; SPS/PPS in-band on keyframes)
/// </code>
/// The client sends nothing itself — the server pushes from connection setup onward. The raw
/// frames are dispatched via <see cref="FrameReceived"/>; decoding is handled by the
/// platform-native <c>KvmVideoView</c> (hardware decoder).
/// </summary>
public sealed class H264StreamService : IH264FrameSource
{
    private const int HeaderSize = 9;

    private readonly NanoKvmApiClient _api;
    private readonly ISettingsService _settings;

    private CancellationTokenSource? _cts;
    private int _lastWidth, _lastHeight;

    public H264StreamService(NanoKvmApiClient api, ISettingsService settings)
    {
        _api = api;
        _settings = settings;
    }

    public bool IsRunning => _cts is not null;

    public event Action<H264Frame>? FrameReceived;
    public event Action? StreamReset;

    /// <summary>Resolution of the current image (from the SPS). Drives the mouse mapping.</summary>
    public event Action<int, int>? DimensionsChanged;

    public H264Frame? LastKeyFrame { get; private set; }

    /// <summary>Starts the stream. <paramref name="onError"/> reports connection failures.</summary>
    public void Start(Action<Exception>? onError = null)
    {
        Stop();
        var cts = _cts = new CancellationTokenSource();
        _ = Task.Run(() => RunAsync(onError, cts.Token));
    }

    public void Stop()
    {
        var cts = _cts;
        _cts = null;
        if (cts is null)
            return;

        cts.Cancel();
        cts.Dispose();
        LastKeyFrame = null;
        _lastWidth = _lastHeight = 0;
        StreamReset?.Invoke();
    }

    private async Task RunAsync(Action<Exception>? onError, CancellationToken ct)
    {
        try
        {
            if (!_api.IsAuthenticated)
                await _api.LoginAsync(ct);

            var host = _settings.Current.NanoKvmHost;
            if (string.IsNullOrWhiteSpace(host))
                throw new InvalidOperationException("No NanoKVM host configured.");

            using var ws = new ClientWebSocket();
            ws.Options.Cookies = _api.Cookies;
            await ws.ConnectAsync(new Uri($"ws://{host}/api/stream/h264/direct"), ct);

            await ReadFramesAsync(ws, ct);
        }
        catch (OperationCanceledException)
        {
            // stopped normally
        }
        catch (Exception ex)
        {
            onError?.Invoke(ex);
        }
    }

    private async Task ReadFramesAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var chunk = new byte[64 * 1024];
        using var message = new MemoryStream(256 * 1024);

        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            message.SetLength(0);

            WebSocketReceiveResult result;
            do
            {
                result = await ws.ReceiveAsync(new ArraySegment<byte>(chunk), ct);
                if (result.MessageType == WebSocketMessageType.Close)
                    return;
                message.Write(chunk, 0, result.Count);
            }
            while (!result.EndOfMessage);

            if (result.MessageType != WebSocketMessageType.Binary || message.Length < HeaderSize)
                continue;

            EmitFrame(message.GetBuffer(), (int)message.Length);
        }
    }

    private void EmitFrame(byte[] buffer, int length)
    {
        bool isKey = buffer[0] != 0;
        long timestampUs = BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(1, 8));

        int payloadLen = length - HeaderSize;
        var data = new byte[payloadLen];
        Array.Copy(buffer, HeaderSize, data, 0, payloadLen);

        var frame = new H264Frame(isKey, timestampUs, data);

        if (isKey)
        {
            LastKeyFrame = frame;

            // Report resolution from the SPS only when it changes (mouse mapping).
            if (H264Utils.TryGetDimensions(data, out int w, out int h) &&
                (w != _lastWidth || h != _lastHeight))
            {
                _lastWidth = w;
                _lastHeight = h;
                DimensionsChanged?.Invoke(w, h);
            }
        }

        FrameReceived?.Invoke(frame);
    }
}
