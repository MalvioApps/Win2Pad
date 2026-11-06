using System.Net;
using Win2Pad.Core.Clipboard;
using Win2Pad.Core.Typing;
using Xunit;

namespace Win2Pad.Core.Tests;

[Collection("ClipboardServer")]
public sealed class ClipboardServerTests
{
    private const int TestPort = 47899;

    private static async Task<ClipboardServer> StartAsync(string key)
    {
        await ClipboardServer.ApplyAsync(new ClipboardOptions { Enabled = true, Key = key, Port = TestPort });
        Assert.True(ClipboardServer.Current is not null, ClipboardServer.StartError);
        return ClipboardServer.Current!;
    }

    private static Task StopAsync() => ClipboardServer.ApplyAsync(new ClipboardOptions { Enabled = false });

    private static string Local(string url) => System.Text.RegularExpressions.Regex.Replace(url, @"//[^:/]+:", "//127.0.0.1:");

    [Fact]
    public async Task Requests_without_the_key_are_refused()
    {
        await StartAsync("0123456789abcdef");
        try
        {
            using var http = new HttpClient();
            var response = await http.GetAsync($"http://127.0.0.1:{TestPort}/clipboard?k=wrong");
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally
        {
            await StopAsync();
        }
    }

    [Fact]
    public async Task Done_ends_the_wait_and_a_missing_done_times_out()
    {
        var server = await StartAsync("0123456789abcdef");
        try
        {
            using var http = new HttpClient();
            var waiting = server.WaitForDoneAsync(TimeSpan.FromSeconds(5));
            var response = await http.GetAsync(Local(server.DoneUrl));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(await waiting);

            Assert.False(await server.WaitForDoneAsync(TimeSpan.FromMilliseconds(100)));
        }
        finally
        {
            await StopAsync();
        }
    }

    [Fact]
    public async Task Setup_page_carries_both_addresses()
    {
        var server = await StartAsync("0123456789abcdef");
        try
        {
            using var http = new HttpClient();
            var page = await http.GetStringAsync(Local(server.SetupUrl));
            Assert.Contains(server.ClipboardUrl, page);
            Assert.Contains(server.DoneUrl, page);
        }
        finally
        {
            await StopAsync();
        }
    }

    [Fact]
    public void Options_get_a_key_when_the_file_has_none()
    {
        var path = Path.Combine(Path.GetTempPath(), $"clip-{Guid.NewGuid():N}.json");
        try
        {
            var options = ClipboardOptions.Load(path);
            Assert.False(options.Enabled);
            Assert.True(options.Key.Length >= 8);
            Assert.Equal(ClipboardOptions.DefaultPort, options.Port);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_dib_becomes_a_bmp_with_the_pixel_offset_after_the_header()
    {
        // 40-byte BITMAPINFOHEADER, 32 bits per pixel, BI_RGB, 1x1 pixel.
        var dib = new byte[44];
        BitConverter.GetBytes(40).CopyTo(dib, 0);
        BitConverter.GetBytes(1).CopyTo(dib, 4);
        BitConverter.GetBytes(1).CopyTo(dib, 8);
        BitConverter.GetBytes((ushort)1).CopyTo(dib, 12);
        BitConverter.GetBytes((ushort)32).CopyTo(dib, 14);

        var bmp = WindowsClipboard.DibToBmp(dib);

        Assert.Equal((byte)'B', bmp[0]);
        Assert.Equal((byte)'M', bmp[1]);
        Assert.Equal(58, BitConverter.ToInt32(bmp, 2));
        Assert.Equal(54, BitConverter.ToInt32(bmp, 10));
    }
}
