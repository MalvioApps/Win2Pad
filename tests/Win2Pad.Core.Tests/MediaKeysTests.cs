using Win2Pad.Core;
using Xunit;

namespace Win2Pad.Core.Tests;

/// <summary>
/// Media keys go out as Consumer Control (report 3), never as keyboard usages.
/// </summary>
public sealed class MediaKeysTests
{
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
}
