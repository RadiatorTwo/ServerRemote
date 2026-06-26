namespace ServerRemote.App.Services.Hid;

/// <summary>
/// NanoKVM HID protocol (binary), verified against the official firmware 2.4.3
/// (sipeed/NanoKVM): every WebSocket frame is binary, first byte = event type
/// (0=heartbeat, 1=keyboard, 2=mouse), followed by the raw USB HID report.
///
/// Source: <c>server/service/ws/client.go</c> (type byte + <c>data[1:]</c>),
/// <c>server/service/hid/{keyboard,mouse}.go</c> (report lengths 8/6/4) and the web client
/// <c>web/src/lib/mouse.ts</c> (absolute position <c>floor(0x7FFF·f)+1</c>, little-endian).
/// </summary>
public sealed class BinaryHidProtocol : IHidProtocol
{
    private const byte TypeHeartbeat = 0;
    private const byte TypeKeyboard = 1;
    private const byte TypeMouse = 2;

    // Absolute position: 16-bit, range 1..0x8000 (like the web client).
    private static int ScaleAbs(double fraction)
        => (int)(Math.Clamp(fraction, 0.0, 1.0) * 0x7FFF) + 1;

    private static byte Modifiers(bool ctrl, bool shift, bool alt, bool meta, bool altGr)
    {
        byte m = 0;
        if (ctrl) m |= 0x01;  // Left Ctrl
        if (shift) m |= 0x02;  // Left Shift
        if (alt) m |= 0x04;  // Left Alt
        if (meta) m |= 0x08;  // Left GUI/Meta
        if (altGr) m |= 0x40;  // Right Alt (AltGr)
        return m;
    }

    // Absolute mouse report (6 bytes → /dev/hidg2): [buttons, xLo, xHi, yLo, yHi, wheel].
    public HidMessage EncodeMouseAbsolute(byte buttons, double fractionX, double fractionY, int wheel)
    {
        int x = ScaleAbs(fractionX);
        int y = ScaleAbs(fractionY);
        var w = (sbyte)Math.Clamp(wheel, -127, 127);

        return new(new byte[]
        {
            TypeMouse, buttons,
            (byte)(x & 0xFF), (byte)((x >> 8) & 0xFF),
            (byte)(y & 0xFF), (byte)((y >> 8) & 0xFF),
            (byte)w
        }, IsText: false);
    }

    // Relative mouse report (4 bytes → /dev/hidg1): [buttons, dx, dy, wheel].
    // The server selects the HID device purely by the report length (server/service/hid/mouse.go:
    // 4 bytes → relative/HID1, 6 bytes → absolute/HID2).
    public HidMessage EncodeMouseRelative(byte buttons, int dx, int dy, int wheel)
    {
        var x = (sbyte)Math.Clamp(dx, -127, 127);
        var y = (sbyte)Math.Clamp(dy, -127, 127);
        var w = (sbyte)Math.Clamp(wheel, -127, 127);

        return new(new byte[] { TypeMouse, buttons, (byte)x, (byte)y, (byte)w }, IsText: false);
    }

    // Boot keyboard report (8 bytes → /dev/hidg0): [modifiers, reserved, key1..key6].
    public HidMessage EncodeKey(byte keycode, bool ctrl, bool shift, bool alt, bool meta, bool altGr)
        => new(new byte[]
        {
            TypeKeyboard, Modifiers(ctrl, shift, alt, meta, altGr), 0, keycode, 0, 0, 0, 0, 0
        }, IsText: false);

    public HidMessage EncodeKeyRelease()
        => new(new byte[] { TypeKeyboard, 0, 0, 0, 0, 0, 0, 0, 0 }, IsText: false);

    public HidMessage EncodeHeartbeat()
        => new(new byte[] { TypeHeartbeat }, IsText: false);
}
