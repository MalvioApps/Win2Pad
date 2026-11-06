using System.Net;
using System.Net.Sockets;
using System.Text;
using Win2Pad.Core.Calibration;
using Win2Pad.Core.Typing;

namespace Win2Pad.Core.Clipboard;

/// <summary>
/// Hands the PC clipboard to an iOS Shortcut on the tablet over the local network. The Shortcut
/// fetches <c>/clipboard</c>, puts what it gets on the iPad's clipboard, then calls <c>/done</c>;
/// Win2Pad then presses ⌘V on the iPad. <c>/setup</c> is a page for Safari with the addresses to
/// put in the Shortcut. Every request needs the key. Plain TCP, so no administrator rights.
/// </summary>
public sealed class ClipboardServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly string _key;
    private readonly object _doneLock = new();
    private TaskCompletionSource? _done;

    private ClipboardServer(TcpListener listener, string host, int port, string key)
    {
        _listener = listener;
        _key = key;
        BaseUrl = $"http://{host}:{port}";
        _ = AcceptLoopAsync();
    }

    /// <summary>The running server, or null when clipboard sharing is off.</summary>
    public static ClipboardServer? Current { get; private set; }

    /// <summary>Raised when the server starts, stops or fails to start.</summary>
    public static event Action? CurrentChanged;

    /// <summary>Why the server is not running although it is enabled, if so.</summary>
    public static string? StartError { get; private set; }

    public string BaseUrl { get; }
    public string ClipboardUrl => $"{BaseUrl}/clipboard?k={_key}";
    public string DoneUrl => $"{BaseUrl}/done?k={_key}";
    public string SetupUrl => $"{BaseUrl}/setup?k={_key}";

    /// <summary>When the tablet last fetched the clipboard, and what it got.</summary>
    public (DateTime At, string What)? LastFetch { get; private set; }

    /// <summary>Raised from a server thread whenever the tablet fetches the clipboard.</summary>
    public event Action<string>? Fetched;

    private static readonly SemaphoreSlim ApplyLock = new(1, 1);

    /// <summary>Starts, restarts or stops the server to match the options.</summary>
    public static async Task ApplyAsync(ClipboardOptions options)
    {
        await ApplyLock.WaitAsync();
        try
        {
            if (Current is { } running)
            {
                Current = null;
                await running.DisposeAsync();
            }
            StartError = null;

            if (options.Enabled)
            {
                var address = CalibrationServer.LanAddress();
                if (address is null)
                {
                    StartError = "This PC has no network connection the iPad could reach.";
                }
                else
                {
                    var listener = new TcpListener(IPAddress.Any, options.Port);
                    try
                    {
                        listener.Start();
                        Current = new ClipboardServer(listener, address.ToString(), options.Port, options.Key);
                    }
                    catch (SocketException ex)
                    {
                        listener.Stop();
                        StartError = $"Port {options.Port} is in use ({ex.SocketErrorCode}).";
                    }
                }
            }
        }
        finally
        {
            ApplyLock.Release();
        }
        CurrentChanged?.Invoke();
    }

    /// <summary>
    /// Starts waiting for the Shortcut's <c>/done</c>. Call it before triggering the Shortcut;
    /// the task is true when it arrives in time.
    /// </summary>
    public async Task<bool> WaitForDoneAsync(TimeSpan timeout)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_doneLock) _done = done;
        var finished = await Task.WhenAny(done.Task, Task.Delay(timeout));
        lock (_doneLock)
        {
            if (ReferenceEquals(_done, done)) _done = null;
        }
        return finished == done.Task;
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }
            _ = HandleAsync(client);
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using var _ = client;
        try
        {
            var stream = client.GetStream();
            var (path, query) = await ReadRequestAsync(stream) ?? default;
            if (path is null) return;

            if (!string.Equals(query.GetValueOrDefault("k"), _key, StringComparison.Ordinal))
            {
                await WriteAsync(stream, 403, "text/plain", "Forbidden"u8.ToArray());
                return;
            }

            switch (path)
            {
                case "/clipboard":
                    await ServeClipboardAsync(stream);
                    break;
                case "/done":
                    lock (_doneLock) _done?.TrySetResult();
                    await WriteAsync(stream, 200, "text/plain", "OK"u8.ToArray());
                    break;
                case "/setup" or "/":
                    await WriteAsync(stream, 200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(SetupPage()));
                    break;
                default:
                    await WriteAsync(stream, 404, "text/plain", "Not found"u8.ToArray());
                    break;
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException) { }
    }

    private async Task ServeClipboardAsync(NetworkStream stream)
    {
        // Text first: copying from most apps puts text and a picture of it; the text is what you want.
        if (WindowsClipboard.HasText && WindowsClipboard.ReadText() is { Length: > 0 } text)
        {
            Record($"text, {text.Length} characters");
            await WriteAsync(stream, 200, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(text));
            return;
        }

        if (WindowsClipboard.HasImage && await WindowsClipboard.ReadImagePngAsync() is { } png)
        {
            Record($"image, {png.Length / 1024} KB");
            await WriteAsync(stream, 200, "image/png", png);
            return;
        }

        Record("nothing to paste");
        await WriteAsync(stream, 204, "text/plain", []);
    }

    private void Record(string what)
    {
        LastFetch = (DateTime.Now, what);
        Fetched?.Invoke(what);
    }

    private static async Task<(string Path, Dictionary<string, string> Query)?> ReadRequestAsync(NetworkStream stream)
    {
        var bytes = new List<byte>(1024);
        var one = new byte[1];
        while (bytes.Count < 16 * 1024)
        {
            if (await stream.ReadAsync(one) == 0) return null;
            bytes.Add(one[0]);
            var n = bytes.Count;
            if (n >= 4 && bytes[n - 4] == '\r' && bytes[n - 3] == '\n' && bytes[n - 2] == '\r' && bytes[n - 1] == '\n') break;
        }

        var first = Encoding.ASCII.GetString(bytes.ToArray()).Split("\r\n")[0].Split(' ');
        if (first.Length < 2 || first[0] is not ("GET" or "POST" or "HEAD")) return null;
        return ParseTarget(first[1]);
    }

    internal static (string Path, Dictionary<string, string> Query) ParseTarget(string target)
    {
        var questionMark = target.IndexOf('?');
        var path = questionMark < 0 ? target : target[..questionMark];
        var query = new Dictionary<string, string>(StringComparer.Ordinal);
        if (questionMark >= 0)
        {
            foreach (var pair in target[(questionMark + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = pair.IndexOf('=');
                if (eq > 0) query[pair[..eq]] = Uri.UnescapeDataString(pair[(eq + 1)..]);
            }
        }
        return (path, query);
    }

    private static async Task WriteAsync(NetworkStream stream, int status, string contentType, byte[] body)
    {
        var reason = status switch { 200 => "OK", 204 => "No Content", 403 => "Forbidden", _ => "Not Found" };
        var head = $"HTTP/1.1 {status} {reason}\r\nContent-Type: {contentType}\r\nContent-Length: {body.Length}\r\n"
                   + "Cache-Control: no-store\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
        if (body.Length > 0) await stream.WriteAsync(body);
    }

    private string SetupPage() => $$"""
        <!doctype html>
        <html lang="en"><head><meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>Win2Pad clipboard</title>
        <style>
          body { font: 17px -apple-system, system-ui, sans-serif; max-width: 640px; margin: 24px auto; padding: 0 16px; color: #1c1c1e; background: #f2f2f7; }
          h1 { font-size: 26px; } li { margin: 0 0 14px; line-height: 1.4; }
          .url { display: flex; gap: 8px; margin: 6px 0; }
          .url input { flex: 1; font: 14px ui-monospace, monospace; padding: 8px; border: 1px solid #c7c7cc; border-radius: 8px; }
          button { font: inherit; padding: 8px 14px; border: 0; border-radius: 8px; background: #0a84ff; color: #fff; }
          code { background: #e5e5ea; padding: 1px 5px; border-radius: 5px; }
          @media (prefers-color-scheme: dark) { body { background: #000; color: #f2f2f7; } .url input { background: #1c1c1e; color: #f2f2f7; border-color: #3a3a3c; } code { background: #2c2c2e; } }
        </style></head><body>
        <h1>Paste from the PC</h1>
        <p>Make this Shortcut once. Afterwards <b>Ctrl+V</b> on the iPad pastes what you copied on the PC, text or images.</p>
        <ol>
          <li>Open <b>Shortcuts</b>, tap <b>+</b> and name it <code>Paste from PC</code>.</li>
          <li>Add <b>Get Contents of URL</b> with this address:
            <div class="url"><input id="u1" readonly value="{{ClipboardUrl}}"><button onclick="copy('u1', this)">Copy</button></div></li>
          <li>Add <b>Copy to Clipboard</b> (it takes the Contents of URL).</li>
          <li>Add another <b>Get Contents of URL</b> with this address:
            <div class="url"><input id="u2" readonly value="{{DoneUrl}}"><button onclick="copy('u2', this)">Copy</button></div></li>
          <li>Go to <b>Settings → Accessibility → Keyboards → Full Keyboard Access</b>, turn it on, open <b>Commands</b>,
            find <code>Paste from PC</code> under Shortcuts and press <b>Control + Option + Command + V</b> on the PC's keyboard
            (Ctrl + Alt + Windows + V).</li>
          <li>Run the Shortcut once by hand and allow it to connect to this PC when asked.</li>
        </ol>
        <script>
          function copy(id, button) {
            const input = document.getElementById(id);
            input.focus(); input.setSelectionRange(0, input.value.length);
            let ok = false;
            try { ok = document.execCommand('copy'); } catch (e) {}
            if (!ok && navigator.clipboard) navigator.clipboard.writeText(input.value).then(() => {}, () => {});
            button.textContent = 'Copied'; setTimeout(() => button.textContent = 'Copy', 1500);
          }
        </script>
        </body></html>
        """;

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        _stop.Dispose();
    }
}
