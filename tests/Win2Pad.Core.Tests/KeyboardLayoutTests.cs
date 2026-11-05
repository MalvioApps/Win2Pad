using Xunit;

namespace Win2Pad.Core.Tests;

/// <summary>
/// A physical key must reach the tablet as the same position whatever layout Windows uses:
/// the tablet applies its own layout, so mapping through Windows' would apply one twice.
/// </summary>
public sealed class KeyboardLayoutTests
{
    // Virtual keys Windows reports for the same physical key under different layouts.
    private const int A = 0x41, F = 0x46, M = 0x4D, Q = 0x51, W = 0x57, Y = 0x59, Z = 0x5A;
    private const int OemComma = 0xBC, Oem1 = 0xBA, Oem7 = 0xDE;

    [Theory]
    // Q position (scan 0x10): Q on US/DE, A on AZERTY, ' on Dvorak
    [InlineData(0x10u, Q, 0x14)]
    [InlineData(0x10u, A, 0x14)]
    [InlineData(0x10u, Oem7, 0x14)]
    // Y position (0x15): Y on US/FR, Z on QWERTZ, F on Dvorak
    [InlineData(0x15u, Y, 0x1C)]
    [InlineData(0x15u, Z, 0x1C)]
    [InlineData(0x15u, F, 0x1C)]
    // Z position (0x2C): Z on US, Y on QWERTZ, W on AZERTY, ; on Dvorak
    [InlineData(0x2Cu, Z, 0x1D)]
    [InlineData(0x2Cu, Y, 0x1D)]
    [InlineData(0x2Cu, W, 0x1D)]
    [InlineData(0x2Cu, Oem1, 0x1D)]
    // A position (0x1E): A on US, Q on AZERTY
    [InlineData(0x1Eu, A, 0x04)]
    [InlineData(0x1Eu, Q, 0x04)]
    // W position (0x11): W on US, Z on AZERTY, , on Dvorak
    [InlineData(0x11u, W, 0x1A)]
    [InlineData(0x11u, Z, 0x1A)]
    [InlineData(0x11u, OemComma, 0x1A)]
    // M position (0x32): M on US, , on AZERTY
    [InlineData(0x32u, M, 0x10)]
    [InlineData(0x32u, OemComma, 0x10)]
    // ; position (0x27): ; on US, M on AZERTY, ñ on ES
    [InlineData(0x27u, Oem1, 0x33)]
    [InlineData(0x27u, M, 0x33)]
    public void The_same_physical_key_maps_to_the_same_position_on_every_layout(uint scan, int virtualKey, byte expected)
    {
        Assert.True(VirtualKeyMap.TryGetUsage(virtualKey, scan, extended: false, out var usage));
        Assert.Equal(expected, usage);
    }

    [Fact]
    public void All_letters_and_digits_are_covered_by_position()
    {
        var usages = new HashSet<byte>();
        foreach (var scan in Enumerable.Range(0x02, 0x32 - 0x02 + 1).Select(s => (uint)s))
            if (VirtualKeyMap.TryGetPositionUsage(scan, false, out var usage)) usages.Add(usage);

        for (byte letter = 0x04; letter <= 0x1D; letter++) Assert.Contains(letter, usages);   // A..Z
        for (byte digit = 0x1E; digit <= 0x27; digit++) Assert.Contains(digit, usages);      // 1..0
    }

    [Fact]
    public void No_two_keys_share_a_position_except_print_screen_and_sysrq()
    {
        var seen = new Dictionary<byte, (uint Scan, bool Extended)>();
        foreach (var extended in new[] { false, true })
        for (uint scan = 1; scan <= 0xFF; scan++)
        {
            if (!VirtualKeyMap.TryGetPositionUsage(scan, extended, out var usage)) continue;
            if (seen.TryGetValue(usage, out var first))
            {
                // Print Screen is E0 37, and the same key reports 54 while Alt is held.
                Assert.Equal((byte)0x46, usage);
                Assert.Contains((first.Scan, first.Extended), new[] { (0x54u, false), (0x37u, true) });
                continue;
            }
            seen[usage] = (scan, extended);
        }
    }

    [Theory]
    [InlineData(0x0Du, 0x1Cu, false, 0x28)] // Enter
    [InlineData(0x0Du, 0x1Cu, true, 0x58)]  // Keypad Enter
    [InlineData(0xBFu, 0x35u, false, 0x38)] // / (US)
    [InlineData(0x6Fu, 0x35u, true, 0x54)]  // Keypad /
    [InlineData(0x6Au, 0x37u, false, 0x55)] // Keypad *
    [InlineData(0x2Cu, 0x37u, true, 0x46)]  // Print Screen
    [InlineData(0x2Cu, 0x54u, false, 0x46)] // Alt+Print Screen (SysRq)
    [InlineData(0x24u, 0x47u, true, 0x4A)]  // Home
    [InlineData(0x24u, 0x47u, false, 0x5F)] // Keypad 7 with Num Lock off still reports VK_HOME
    [InlineData(0x67u, 0x47u, false, 0x5F)] // Keypad 7 with Num Lock on
    [InlineData(0x2Eu, 0x53u, true, 0x4C)]  // Delete
    [InlineData(0x2Eu, 0x53u, false, 0x63)] // Keypad . with Num Lock off
    [InlineData(0x5Du, 0x5Du, true, 0x65)]  // Application (menu) key
    public void Keys_sharing_a_scan_code_are_told_apart_by_the_extended_flag(uint vk, uint scan, bool extended, byte expected)
    {
        Assert.True(VirtualKeyMap.TryGetUsage((int)vk, scan, extended, out var usage));
        Assert.Equal(expected, usage);
    }

    [Theory]
    [InlineData(0x13, false, 0x48)] // Pause
    [InlineData(0x90, true, 0x53)]  // Num Lock (reported extended)
    [InlineData(0x90, false, 0x53)] // Num Lock (some keyboards do not set the flag)
    public void Pause_and_num_lock_share_0x45_and_go_by_virtual_key(int vk, bool extended, byte expected)
    {
        Assert.False(VirtualKeyMap.TryGetPositionUsage(0x45, extended, out _));
        Assert.True(VirtualKeyMap.TryGetUsage(vk, 0x45, extended, out var usage));
        Assert.Equal(expected, usage);
    }

    [Theory]
    [InlineData(0xA0, 0x2Au, false)] // Left Shift
    [InlineData(0xA1, 0x36u, false)] // Right Shift
    [InlineData(0xA2, 0x1Du, false)] // Left Control
    [InlineData(0xA3, 0x1Du, true)]  // Right Control
    [InlineData(0xA4, 0x38u, false)] // Left Alt
    [InlineData(0xA5, 0x38u, true)]  // Right Alt / AltGr
    [InlineData(0x5B, 0x5Bu, true)]  // Left Windows
    [InlineData(0x5C, 0x5Cu, true)]  // Right Windows
    public void Modifiers_never_become_key_usages(int vk, uint scan, bool extended)
    {
        // They travel in the report's modifier byte; a key usage as well would press them twice.
        Assert.False(VirtualKeyMap.TryGetUsage(vk, scan, extended, out _));
    }

    [Theory]
    [InlineData(0x41, 0x04)] // A
    [InlineData(0x51, 0x14)] // Q
    [InlineData(0x31, 0x1E)] // 1
    [InlineData(0x7C, 0x68)] // F13
    [InlineData(0x87, 0x73)] // F24
    public void Injected_input_without_a_scan_code_falls_back_to_the_virtual_key(int vk, byte expected)
    {
        Assert.True(VirtualKeyMap.TryGetUsage(vk, 0, extended: false, out var usage));
        Assert.Equal(expected, usage);
    }

    [Fact]
    public void Typed_unicode_characters_have_no_position()
    {
        // VK_PACKET carries a character, not a key; there is nothing to send for it.
        Assert.False(VirtualKeyMap.TryGetUsage(0xE7, 0, extended: false, out _));
    }

    [Fact]
    public void Synthesized_scan_codes_are_not_positions()
    {
        // 0x21D is the Control Windows adds for AltGr; flagged codes are never physical keys.
        Assert.False(VirtualKeyMap.TryGetPositionUsage(0x21D, false, out _));
        Assert.False(VirtualKeyMap.TryGetUsage(0xA2, 0x21D, extended: false, out _));
    }

    [Theory]
    [InlineData(0x70u, 0x88)] // Katakana/Hiragana (JIS)
    [InlineData(0x73u, 0x87)] // Ro (JIS), / ? (ABNT2)
    [InlineData(0x79u, 0x8A)] // Henkan (JIS)
    [InlineData(0x7Bu, 0x8B)] // Muhenkan (JIS)
    [InlineData(0x7Du, 0x89)] // Yen (JIS)
    [InlineData(0x7Eu, 0x85)] // Keypad . (ABNT2)
    [InlineData(0x72u, 0x90)] // Hangul / English (Korean)
    [InlineData(0x71u, 0x91)] // Hanja (Korean)
    [InlineData(0x29u, 0x35)] // Hankaku/Zenkaku (JIS) is the key left of 1
    public void Japanese_brazilian_and_korean_keys_have_their_international_usages(uint scan, byte expected)
    {
        Assert.True(VirtualKeyMap.TryGetPositionUsage(scan, false, out var usage));
        Assert.Equal(expected, usage);
        Assert.True(usage <= HidDescriptors.MaxKeyUsage);
    }

    [Theory]
    [InlineData(0x64u, 0x7C, 0x68)] // F13
    [InlineData(0x6Eu, 0x86, 0x72)] // F23
    [InlineData(0x76u, 0x87, 0x73)] // F24
    [InlineData(0x57u, 0x7A, 0x44)] // F11
    [InlineData(0x58u, 0x7B, 0x45)] // F12
    public void Function_keys_agree_by_position_and_by_virtual_key(uint scan, int vk, byte expected)
    {
        Assert.True(VirtualKeyMap.TryGetUsage(vk, scan, false, out var byPosition));
        Assert.True(VirtualKeyMap.TryGetUsage(vk, out var byVirtualKey));
        Assert.Equal(expected, byPosition);
        Assert.Equal(expected, byVirtualKey);
    }

    [Fact]
    public void An_azerty_keyboard_types_its_letters_through_the_capture()
    {
        // French AZERTY: the key Windows calls A sits where US has Q.
        using var capture = new InputCapture();
        byte[] lastUsages = [];
        capture.KeyboardReport += (_, usages) => lastUsages = usages;

        capture.HandleKeyboardEvent(A, isDown: true, scanCode: 0x10);
        Assert.Equal(new byte[] { 0x14 }, lastUsages);
        capture.HandleKeyboardEvent(A, isDown: false, scanCode: 0x10);
        Assert.Empty(lastUsages);
    }

    [Fact]
    public void A_qwertz_keyboard_does_not_swap_y_and_z()
    {
        using var capture = new InputCapture();
        byte[] lastUsages = [];
        capture.KeyboardReport += (_, usages) => lastUsages = usages;

        capture.HandleKeyboardEvent(Z, isDown: true, scanCode: 0x15); // German Z, US Y position
        Assert.Equal(new byte[] { 0x1C }, lastUsages);
    }

    [Fact]
    public void The_iso_swap_moves_only_the_two_iso_keys_and_only_by_position()
    {
        var previous = VirtualKeyMap.SwapIsoKeys;
        try
        {
            VirtualKeyMap.SwapIsoKeys = true;
            Assert.True(VirtualKeyMap.TryGetUsage(0xDC, 0x29, false, out var leftOfOne));
            Assert.True(VirtualKeyMap.TryGetUsage(0xE2, 0x56, false, out var leftOfZ));
            Assert.True(VirtualKeyMap.TryGetUsage(0x51, 0x10, false, out var q));
            Assert.Equal((0x64, 0x35, 0x14), (leftOfOne, leftOfZ, q));
        }
        finally
        {
            VirtualKeyMap.SwapIsoKeys = previous;
        }
    }
}
