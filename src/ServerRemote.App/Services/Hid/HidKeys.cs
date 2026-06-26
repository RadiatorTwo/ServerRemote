namespace ServerRemote.App.Services.Hid;

/// <summary>Host keyboard layout for the character→HID mapping.</summary>
public enum KvmKeyboardLayout
{
    /// <summary>US QWERTY.</summary>
    Us,
    /// <summary>German QWERTZ.</summary>
    De
}

/// <summary>
/// Translates typed characters into USB HID usage IDs along with the required Shift/AltGr.
/// HID codes are position-based; the host applies its own layout, so the mapping must match the
/// host layout (<see cref="KvmKeyboardLayout"/>). Covers the common keys for a simple on-screen
/// keyboard.
/// </summary>
public static class HidKeys
{
    public const byte Enter = 0x28;
    public const byte Escape = 0x29;
    public const byte Backspace = 0x2A;
    public const byte Tab = 0x2B;
    public const byte Space = 0x2C;
    public const byte Delete = 0x4C;
    public const byte ArrowRight = 0x4F;
    public const byte ArrowLeft = 0x50;
    public const byte ArrowDown = 0x51;
    public const byte ArrowUp = 0x52;
    public const byte F4 = 0x3D;
    public const byte F12 = 0x45;

    /// <summary>
    /// Returns the HID usage ID along with the required Shift/AltGr for the given host layout, or
    /// <c>false</c> if it cannot be mapped.
    /// </summary>
    public static bool TryMap(char c, KvmKeyboardLayout layout, out byte keycode, out bool shift, out bool altGr)
    {
        altGr = false;

        // Layout-independent control keys.
        switch (c)
        {
            case '\n' or '\r': keycode = Enter; shift = false; return true;
            case ' ': keycode = Space; shift = false; return true;
            case '\t': keycode = Tab; shift = false; return true;
        }

        return layout == KvmKeyboardLayout.De
            ? TryMapDe(c, out keycode, out shift, out altGr)
            : TryMapUs(c, out keycode, out shift);
    }

    // ----- US QWERTY -----
    private static bool TryMapUs(char c, out byte keycode, out bool shift)
    {
        shift = false;
        keycode = 0;

        switch (c)
        {
            case >= 'a' and <= 'z':
                keycode = (byte)(0x04 + (c - 'a'));
                return true;
            case >= 'A' and <= 'Z':
                keycode = (byte)(0x04 + (c - 'A'));
                shift = true;
                return true;
            case >= '1' and <= '9':
                keycode = (byte)(0x1E + (c - '1'));
                return true;
            case '0':
                keycode = 0x27;
                return true;
        }

        (byte code, bool sh)? sym = c switch
        {
            '-' => (0x2D, false),
            '_' => (0x2D, true),
            '=' => (0x2E, false),
            '+' => (0x2E, true),
            '[' => (0x2F, false),
            '{' => (0x2F, true),
            ']' => (0x30, false),
            '}' => (0x30, true),
            '\\' => (0x31, false),
            '|' => (0x31, true),
            ';' => (0x33, false),
            ':' => (0x33, true),
            '\'' => (0x34, false),
            '"' => (0x34, true),
            '`' => (0x35, false),
            '~' => (0x35, true),
            ',' => (0x36, false),
            '<' => (0x36, true),
            '.' => (0x37, false),
            '>' => (0x37, true),
            '/' => (0x38, false),
            '?' => (0x38, true),
            '!' => (0x1E, true),
            '@' => (0x1F, true),
            '#' => (0x20, true),
            '$' => (0x21, true),
            '%' => (0x22, true),
            '^' => (0x23, true),
            '&' => (0x24, true),
            '*' => (0x25, true),
            '(' => (0x26, true),
            ')' => (0x27, true),
            _ => null
        };

        if (sym is { } s)
        {
            keycode = s.code;
            shift = s.sh;
            return true;
        }

        return false;
    }

    // ----- German QWERTZ -----
    // HID codes are the PHYSICAL US positions; on a German host they produce the German
    // character. Key differences: y/z swapped, umlauts/ß on their own keys, many symbols via
    // AltGr (Right-Alt). Dead keys (^ ` ´) are intentionally omitted.
    private static bool TryMapDe(char c, out byte keycode, out bool shift, out bool altGr)
    {
        shift = false;
        altGr = false;
        keycode = 0;

        switch (c)
        {
            // Letters: only y/z swapped compared to US.
            case 'y': keycode = 0x1D; return true;            // physical US Z position
            case 'z': keycode = 0x1C; return true;            // physical US Y position
            case 'Y': keycode = 0x1D; shift = true; return true;
            case 'Z': keycode = 0x1C; shift = true; return true;
            case >= 'a' and <= 'z': keycode = (byte)(0x04 + (c - 'a')); return true;
            case >= 'A' and <= 'Z': keycode = (byte)(0x04 + (c - 'A')); shift = true; return true;

            // Number row (unshifted same as US).
            case >= '1' and <= '9': keycode = (byte)(0x1E + (c - '1')); return true;
            case '0': keycode = 0x27; return true;
        }

        // (code, shift, altGr)
        (byte code, bool sh, bool ag)? sym = c switch
        {
            // Shifted number row (German).
            '!' => (0x1E, true, false),
            '"' => (0x1F, true, false),
            '§' => (0x20, true, false),
            '$' => (0x21, true, false),
            '%' => (0x22, true, false),
            '&' => (0x23, true, false),
            '/' => (0x24, true, false),
            '(' => (0x25, true, false),
            ')' => (0x26, true, false),
            '=' => (0x27, true, false),

            // AltGr of the number/letter rows.
            '@' => (0x14, false, true),   // AltGr+Q
            '€' => (0x08, false, true),   // AltGr+E
            '{' => (0x24, false, true),   // AltGr+7
            '[' => (0x25, false, true),   // AltGr+8
            ']' => (0x26, false, true),   // AltGr+9
            '}' => (0x27, false, true),   // AltGr+0
            '\\' => (0x2D, false, true),  // AltGr+ß
            '~' => (0x30, false, true),   // AltGr++
            '|' => (0x64, false, true),   // AltGr+< (ISO key)
            'µ' => (0x10, false, true),   // AltGr+M

            // Dedicated keys to the right of the letters.
            'ü' => (0x2F, false, false),
            'Ü' => (0x2F, true, false),
            'ö' => (0x33, false, false),
            'Ö' => (0x33, true, false),
            'ä' => (0x34, false, false),
            'Ä' => (0x34, true, false),
            'ß' => (0x2D, false, false),
            '?' => (0x2D, true, false),
            '+' => (0x30, false, false),
            '*' => (0x30, true, false),
            '#' => (0x31, false, false),
            '\'' => (0x31, true, false),
            '<' => (0x64, false, false),
            '>' => (0x64, true, false),

            // Bottom row.
            ',' => (0x36, false, false),
            ';' => (0x36, true, false),
            '.' => (0x37, false, false),
            ':' => (0x37, true, false),
            '-' => (0x38, false, false),
            '_' => (0x38, true, false),

            _ => null
        };

        if (sym is { } s)
        {
            keycode = s.code;
            shift = s.sh;
            altGr = s.ag;
            return true;
        }

        return false;
    }

    /// <summary>Detects JS KeyboardEvent modifier codes. <paramref name="which"/>: 0=Ctrl,1=Shift,2=Alt,3=Meta.</summary>
    public static bool IsModifierCode(string code, out int which)
    {
        which = code switch
        {
            "ControlLeft" or "ControlRight" => 0,
            "ShiftLeft" or "ShiftRight" => 1,
            "AltLeft" or "AltRight" => 2,
            "MetaLeft" or "MetaRight" or "OSLeft" or "OSRight" => 3,
            _ => -1
        };
        return which >= 0;
    }

    /// <summary>Maps a JS KeyboardEvent code (e.g. "KeyA", "F4", "Delete") to a HID usage ID.</summary>
    public static bool TryMapJsCode(string code, out byte keycode)
    {
        keycode = 0;

        if (code.StartsWith("Key", StringComparison.Ordinal) && code.Length == 4)
        {
            var c = char.ToLowerInvariant(code[3]);
            if (c is >= 'a' and <= 'z') { keycode = (byte)(0x04 + (c - 'a')); return true; }
        }

        if (code.StartsWith("Digit", StringComparison.Ordinal) && code.Length == 6)
        {
            var d = code[5];
            if (d == '0') { keycode = 0x27; return true; }
            if (d is >= '1' and <= '9') { keycode = (byte)(0x1E + (d - '1')); return true; }
        }

        if (code.StartsWith("F", StringComparison.Ordinal) &&
            byte.TryParse(code.AsSpan(1), out var fn) && fn is >= 1 and <= 12)
        {
            keycode = (byte)(0x3A + (fn - 1));
            return true;
        }

        keycode = code switch
        {
            "Enter" or "NumpadEnter" => Enter,
            "Escape" => Escape,
            "Backspace" => Backspace,
            "Tab" => Tab,
            "Space" => Space,
            "Delete" => Delete,
            "ArrowRight" => ArrowRight,
            "ArrowLeft" => ArrowLeft,
            "ArrowDown" => ArrowDown,
            "ArrowUp" => ArrowUp,
            "Minus" => 0x2D,
            "Equal" => 0x2E,
            "BracketLeft" => 0x2F,
            "BracketRight" => 0x30,
            "Backslash" => 0x31,
            "Semicolon" => 0x33,
            "Quote" => 0x34,
            "Backquote" => 0x35,
            "Comma" => 0x36,
            "Period" => 0x37,
            "Slash" => 0x38,
            _ => 0
        };
        return keycode != 0;
    }
}
