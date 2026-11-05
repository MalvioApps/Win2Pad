namespace Win2Pad.Core;

/// <summary>Maps Windows virtual-key codes to HID keyboard usage IDs.</summary>
public static class VirtualKeyMap
{
    /// <summary>
    /// Resolves a key from its physical identity rather than the virtual-key code Windows
    /// derived from it. A real keyboard has no Num Lock state of its own: it always reports the
    /// physical key and lets the host apply its own Num Lock. Windows resolves Num Lock for us
    /// first, so without this the local machine would silently decide what the remote receives.
    /// </summary>
    public static bool TryGetUsage(int virtualKey, uint scanCode, bool extended, out byte usage)
    {
        if (TryGetNumpadUsage(scanCode, extended, out usage)) return true;
        if (TryGetPunctuationUsage(scanCode, extended, out usage)) return true;

        // Keypad Enter shares VK_RETURN with the main Enter; only the extended flag tells them
        // apart, and some hosts bind them separately.
        if (virtualKey == 0x0D && extended)
        {
            usage = 0x58; // Keypad Enter
            return true;
        }

        return TryGetUsage(virtualKey, out usage);
    }

    /// <summary>
    /// The numpad keys whose virtual-key code changes with Num Lock. The navigation cluster
    /// shares their scan codes and is told apart only by the extended flag, so an extended key
    /// here is Home/End/arrows and never the numpad.
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
    /// When true, swaps the two ISO keys that some hosts (macOS, possibly iPadOS) exchange on
    /// non-Apple ISO keyboards: the key left of 1 and the extra key left of Z. Set with the
    /// WIN2PAD_SWAP_ISO=1 environment variable if those two keys come out reversed on the host.
    /// </summary>
    public static bool SwapIsoKeys { get; set; } =
        Environment.GetEnvironmentVariable("WIN2PAD_SWAP_ISO") == "1";

    /// <summary>
    /// The punctuation keys resolved from their physical position (scan code set 1). Their
    /// virtual-key codes depend on the local layout - on Spanish, VK_OEM_3 is Ñ, not the grave
    /// key - so mapping them by virtual key sends the wrong position. A real keyboard sends the
    /// position and lets the host apply its own layout; this does the same.
    /// </summary>
    private static bool TryGetPunctuationUsage(uint scanCode, bool extended, out byte usage)
    {
        usage = 0;
        // Extended 0x35 is Numpad /, and flagged codes (e.g. 0x21D) are synthesized keys.
        if (extended || scanCode > 0xFF) return false;

        usage = scanCode switch
        {
            0x29 => (byte)0x35, // key left of 1 (` on US, º on ES)
            0x0C => (byte)0x2D, // key right of 0 (- on US, ' on ES)
            0x0D => (byte)0x2E, // = on US, ¡ on ES
            0x1A => (byte)0x2F, // [ on US, ` on ES
            0x1B => (byte)0x30, // ] on US, + on ES
            0x2B => (byte)0x31, // \ on US, ç on ES (ISO Non-US # shares this scan code)
            0x27 => (byte)0x33, // ; on US, Ñ on ES
            0x28 => (byte)0x34, // ' on US, ´ on ES
            0x33 => (byte)0x36, // ,
            0x34 => (byte)0x37, // .
            0x35 => (byte)0x38, // / on US, - on ES
            0x56 => (byte)0x64, // ISO key left of Z (< > on ES), absent on US keyboards
            _ => (byte)0
        };

        if (SwapIsoKeys)
        {
            if (usage == 0x35) usage = 0x64;
            else if (usage == 0x64) usage = 0x35;
        }

        return usage != 0;
    }

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
            _ => 0
        };

        return usage != 0;
    }
}
