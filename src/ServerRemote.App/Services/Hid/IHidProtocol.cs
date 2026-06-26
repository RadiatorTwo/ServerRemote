namespace ServerRemote.App.Services.Hid;

/// <summary>A fully encoded WebSocket frame plus an indication of whether it is a text or binary frame.</summary>
public readonly record struct HidMessage(byte[] Data, bool IsText);

/// <summary>
/// Encodes HID input (mouse/keyboard) for the NanoKVM WebSocket.
///
/// Framing verified against the official firmware (sipeed/NanoKVM, server <c>service/ws</c>
/// + <c>service/hid</c> and web client <c>lib/mouse.ts</c>/<c>keyboard.ts</c>, as of 2.4.3):
/// binary frame, first byte = event type (0=Heartbeat, 1=Keyboard, 2=Mouse), followed by the
/// raw USB HID report.
/// </summary>
public interface IHidProtocol
{
    /// <summary>
    /// Absolute mouse report (6-byte report for <c>/dev/hidg2</c>): current button bitmask,
    /// position as a fraction (0..1) of the screen area, and scroll-wheel delta. The buttons are
    /// sent with every movement (otherwise a drag, for example, would break off).
    /// </summary>
    HidMessage EncodeMouseAbsolute(byte buttons, double fractionX, double fractionY, int wheel);

    /// <summary>
    /// Relative mouse report (4-byte report for <c>/dev/hidg1</c>): button bitmask plus
    /// delta movement (−127..127) and scroll wheel. The server distinguishes absolute/relative
    /// solely by the report length (6 vs. 4 bytes after the type byte). Relative movement is
    /// recognized more reliably as "activity" by the host — ideal for waking a sleeping display.
    /// </summary>
    HidMessage EncodeMouseRelative(byte buttons, int dx, int dy, int wheel);

    /// <summary>Press a key (USB HID usage ID) with modifiers. <paramref name="altGr"/> =
    /// Right-Alt (for AltGr characters like <c>@ € { } [ ] \ |</c> on the German layout).</summary>
    HidMessage EncodeKey(byte keycode, bool ctrl, bool shift, bool alt, bool meta, bool altGr);

    /// <summary>Release all keys.</summary>
    HidMessage EncodeKeyRelease();

    /// <summary>Heartbeat to keep the session open.</summary>
    HidMessage EncodeHeartbeat();
}
