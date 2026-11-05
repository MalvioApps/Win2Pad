using Win2Pad.Core;
using Xunit;

namespace Win2Pad.Core.Tests;

/// <summary>
/// Non-US layouts: the host must receive physical key positions, and AltGr must not leak the
/// Left Control that Windows synthesizes for it.
/// </summary>
public sealed class LayoutIndependenceTests
{
    [Theory]
    // Spanish layout: (virtual key Windows reports, scan code, HID usage of that position)
    [InlineData(0xC0, 0x27u, 0x33)] // Ñ          - VK_OEM_3, would be ` (0x35) by virtual key
    [InlineData(0xDB, 0x0Cu, 0x2D)] // ' ?        - VK_OEM_4, would be [ (0x2F)
    [InlineData(0xDD, 0x0Du, 0x2E)] // ¡ ¿        - VK_OEM_6, would be ] (0x30)
    [InlineData(0xDC, 0x29u, 0x35)] // º ª        - VK_OEM_5, would be \ (0x31)
    [InlineData(0xBA, 0x1Au, 0x2F)] // ` ^        - VK_OEM_1, would be ; (0x33)
    [InlineData(0xBB, 0x1Bu, 0x30)] // + *        - VK_OEM_PLUS, would be = (0x2E)
    [InlineData(0xBF, 0x2Bu, 0x31)] // Ç          - VK_OEM_2, would be / (0x38)
    [InlineData(0xDE, 0x28u, 0x34)] // ´ ¨
    [InlineData(0xBD, 0x35u, 0x38)] // - _        - VK_OEM_MINUS, would be - (0x2D)
    [InlineData(0xE2, 0x56u, 0x64)] // < >        - VK_OEM_102, previously unmapped
    [InlineData(0xBC, 0x33u, 0x36)] // ,
    [InlineData(0xBE, 0x34u, 0x37)] // .
    public void Spanish_punctuation_resolves_to_its_physical_position(int virtualKey, uint scanCode, byte expected)
    {
        Assert.True(VirtualKeyMap.TryGetUsage(virtualKey, scanCode, extended: false, out var usage));
        Assert.Equal(expected, usage);
    }

    [Fact]
    public void Us_layout_punctuation_is_unchanged()
    {
        // On US the virtual key and the position agree, so the result must match the old map.
        Assert.True(VirtualKeyMap.TryGetUsage(0xBA, 0x27, extended: false, out var semicolon));
        Assert.Equal(0x33, semicolon);
        Assert.True(VirtualKeyMap.TryGetUsage(0xBF, 0x35, extended: false, out var slash));
        Assert.Equal(0x38, slash);
    }

    [Fact]
    public void Numpad_divide_is_not_mistaken_for_the_slash_key()
    {
        // Numpad / is scan code 0x35 with the extended flag.
        Assert.True(VirtualKeyMap.TryGetUsage(0x6F, 0x35, extended: true, out var usage));
        Assert.Equal(0x54, usage);
    }

    [Fact]
    public void Injected_punctuation_without_a_scan_code_falls_back_to_the_virtual_key()
    {
        Assert.True(VirtualKeyMap.TryGetUsage(0xBA, 0, extended: false, out var usage));
        Assert.Equal(0x33, usage);
    }

    [Fact]
    public void Iso_swap_exchanges_only_the_two_iso_keys()
    {
        var previous = VirtualKeyMap.SwapIsoKeys;
        try
        {
            VirtualKeyMap.SwapIsoKeys = true;
            VirtualKeyMap.TryGetUsage(0xDC, 0x29, false, out var leftOfOne);
            VirtualKeyMap.TryGetUsage(0xE2, 0x56, false, out var leftOfZ);
            VirtualKeyMap.TryGetUsage(0xC0, 0x27, false, out var enye);
            Assert.Equal(0x64, leftOfOne);
            Assert.Equal(0x35, leftOfZ);
            Assert.Equal(0x33, enye);
        }
        finally
        {
            VirtualKeyMap.SwapIsoKeys = previous;
        }
    }

    [Fact]
    public void AltGr_sends_right_alt_without_the_phantom_left_control()
    {
        using var capture = new InputCapture();
        KeyModifiers lastModifiers = default;
        byte[] lastUsages = [];
        capture.KeyboardReport += (modifiers, usages) =>
        {
            lastModifiers = modifiers;
            lastUsages = usages;
        };

        // AltGr+2 on Spanish (@): Windows emits phantom LCtrl (0x21D), then Right Alt, then 2.
        Assert.True(capture.HandleKeyboardEvent(0xA2, true, 0x21D));
        capture.HandleKeyboardEvent(0xA5, true, 0x38, extended: true);
        capture.HandleKeyboardEvent(0x32, true, 0x03);

        Assert.Equal(KeyModifiers.RightAlt, lastModifiers);
        Assert.Equal(new byte[] { 0x1F }, lastUsages);
    }

    [Fact]
    public void A_real_left_control_is_still_forwarded()
    {
        using var capture = new InputCapture();
        KeyModifiers lastModifiers = default;
        capture.KeyboardReport += (modifiers, _) => lastModifiers = modifiers;

        capture.HandleKeyboardEvent(0xA2, true, 0x1D);

        Assert.Equal(KeyModifiers.LeftControl, lastModifiers);
    }

    [Fact]
    public void The_phantom_control_passes_through_to_windows_when_not_capturing()
    {
        using var capture = new InputCapture();
        capture.SetPassThrough(true);

        Assert.False(capture.HandleKeyboardEvent(0xA2, true, 0x21D));
    }
}
