using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Win2Pad.Core.Calibration;

/// <summary>
/// A small LAN web server for the calibration page: it serves the page to Safari on the tablet
/// and receives the pointer positions the page sees over a WebSocket. Built on a plain TCP
/// listener, so it needs no URL reservation or administrator rights. A random key in the URL
/// keeps other devices on the network out. Runs only while calibrating.
/// </summary>
public sealed class CalibrationServer : IPointerFeed, IAsyncDisposable
{
    private const string WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
    private const int FirstPort = 47800;

    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly string _key = Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
    private readonly object _socketLock = new();
    private WebSocket? _socket;
    private long _sequence;
    private PointerSample? _latest;
    private PageInfo? _page;

    private CalibrationServer(TcpListener listener, IPAddress address)
    {
        _listener = listener;
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Url = $"http://{address}:{port}/?k={_key}";
        _ = AcceptLoopAsync();
    }

    /// <summary>The address to open on the tablet; it must be on the same network as this PC.</summary>
    public string Url { get; }

    public PageInfo? Page => Volatile.Read(ref _page);

    public PointerSample? Latest
    {
        get { lock (_socketLock) return _latest; }
    }

    public bool PageConnected
    {
        get { lock (_socketLock) return _socket is { State: WebSocketState.Open } && _page is not null; }
    }

    /// <summary>Raised from a server thread when a page connects, reports its size or goes away.</summary>
    public event Action? PageChanged;

    public static CalibrationServer Start()
    {
        var address = LanAddress() ?? throw new InvalidOperationException(
            "This PC has no network connection the tablet could reach. Connect both to the same Wi-Fi.");

        for (var port = FirstPort; port < FirstPort + 10; port++)
        {
            var listener = new TcpListener(IPAddress.Any, port);
            try
            {
                listener.Start();
                return new CalibrationServer(listener, address);
            }
            catch (SocketException)
            {
                listener.Stop();
            }
        }

        var any = new TcpListener(IPAddress.Any, 0);
        any.Start();
        return new CalibrationServer(any, address);
    }

    /// <summary>Shows progress on the tablet's page.</summary>
    public async Task SendStatusAsync(string text, double progress, string state = "running")
    {
        WebSocket? socket;
        lock (_socketLock) socket = _socket;
        if (socket is not { State: WebSocketState.Open }) return;

        var json = JsonSerializer.SerializeToUtf8Bytes(new { type = "status", text, progress, state });
        try
        {
            await socket.SendAsync(json, WebSocketMessageType.Text, true, _stop.Token);
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException) { }
    }

    /// <summary>The IPv4 address of the interface with a default gateway: the one the tablet shares.</summary>
    internal static IPAddress? LanAddress()
    {
        var candidates =
            from nic in NetworkInterface.GetAllNetworkInterfaces()
            where nic.OperationalStatus == OperationalStatus.Up
                  && nic.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            let props = nic.GetIPProperties()
            where props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork
                                                  && !g.Address.Equals(IPAddress.Any))
            from unicast in props.UnicastAddresses
            where unicast.Address.AddressFamily == AddressFamily.InterNetwork
            // Wi-Fi first: a tablet is far more likely to be on the wireless network.
            orderby nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? 0 : 1
            select unicast.Address;
        return candidates.FirstOrDefault();
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
            var request = await ReadHeadersAsync(stream);
            if (request is null) return;

            var (path, query, headers) = request.Value;
            if (!string.Equals(query.GetValueOrDefault("k"), _key, StringComparison.Ordinal))
            {
                await WriteAsync(stream, 403, "text/plain", "Forbidden"u8.ToArray());
                return;
            }

            if (path == "/ws" && headers.TryGetValue("sec-websocket-key", out var key))
            {
                await AcceptWebSocketAsync(stream, key);
                return;
            }

            if (path == "/")
            {
                await WriteAsync(stream, 200, "text/html; charset=utf-8", PageHtml.Value);
                return;
            }

            await WriteAsync(stream, 404, "text/plain", "Not found"u8.ToArray());
        }
        catch (Exception ex) when (ex is IOException or SocketException or WebSocketException
                                       or OperationCanceledException or ObjectDisposedException) { }
    }

    private async Task AcceptWebSocketAsync(NetworkStream stream, string key)
    {
        var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key.Trim() + WebSocketGuid)));
        var response = "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n"
                       + $"Sec-WebSocket-Accept: {accept}\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(response), _stop.Token);

        var socket = WebSocket.CreateFromStream(stream, new WebSocketCreationOptions
        {
            IsServer = true,
            KeepAliveInterval = TimeSpan.FromSeconds(15)
        });

        // One page at a time: a reload or a second tab replaces the previous one.
        WebSocket? previous;
        lock (_socketLock)
        {
            previous = _socket;
            _socket = socket;
        }
        previous?.Abort();

        var buffer = new byte[4096];
        using var message = new MemoryStream();
        try
        {
            while (socket.State == WebSocketState.Open && !_stop.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(buffer, _stop.Token);
                if (result.MessageType == WebSocketMessageType.Close) break;
                message.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage) continue;
                if (message.Length <= 64 * 1024) HandleMessage(message.ToArray());
                message.SetLength(0);
            }
        }
        finally
        {
            lock (_socketLock)
            {
                if (ReferenceEquals(_socket, socket)) _socket = null;
            }
            socket.Dispose();
            PageChanged?.Invoke();
        }
    }

    internal void HandleMessage(byte[] json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            switch (root.GetProperty("type").GetString())
            {
                case "hello":
                    Volatile.Write(ref _page, new PageInfo(
                        root.GetProperty("sw").GetInt32(), root.GetProperty("sh").GetInt32(),
                        root.GetProperty("vw").GetInt32(), root.GetProperty("vh").GetInt32()));
                    PageChanged?.Invoke();
                    break;
                case "move":
                    var sample = new PointerSample(0, root.GetProperty("x").GetDouble(), root.GetProperty("y").GetDouble());
                    lock (_socketLock) _latest = sample with { Sequence = ++_sequence };
                    break;
            }
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException) { }
    }

    private static async Task<(string Path, Dictionary<string, string> Query, Dictionary<string, string> Headers)?>
        ReadHeadersAsync(NetworkStream stream)
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

        var lines = Encoding.ASCII.GetString(bytes.ToArray()).Split("\r\n");
        var parts = lines[0].Split(' ');
        if (parts.Length < 2 || parts[0] != "GET") return null;

        var target = parts[1];
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

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':');
            if (colon > 0) headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }
        return (path, query, headers);
    }

    private static async Task WriteAsync(NetworkStream stream, int status, string contentType, byte[] body)
    {
        var reason = status switch { 200 => "OK", 403 => "Forbidden", _ => "Not Found" };
        var head = $"HTTP/1.1 {status} {reason}\r\nContent-Type: {contentType}\r\nContent-Length: {body.Length}\r\n"
                   + "Cache-Control: no-store\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
        await stream.WriteAsync(body);
    }

    private static readonly Lazy<byte[]> PageHtml = new(() =>
    {
        using var resource = typeof(CalibrationServer).Assembly
            .GetManifestResourceStream("Win2Pad.Core.Calibration.calibration.html")!;
        using var copy = new MemoryStream();
        resource.CopyTo(copy);
        return copy.ToArray();
    });

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        WebSocket? socket;
        lock (_socketLock)
        {
            socket = _socket;
            _socket = null;
        }
        socket?.Abort();
        _stop.Dispose();
    }
}
