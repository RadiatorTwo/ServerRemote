namespace ServerRemote.App.Services.H264;

/// <summary>
/// A single H.264 frame from the NanoKVM <c>direct</c> stream.
/// <see cref="Data"/> contains the raw Annex-B NAL payload (with start codes
/// <c>00 00 00 01</c>); on keyframes the SPS/PPS are placed in-band before it.
/// </summary>
public readonly record struct H264Frame(bool IsKeyFrame, long TimestampUs, byte[] Data);

/// <summary>
/// Source of decode-ready H.264 frames. Multiple <c>KvmVideoView</c> instances (main page and
/// fullscreen page) attach to the same source; each decodes into its own native surface.
/// </summary>
public interface IH264FrameSource
{
    /// <summary>Raised for each received frame (background thread).</summary>
    event Action<H264Frame>? FrameReceived;

    /// <summary>Raised when the stream stops/drops — attached decoders reset.</summary>
    event Action? StreamReset;

    /// <summary>
    /// Last received keyframe (with SPS/PPS). A freshly attached view can start immediately with it
    /// instead of waiting for the next keyframe — avoids a black image when
    /// switching to fullscreen mode.
    /// </summary>
    H264Frame? LastKeyFrame { get; }
}
