using ServerRemote.App.Services.H264;

namespace ServerRemote.App.Components;

/// <summary>
/// Native hardware video display for the NanoKVM H.264 stream. Binds to an
/// <see cref="IH264FrameSource"/> (the <c>H264StreamService</c>); the platform-native handler
/// decodes the frames using a hardware decoder (<c>MediaCodec</c> on Android,
/// <c>MediaStreamSource</c> on Windows) and renders them flicker-free with letterboxing (AspectFit).
///
/// Multiple instances (main and fullscreen page) may bind to the same source — each one
/// decodes independently into its own surface.
/// </summary>
public sealed class KvmVideoView : View
{
    public static readonly BindableProperty SourceProperty = BindableProperty.Create(
        nameof(Source), typeof(IH264FrameSource), typeof(KvmVideoView));

    public IH264FrameSource? Source
    {
        get => (IH264FrameSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }
}
