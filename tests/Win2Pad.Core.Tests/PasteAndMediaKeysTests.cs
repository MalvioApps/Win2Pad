using Win2Pad.Core;
using Win2Pad.Core.Typing;
using Xunit;

namespace Win2Pad.Core.Tests;

/// <summary>
/// 0.2: Ctrl+V on the tablet types the PC clipboard, and media keys go out as Consumer Control.
/// </summary>
public sealed class PasteAndMediaKeysTests
{
    private const int LeftControl = 0xA2;
    private const int LeftAlt = 0xA4;
    private const int LeftWin = 0x5B;
    private const int V = 0x56;
    private const int C = 0x43;
    private const int X = 0x58;

    /// <summary>A small Spanish-like layout: letters, digits, Shift symbols, AltGr @ and a dead acute.</summary>
    private sealed class FakeLayout : IKeyLayout
    {
        public bool TryGetKey(char c, out KeyStroke stroke)
        {
            stroke = default;
            if (c is >= 'a' and <= 'z') { stroke = new((byte)(0x04 + c - 'a'), KeyModifiers.None); return true; }
            if (c is >= 'A' and <= 'Z') { stroke = new((byte)(0x04 + c - 'A'), KeyModifiers.LeftShift); return true; }
            switch (c)
            {
                case ' ': stroke = new(0x2C, KeyModifiers.None); return true;
                case '1': stroke = new(0x1E, KeyModifiers.None); return true;
                case '!': stroke = new(0x1E, KeyModifiers.LeftShift); return true;
                case '@': stroke = new(0x1F, KeyModifiers.RightAlt); return true;
                case '´': stroke = new(0x34, KeyModifiers.None); return true;
                case '¨': stroke = new(0x34, KeyModifiers.LeftShift); return true;
                case 'ñ': stroke = new(0x33, KeyModifiers.None); return true;
                default: return false;
            }
        }
    }

    private static IReadOnlyList<KeyStroke> Strokes(string text, out int skipped)
    {
        var (strokes, s) = TextTyper.ToStrokes(text, new FakeLayout());
        skipped = s;
        return strokes;
    }

    [Fact]
    public void Plain_letters_and_capitals_use_shift()
    {
        var strokes = Strokes("aB", out var skipped);

        Assert.Equal(0, skipped);
        Assert.Equal([new KeyStroke(0x04, KeyModifiers.None), new KeyStroke(0x05, KeyModifiers.LeftShift)], strokes);
    }

    [Fact]
    public void Altgr_characters_keep_right_alt()
    {
        var strokes = Strokes("@", out _);

        Assert.Equal([new KeyStroke(0x1F, KeyModifiers.RightAlt)], strokes);
    }

    [Fact]
    public void A_key_of_its_own_wins_over_a_dead_key()
    {
        var strokes = Strokes("ñ", out _);

        Assert.Equal([new KeyStroke(0x33, KeyModifiers.None)], strokes);
    }

    [Theory]
    [InlineData("á", 0x04, false)]
    [InlineData("É", 0x08, true)]
    [InlineData("ü", 0x18, false)]
    public void Accented_letters_go_through_the_dead_key(string text, byte letter, bool shifted)
    {
        var strokes = Strokes(text, out var skipped);

        Assert.Equal(0, skipped);
        Assert.Equal(2, strokes.Count);
        Assert.Equal(0x34, strokes[0].Usage);
        Assert.Equal(new KeyStroke(letter, shifted ? KeyModifiers.LeftShift : KeyModifiers.None), strokes[1]);
    }

    [Theory]
    [InlineData("a\r\nb")]
    [InlineData("a\nb")]
    [InlineData("a\rb")]
    public void Every_kind_of_line_break_is_one_enter(string text)
    {
        var strokes = Strokes(text, out _);

        Assert.Equal([0x04, 0x28, 0x05], strokes.Select(s => s.Usage));
    }

    [Fact]
    public void Tab_is_the_tab_key()
    {
        Assert.Equal([0x2B], Strokes("\t", out _).Select(s => s.Usage));
    }

    [Fact]
    public void Characters_the_layout_cannot_type_are_skipped_and_counted_once()
    {
        var strokes = Strokes("a😀b€", out var skipped);

        Assert.Equal([0x04, 0x05], strokes.Select(s => s.Usage));
        Assert.Equal(2, skipped);
    }

    [Theory]
    [InlineData(0xAD, ConsumerKeyMap.Mute)]
    [InlineData(0xAE, ConsumerKeyMap.VolumeDown)]
    [InlineData(0xAF, ConsumerKeyMap.VolumeUp)]
    [InlineData(0xB0, ConsumerKeyMap.NextTrack)]
    [InlineData(0xB1, ConsumerKeyMap.PreviousTrack)]
    [InlineData(0xB2, ConsumerKeyMap.Stop)]
    [InlineData(0xB3, ConsumerKeyMap.PlayPause)]
    [InlineData(0xAC, ConsumerKeyMap.Home)]
    [InlineData(0xAA, ConsumerKeyMap.Search)]
    public void Media_keys_map_to_consumer_usages(int virtualKey, ushort expected)
    {
        Assert.True(ConsumerKeyMap.TryGetUsage(virtualKey, out var usage));
        Assert.Equal(expected, usage);
    }

    [Fact]
    public void Ordinary_keys_are_not_media_keys()
    {
        Assert.False(ConsumerKeyMap.TryGetUsage(0x41, out _));
    }

    [Fact]
    public void Consumer_report_is_the_usage_little_endian()
    {
        Assert.Equal(new byte[] { 0x23, 0x02 }, HidReports.Consumer(ConsumerKeyMap.Home));
        Assert.Equal(new byte[] { 0x00, 0x00 }, HidReports.Consumer(0));
        Assert.Equal(HidReports.ConsumerReportLength, HidReports.Consumer(ConsumerKeyMap.Mute).Length);
    }

    [Fact]
    public void Report_map_declares_a_consumer_collection_with_report_id_3()
    {
        var map = HidDescriptors.ReportMapValue;
        var consumerPage = IndexOf(map, [0x05, 0x0C, 0x09, 0x01, 0xA1, 0x01, 0x85, HidDescriptors.ConsumerReportId]);

        Assert.True(consumerPage >= 0);
        Assert.Equal(3, HidDescriptors.ConsumerReportId);
        // 16-bit report size, one field: matches the two-byte report.
        Assert.True(IndexOf(map, [0x75, 0x10, 0x95, 0x01, 0x81, 0x00]) > consumerPage);
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i + needle.Length <= haystack.Length; i++)
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle)) return i;
        return -1;
    }

    [Fact]
    public void Media_keys_while_capturing_go_out_as_consumer_reports_and_are_swallowed()
    {
        using var capture = new InputCapture();
        capture.SetPassThrough(false);
        var consumer = new List<ushort>();
        var keyboard = 0;
        capture.ConsumerReport += consumer.Add;
        capture.KeyboardReport += (_, _) => keyboard++;

        Assert.True(capture.HandleKeyboardEvent(0xAF, true));
        Assert.True(capture.HandleKeyboardEvent(0xAF, false));

        Assert.Equal([ConsumerKeyMap.VolumeUp, (ushort)0], consumer);
        Assert.Equal(0, keyboard);
    }

    [Fact]
    public void Media_keys_in_pass_through_stay_on_the_pc()
    {
        using var capture = new InputCapture();
        capture.SetPassThrough(true);
        var consumer = 0;
        capture.ConsumerReport += _ => consumer++;

        Assert.False(capture.HandleKeyboardEvent(0xAF, true));
        Assert.False(capture.HandleKeyboardEvent(0xAF, false));
        Assert.Equal(0, consumer);
    }

    [Fact]
    public void Ctrl_v_taken_over_sends_no_v_down_or_up()
    {
        using var capture = new InputCapture();
        capture.SetPassThrough(false);
        var asked = 0;
        capture.PasteRequested = () => { asked++; return true; };
        var reports = new List<byte[]>();
        capture.KeyboardReport += (_, usages) => reports.Add(usages);

        capture.HandleKeyboardEvent(LeftControl, true);
        Assert.True(capture.HandleKeyboardEvent(V, true));
        Assert.True(capture.HandleKeyboardEvent(V, false));
        capture.HandleKeyboardEvent(LeftControl, false);

        Assert.Equal(1, asked);
        Assert.DoesNotContain(reports, usages => usages.Contains((byte)0x19)); // V
    }

    [Fact]
    public void Ctrl_v_declined_reaches_the_tablet()
    {
        using var capture = new InputCapture();
        capture.SetPassThrough(false);
        capture.PasteRequested = () => false;
        var reports = new List<byte[]>();
        capture.KeyboardReport += (_, usages) => reports.Add(usages);

        capture.HandleKeyboardEvent(LeftControl, true);
        capture.HandleKeyboardEvent(V, true);

        Assert.Contains(reports, usages => usages.Contains((byte)0x19));
    }

    [Theory]
    [InlineData(LeftAlt)]
    [InlineData(LeftWin)]
    public void Ctrl_v_with_other_modifiers_is_not_a_paste(int other)
    {
        using var capture = new InputCapture();
        capture.SetPassThrough(false);
        var asked = 0;
        capture.PasteRequested = () => { asked++; return true; };

        capture.HandleKeyboardEvent(LeftControl, true);
        capture.HandleKeyboardEvent(other, true);
        capture.HandleKeyboardEvent(V, true);

        Assert.Equal(0, asked);
    }

    [Fact]
    public void Plain_v_is_not_a_paste()
    {
        using var capture = new InputCapture();
        capture.SetPassThrough(false);
        var asked = 0;
        capture.PasteRequested = () => { asked++; return true; };

        capture.HandleKeyboardEvent(V, true);

        Assert.Equal(0, asked);
    }

    [Theory]
    [InlineData(C)]
    [InlineData(X)]
    public void Copy_or_cut_on_the_tablet_is_reported(int key)
    {
        using var capture = new InputCapture();
        capture.SetPassThrough(false);
        var copies = 0;
        capture.CopiedOnTablet += () => copies++;

        capture.HandleKeyboardEvent(LeftControl, true);
        capture.HandleKeyboardEvent(key, true);
        capture.HandleKeyboardEvent(key, false);
        capture.HandleKeyboardEvent(LeftControl, false);
        capture.HandleKeyboardEvent(key, true); // the key alone, Ctrl released, does not count
        capture.HandleKeyboardEvent(key, false);

        Assert.Equal(1, copies);
    }

    [Fact]
    public void Copy_on_the_pc_is_not_reported_as_a_tablet_copy()
    {
        using var capture = new InputCapture();
        capture.SetPassThrough(true);
        var copies = 0;
        capture.CopiedOnTablet += () => copies++;

        capture.HandleKeyboardEvent(LeftControl, true);
        capture.HandleKeyboardEvent(C, true);

        Assert.Equal(0, copies);
    }
}
