namespace Win2Pad.Core;

/// <summary>
/// Maps a key Windows reports to the HID usage the tablet receives.
///
/// A Bluetooth keyboard sends key positions, not characters: "row 2, third key", and the host
/// applies its own layout to decide that it is a Q (or an A on AZERTY). Win2Pad does the same,
/// from the scan code, which identifies the physical key whatever layout Windows uses. The
/// virtual-key code is the key Windows already interpreted through its layout, so mapping from
/// it would apply the layout twice: Y and Z swapped on German, A/Q and Z/W on French, nonsense on
/// Dvorak. It is used only when there is no scan code, for input injected by software.
/// </summary>
public static class VirtualKeyMap
{
    /// <summary>
    /// Resolves a key from its physical identity. <paramref name="scanCode"/> is the set-1 make
    /// code and <paramref name="extended"/> the E0 prefix flag; together they name the key.
    /// </summary>
    public static bool TryGetUsage(int virtualKey, uint scanCode, bool extended, out byte usage)
    {
        // A real keyboard has no Num Lock state of its own: it reports the physical key and the
        // host applies its own Num Lock. Windows has resolved Num Lock already, so the keypad
        // goes by position before anything else looks at the virtual key.
        if (TryGetNumpadUsage(scanCode, extended, out usage)) return true;
        if (TryGetPositionUsage(scanCode, extended, out usage))
        {
            if (SwapIsoKeys)
            {
                if (usage == IsoLeftOfOne) usage = IsoLeftOfZ;
                else if (usage == IsoLeftOfZ) usage = IsoLeftOfOne;
            }
            return true;
        }

        // No usable scan code: software-injected input, or a key the table leaves to the
        // virtual key because its scan code is ambiguous (Pause and Num Lock share 0x45).
        if (virtualKey == 0x0D && extended)
        {
            usage = 0x58; // Keypad Enter
            return true;
        }
        return TryGetUsage(virtualKey, out usage);
    }

    private const byte IsoLeftOfOne = 0x35; // ` on US, º on ES, ^ on DE
    private const byte IsoLeftOfZ = 0x64;   // < > on most ISO layouts

    /// <summary>
    /// Swaps the two ISO keys some hosts exchange on non-Apple ISO keyboards: the key left of 1
    /// and the extra key left of Z. Set from the app (Keyboard and mouse), or with the
    /// WIN2PAD_SWAP_ISO=1 environment variable.
    /// </summary>
    public static bool SwapIsoKeys { get; set; } =
        Environment.GetEnvironmentVariable("WIN2PAD_SWAP_ISO") == "1";

    /// <summary>
    /// The keypad keys whose virtual-key code changes with Num Lock. The navigation cluster
    /// shares their scan codes and is told apart only by the extended flag, so an extended key
    /// here is Home/End/arrows and never the keypad.
    /// </summary>
    private static bool TryGetNumpadUsage(uint scanCode, bool extended, out byte usage)
    {
        usage = 0;
        if (extended) return false;

        usage = scanCode switch
        {
            0x47 => (byte)0x5F, // Keypad 7
            0x48 => (byte)0x60, // Keypad 8
            0x49 => (byte)0x61, // Keypad 9
            0x4B => (byte)0x5C, // Keypad 4
            0x4C => (byte)0x5D, // Keypad 5
            0x4D => (byte)0x5E, // Keypad 6
            0x4F => (byte)0x59, // Keypad 1
            0x50 => (byte)0x5A, // Keypad 2
            0x51 => (byte)0x5B, // Keypad 3
            0x52 => (byte)0x62, // Keypad 0
            0x53 => (byte)0x63, // Keypad .
            _ => (byte)0
        };
        return usage != 0;
    }

    /// <summary>
    /// Every other key by position. Modifiers are deliberately absent: they travel in the
    /// report's modifier byte, not as key usages. So are Pause and Num Lock (both 0x45) and
    /// Ctrl+Break, which the virtual key tells apart reliably.
    /// </summary>
    internal static bool TryGetPositionUsage(uint scanCode, bool extended, out byte usage)
    {
        usage = 0;
        // Flagged codes (such as 0x21D, the Control Windows synthesizes for AltGr) are not keys.
        if (scanCode is 0 or > 0xFF) return false;

        usage = extended ? Extended(scanCode) : Plain(scanCode);
        return usage != 0;
    }

    private static byte Plain(uint scanCode) => scanCode switch
    {
        0x01 => 0x29, // Escape
        // Number row: 1..9, 0, then the two keys right of 0
        >= 0x02 and <= 0x0A => (byte)(0x1E + (scanCode - 0x02)),
        0x0B => 0x27, // 0
        0x0C => 0x2D, // - on US, ' on ES, ß on DE, ) on FR
        0x0D => 0x2E, // = on US, ¡ on ES, ´ on DE
        0x0E => 0x2A, // Backspace
        0x0F => 0x2B, // Tab
        // Top letter row (Q W E R T Y U I O P on US)
        0x10 => 0x14, 0x11 => 0x1A, 0x12 => 0x08, 0x13 => 0x15, 0x14 => 0x17,
        0x15 => 0x1C, 0x16 => 0x18, 0x17 => 0x0C, 0x18 => 0x12, 0x19 => 0x13,
        0x1A => 0x2F, // [ on US, ` on ES, ü on DE
        0x1B => 0x30, // ] on US, + on ES
        0x1C => 0x28, // Enter
        // Home row (A S D F G H J K L on US)
        0x1E => 0x04, 0x1F => 0x16, 0x20 => 0x07, 0x21 => 0x09, 0x22 => 0x0A,
        0x23 => 0x0B, 0x24 => 0x0D, 0x25 => 0x0E, 0x26 => 0x0F,
        0x27 => 0x33, // ; on US, ñ on ES, ö on DE
        0x28 => 0x34, // ' on US, ´ on ES, ä on DE
        0x29 => IsoLeftOfOne, // ` on US, º on ES; Hankaku/Zenkaku on JIS
        0x2B => 0x31, // \ on US; the ISO key left of Enter (ç on ES, # on UK/DE)
        // Bottom letter row (Z X C V B N M on US)
        0x2C => 0x1D, 0x2D => 0x1B, 0x2E => 0x06, 0x2F => 0x19, 0x30 => 0x05,
        0x31 => 0x11, 0x32 => 0x10,
        0x33 => 0x36, // ,
        0x34 => 0x37, // .
        0x35 => 0x38, // / on US, - on ES
        0x37 => 0x55, // Keypad *
        0x39 => 0x2C, // Space
        0x3A => 0x39, // Caps Lock
        // F1..F10
        >= 0x3B and <= 0x44 => (byte)(0x3A + (scanCode - 0x3B)),
        0x46 => 0x47, // Scroll Lock
        0x4A => 0x56, // Keypad -
        0x4E => 0x57, // Keypad +
        0x54 => 0x46, // SysRq: Print Screen pressed with Alt
        0x56 => IsoLeftOfZ, // the extra ISO key left of Z (< > on ES, DE, FR...)
        0x57 => 0x44, // F11
        0x58 => 0x45, // F12
        0x59 => 0x67, // Keypad = (some layouts)
        // F13..F23, F24
        >= 0x64 and <= 0x6E => (byte)(0x68 + (scanCode - 0x64)),
        0x76 => 0x73, // F24
        // Japanese (JIS), Brazilian (ABNT2) and Korean keys
        0x70 => 0x88, // Katakana/Hiragana (International 2)
        0x73 => 0x87, // Ro on JIS, / ? on ABNT2 (International 1)
        0x79 => 0x8A, // Henkan (International 4)
        0x7B => 0x8B, // Muhenkan (International 5)
        0x7D => 0x89, // Yen (International 3)
        0x7E => 0x85, // Keypad . on ABNT2 (Keypad Comma)
        0x72 => 0x90, // Hangul / English (LANG1)
        0x71 => 0x91, // Hanja (LANG2)
        _ => 0
    };

    private static byte Extended(uint scanCode) => scanCode switch
    {
        0x1C => 0x58, // Keypad Enter
        0x35 => 0x54, // Keypad /
        0x37 => 0x46, // Print Screen
        0x47 => 0x4A, // Home
        0x48 => 0x52, // Up
        0x49 => 0x4B, // Page Up
        0x4B => 0x50, // Left
        0x4D => 0x4F, // Right
        0x4F => 0x4D, // End
        0x50 => 0x51, // Down
        0x51 => 0x4E, // Page Down
        0x52 => 0x49, // Insert
        0x53 => 0x4C, // Delete
        0x5D => 0x65, // Application (menu) key
        0x5E => 0x66, // Power
        _ => 0
    };

    /// <summary>
    /// The fallback by virtual key, for input with no scan code. It describes US positions,
    /// which is the best that can be done without one.
    /// </summary>
    public static bool TryGetUsage(int virtualKey, out byte usage)
    {
        switch (virtualKey)
        {
            case >= 0x41 and <= 0x5A: // A-Z
                usage = (byte)(0x04 + (virtualKey - 0x41));
                return true;
            case >= 0x31 and <= 0x39: // 1-9
                usage = (byte)(0x1E + (virtualKey - 0x31));
                return true;
            case 0x30: usage = 0x27; return true; // 0
            case >= 0x70 and <= 0x7B: // F1-F12
                usage = (byte)(0x3A + (virtualKey - 0x70));
                return true;
            case >= 0x7C and <= 0x87: // F13-F24
                usage = (byte)(0x68 + (virtualKey - 0x7C));
                return true;
            case >= 0x61 and <= 0x69: // Numpad 1-9 (Windows sends these only with Num Lock on)
                usage = (byte)(0x59 + (virtualKey - 0x61));
                return true;
        }

        usage = virtualKey switch
        {
            0x0D => 0x28, // Enter
            0x1B => 0x29, // Escape
            0x08 => 0x2A, // Backspace
            0x09 => 0x2B, // Tab
            0x20 => 0x2C, // Space
            0xBD => 0x2D, // -
            0xBB => 0x2E, // =
            0xDB => 0x2F, // [
            0xDD => 0x30, // ]
            0xDC => 0x31, // \
            0xBA => 0x33, // ;
            0xDE => 0x34, // '
            0xC0 => 0x35, // `
            0xBC => 0x36, // ,
            0xBE => 0x37, // .
            0xBF => 0x38, // /
            0xE2 => 0x64, // VK_OEM_102: ISO key left of Z
            0x14 => 0x39, // Caps Lock
            0x2C => 0x46, // Print Screen
            0x91 => 0x47, // Scroll Lock
            0x13 => 0x48, // Pause
            0x2D => 0x49, // Insert
            0x24 => 0x4A, // Home
            0x21 => 0x4B, // Page Up
            0x2E => 0x4C, // Delete
            0x23 => 0x4D, // End
            0x22 => 0x4E, // Page Down
            0x27 => 0x4F, // Right
            0x25 => 0x50, // Left
            0x28 => 0x51, // Down
            0x26 => 0x52, // Up
            0x90 => 0x53, // Num Lock
            0x6F => 0x54, // Numpad /
            0x6A => 0x55, // Numpad *
            0x6D => 0x56, // Numpad -
            0x6B => 0x57, // Numpad +
            0x60 => 0x62, // Numpad 0
            0x6E => 0x63, // Numpad .
            0x5D => 0x65, // Application (menu) key
            _ => 0
        };

        return usage != 0;
    }
}
