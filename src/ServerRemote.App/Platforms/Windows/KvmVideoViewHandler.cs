using System.Collections.Concurrent;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Maui.Handlers;
using Microsoft.UI.Xaml.Controls;
using ServerRemote.App.Components;
using ServerRemote.App.Services.H264;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Playback;

namespace ServerRemote.App.Platforms.Windows;

/// <summary>
/// Windows handler for <see cref="KvmVideoView"/>. Feeds the raw Annex-B H.264 frames via
/// a <see cref="MediaStreamSource"/> into the Media Foundation decoder (hardware where available)
/// and renders them through a <see cref="MediaPlayerElement"/> (<c>Stretch=Uniform</c> = AspectFit,
/// black letterbox borders). The first keyframe initializes the source (resolution from the SPS).
/// </summary>
public sealed class KvmVideoViewHandler : ViewHandler<KvmVideoView, MediaPlayerElement>
{
    public static readonly IPropertyMapper<KvmVideoView, KvmVideoViewHandler> VideoMapper =
        new PropertyMapper<KvmVideoView, KvmVideoViewHandler>(ViewMapper)
        {
            [nameof(KvmVideoView.Source)] = MapSource
        };

    public KvmVideoViewHandler() : base(VideoMapper) { }

    private readonly BlockingCollection<H264Frame> _queue =
        new(new ConcurrentQueue<H264Frame>(), 8);

    private MediaPlayer? _player;
    private MediaStreamSource? _mss;
    private IH264FrameSource? _source;
    private CancellationTokenSource? _drainCts;
    private long _baseTimestampUs = -1;
    private bool _initialized;

    protected override MediaPlayerElement CreatePlatformView()
    {
        var element = new MediaPlayerElement
        {
            Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
            AreTransportControlsEnabled = false,
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black)
        };

        _player = new MediaPlayer { RealTimePlayback = true, AutoPlay = true };
        element.SetMediaPlayer(_player);
        return element;
    }

    protected override void ConnectHandler(MediaPlayerElement platformView)
    {
        base.ConnectHandler(platformView);
        Subscribe(VirtualView.Source);
    }

    protected override void DisconnectHandler(MediaPlayerElement platformView)
    {
        Unsubscribe();
        TeardownSource();
        _player?.Dispose();
        _player = null;
        base.DisconnectHandler(platformView);
    }

    private static void MapSource(KvmVideoViewHandler handler, KvmVideoView view)
    {
        handler.Unsubscribe();
        handler.Subscribe(view.Source);
    }

    private void Subscribe(IH264FrameSource? source)
    {
        _source = source;
        if (source is null)
            return;

        source.FrameReceived += OnFrame;
        source.StreamReset += OnReset;

        if (source.LastKeyFrame is { } key)
            OnFrame(key);
    }

    private void Unsubscribe()
    {
        if (_source is null)
            return;
        _source.FrameReceived -= OnFrame;
        _source.StreamReset -= OnReset;
        _source = null;
        OnReset();
    }

    // ----- frame input (background thread) -----

    private void OnFrame(H264Frame frame)
    {
        if (!_initialized)
        {
            // Wait for the first real keyframe (with SPS) — build resolution + source from it.
            if (!frame.IsKeyFrame || !H264Utils.ContainsSps(frame.Data))
                return;
            if (!H264Utils.TryGetDimensions(frame.Data, out int w, out int h))
                return;
            InitializeSource(w, h);
        }

        // When the buffer is full, discard the oldest frame (keep latency low).
        while (!_queue.TryAdd(frame))
            if (!_queue.TryTake(out _))
                break;
    }

    private void OnReset()
    {
        while (_queue.TryTake(out _)) { }
        TeardownSource();
    }

    private void InitializeSource(int width, int height)
    {
        var props = VideoEncodingProperties.CreateH264();
        props.Width = (uint)width;
        props.Height = (uint)height;

        var descriptor = new VideoStreamDescriptor(props);
        var mss = new MediaStreamSource(descriptor)
        {
            BufferTime = TimeSpan.Zero,
            IsLive = true
        };
        mss.Starting += OnStarting;
        mss.SampleRequested += OnSampleRequested;

        _mss = mss;
        _drainCts = new CancellationTokenSource();
        _baseTimestampUs = -1;
        _initialized = true;

        // Attach the source to the player on the UI thread.
        var element = PlatformView;
        element.DispatcherQueue.TryEnqueue(() =>
        {
            if (_player is not null && _mss == mss)
                _player.Source = MediaSource.CreateFromMediaStreamSource(mss);
        });
    }

    private void TeardownSource()
    {
        _initialized = false;
        _drainCts?.Cancel();

        var mss = _mss;
        _mss = null;
        if (mss is not null)
        {
            mss.Starting -= OnStarting;
            mss.SampleRequested -= OnSampleRequested;
        }

        var player = _player;
        var element = PlatformView;
        element?.DispatcherQueue.TryEnqueue(() =>
        {
            if (player is not null)
                player.Source = null;
        });

        _drainCts?.Dispose();
        _drainCts = null;
        _baseTimestampUs = -1;
    }

    private void OnStarting(MediaStreamSource sender, MediaStreamSourceStartingEventArgs args)
        => args.Request.SetActualStartPosition(TimeSpan.Zero);

    private void OnSampleRequested(MediaStreamSource sender, MediaStreamSourceSampleRequestedEventArgs args)
    {
        var token = _drainCts?.Token ?? CancellationToken.None;
        H264Frame frame;
        try
        {
            frame = _queue.Take(token);
        }
        catch (OperationCanceledException)
        {
            return; // teardown in progress — no sample, MF ends the request
        }

        if (_baseTimestampUs < 0)
            _baseTimestampUs = frame.TimestampUs;

        long relUs = frame.TimestampUs - _baseTimestampUs;
        if (relUs < 0)
            relUs = 0;

        var sample = MediaStreamSample.CreateFromBuffer(
            frame.Data.AsBuffer(),
            TimeSpan.FromTicks(relUs * 10)); // 1 µs = 10 ticks
        sample.Duration = TimeSpan.FromMilliseconds(16);
        sample.KeyFrame = frame.IsKeyFrame;
        args.Request.Sample = sample;
    }
}
