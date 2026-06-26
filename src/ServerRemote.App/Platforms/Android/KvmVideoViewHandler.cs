using System.Collections.Concurrent;
using Android.Content;
using Android.Graphics;
using Android.Media;
using Android.Views;
using Android.Widget;
using Microsoft.Maui.Handlers;
using ServerRemote.App.Components;
using ServerRemote.App.Services.H264;
using Java.Nio;

namespace ServerRemote.App.Platforms.Android;

/// <summary>
/// Android handler for <see cref="KvmVideoView"/>. Decodes the H.264 stream via
/// <see cref="MediaCodec"/> (hardware) and renders directly into the <see cref="TextureView"/> surface —
/// no pixel copying, no flickering. A dedicated worker thread feeds the decoder and
/// drains the output; the first keyframe initializes it (SPS/PPS from the csd).
/// </summary>
public sealed class KvmVideoViewHandler : ViewHandler<KvmVideoView, AspectFrameLayout>
{
    public static readonly IPropertyMapper<KvmVideoView, KvmVideoViewHandler> VideoMapper =
        new PropertyMapper<KvmVideoView, KvmVideoViewHandler>(ViewMapper)
        {
            [nameof(KvmVideoView.Source)] = MapSource
        };

    public KvmVideoViewHandler() : base(VideoMapper) { }

    private AndroidH264Renderer? _renderer;
    private IH264FrameSource? _source;

    protected override AspectFrameLayout CreatePlatformView()
    {
        var layout = new AspectFrameLayout(Context);
        var texture = new TextureView(Context);
        _renderer = new AndroidH264Renderer(texture, layout);
        layout.AddView(texture);
        return layout;
    }

    protected override void ConnectHandler(AspectFrameLayout platformView)
    {
        base.ConnectHandler(platformView);
        Subscribe(VirtualView.Source);
    }

    protected override void DisconnectHandler(AspectFrameLayout platformView)
    {
        Unsubscribe();
        _renderer?.Dispose();
        _renderer = null;
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
        if (source is null || _renderer is null)
            return;

        source.FrameReceived += _renderer.Submit;
        source.StreamReset += _renderer.Reset;

        // Freshly attached: start immediately with the most recently received keyframe.
        if (source.LastKeyFrame is { } key)
            _renderer.Submit(key);
    }

    private void Unsubscribe()
    {
        if (_source is null || _renderer is null)
            return;
        _source.FrameReceived -= _renderer.Submit;
        _source.StreamReset -= _renderer.Reset;
        _renderer.Reset();
        _source = null;
    }
}

/// <summary>
/// FrameLayout that centers its single child (the <see cref="TextureView"/>) with correct
/// aspect ratio (AspectFit) — the surrounding margin stays black (letterbox), matching the
/// mouse mapping of the <c>KvmInputSurface</c>.
/// </summary>
public sealed class AspectFrameLayout : FrameLayout
{
    private double _aspect; // width/height; 0 = unknown → full area

    public AspectFrameLayout(Context context) : base(context)
    {
        SetBackgroundColor(global::Android.Graphics.Color.Black);
    }

    public void SetAspect(double aspect)
    {
        if (System.Math.Abs(aspect - _aspect) < 0.0001)
            return;
        _aspect = aspect;
        RequestLayout();
    }

    protected override void OnLayout(bool changed, int left, int top, int right, int bottom)
    {
        int w = right - left;
        int h = bottom - top;
        if (ChildCount == 0 || w <= 0 || h <= 0)
            return;

        var child = GetChildAt(0);
        if (child is null)
            return;

        if (_aspect <= 0)
        {
            child.Layout(0, 0, w, h);
            return;
        }

        double containerAr = (double)w / h;
        int dispW, dispH;
        if (containerAr > _aspect)
        {
            dispH = h;
            dispW = (int)System.Math.Round(h * _aspect);
        }
        else
        {
            dispW = w;
            dispH = (int)System.Math.Round(w / _aspect);
        }

        int offX = (w - dispW) / 2;
        int offY = (h - dispH) / 2;
        child.Layout(offX, offY, offX + dispW, offY + dispH);
    }
}

/// <summary>
/// Encapsulates the <see cref="MediaCodec"/> decoder together with the worker thread and frame queue.
/// </summary>
internal sealed class AndroidH264Renderer : Java.Lang.Object, TextureView.ISurfaceTextureListener
{
    private const string Mime = "video/avc";
    private const int QueueCapacity = 8;

    private readonly TextureView _textureView;
    private readonly AspectFrameLayout _layout;
    private readonly BlockingCollection<H264Frame> _queue =
        new(new ConcurrentQueue<H264Frame>(), QueueCapacity);

    private Surface? _surface;
    private MediaCodec? _codec;
    private Thread? _worker;
    private volatile bool _running;
    private int _cfgWidth, _cfgHeight;

    public AndroidH264Renderer(TextureView textureView, AspectFrameLayout layout)
    {
        _textureView = textureView;
        _layout = layout;
        _textureView.SurfaceTextureListener = this;
    }

    // ----- from the frame source (background thread) -----

    public void Submit(H264Frame frame)
    {
        if (!_running)
            return;
        // When the buffer is full, discard the oldest frame (keep latency low).
        while (!_queue.TryAdd(frame))
            if (!_queue.TryTake(out _))
                break;
    }

    public void Reset()
    {
        while (_queue.TryTake(out _)) { }
        TeardownCodec();
    }

    // ----- TextureView lifecycle -----

    public void OnSurfaceTextureAvailable(SurfaceTexture surface, int width, int height)
    {
        _surface = new Surface(surface);
        StartWorker();
    }

    public bool OnSurfaceTextureDestroyed(SurfaceTexture surface)
    {
        StopWorker();
        _surface?.Release();
        _surface = null;
        return true;
    }

    public void OnSurfaceTextureSizeChanged(SurfaceTexture surface, int width, int height) { }
    public void OnSurfaceTextureUpdated(SurfaceTexture surface) { }

    // ----- Worker -----

    private void StartWorker()
    {
        if (_running)
            return;
        _running = true;
        _worker = new Thread(DecodeLoop) { IsBackground = true, Name = "nanokvm-h264" };
        _worker.Start();
    }

    private void StopWorker()
    {
        _running = false;
        _worker?.Join(500);
        _worker = null;
        TeardownCodec();
    }

    private void DecodeLoop()
    {
        var info = new MediaCodec.BufferInfo();
        try
        {
            while (_running)
            {
                if (!_queue.TryTake(out var frame, 100))
                    continue;

                if (_codec is null)
                {
                    // Wait for the first real keyframe (with SPS) — otherwise the decoder won't start.
                    if (!frame.IsKeyFrame || !H264Utils.ContainsSps(frame.Data))
                        continue;
                    if (!TryInitCodec(frame.Data))
                        continue;
                }
                else if (frame.IsKeyFrame && H264Utils.TryGetDimensions(frame.Data, out int w, out int h)
                         && (w != _cfgWidth || h != _cfgHeight))
                {
                    // Resolution change → re-create the decoder.
                    TeardownCodec();
                    if (!TryInitCodec(frame.Data))
                        continue;
                }

                FeedInput(frame);
                DrainOutput(info);
            }
        }
        catch (Java.Lang.Exception)
        {
            // Decoder/surface torn down — worker shuts down cleanly.
        }
        catch (InvalidOperationException)
        {
            // BlockingCollection after CompleteAdding — normal stop.
        }
    }

    private bool TryInitCodec(byte[] keyframe)
    {
        if (_surface is null)
            return false;
        if (!H264Utils.TryGetParameterSets(keyframe, out var sps, out var pps) || sps is null || pps is null)
            return false;
        if (!H264Utils.TryGetDimensions(keyframe, out int width, out int height))
            return false;

        try
        {
            var format = MediaFormat.CreateVideoFormat(Mime, width, height);
            format.SetByteBuffer("csd-0", ByteBuffer.Wrap(sps));
            format.SetByteBuffer("csd-1", ByteBuffer.Wrap(pps));
            // Low latency where supported.
            format.SetInteger("low-latency", 1);

            var codec = MediaCodec.CreateDecoderByType(Mime);
            codec.Configure(format, _surface, null, (MediaCodecConfigFlags)0);
            codec.Start();
            _codec = codec;
            _cfgWidth = width;
            _cfgHeight = height;

            // Adjust the letterbox layout to the actual resolution (UI thread).
            double aspect = (double)width / height;
            _layout.Post(() => _layout.SetAspect(aspect));
            return true;
        }
        catch (Java.Lang.Exception)
        {
            TeardownCodec();
            return false;
        }
    }

    private void FeedInput(H264Frame frame)
    {
        var codec = _codec;
        if (codec is null)
            return;

        int index = codec.DequeueInputBuffer(10000);
        if (index < 0)
            return;

        var buffer = codec.GetInputBuffer(index);
        if (buffer is null)
        {
            codec.QueueInputBuffer(index, 0, 0, frame.TimestampUs, (MediaCodecBufferFlags)0);
            return;
        }

        buffer.Clear();
        buffer.Put(frame.Data);
        codec.QueueInputBuffer(index, 0, frame.Data.Length, frame.TimestampUs, (MediaCodecBufferFlags)0);
    }

    private void DrainOutput(MediaCodec.BufferInfo info)
    {
        var codec = _codec;
        if (codec is null)
            return;

        int index;
        while ((index = codec.DequeueOutputBuffer(info, 0)) >= 0)
            codec.ReleaseOutputBuffer(index, true); // true = render to surface
    }

    private void TeardownCodec()
    {
        var codec = _codec;
        _codec = null;
        _cfgWidth = _cfgHeight = 0;
        if (codec is null)
            return;
        try { codec.Stop(); } catch (Java.Lang.Exception) { }
        try { codec.Release(); } catch (Java.Lang.Exception) { }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopWorker();
            _queue.Dispose();
            _surface?.Release();
            _surface = null;
        }
        base.Dispose(disposing);
    }
}
