using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ModbusServer;

/// <summary>Přenáší UTF-8 proud bez aplikační hlavičky nebo oddělovače zpráv.</summary>
public sealed class TcpTextTransport : IDisposable
{
    private const int MaxClients = 32;
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(5);
    private readonly HashSet<TcpClient> _clients = [];
    private readonly object _gate = new();
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private TcpListener? _listener;
    private CancellationTokenSource? _stop;

    public event Action<string>? Status;
    public event Action<string>? Log;
    public event Action<string, string>? Received;
    public bool IsRunning => _stop is not null;
    public int Port => (_listener?.LocalEndpoint as IPEndPoint)?.Port ?? 0;
    public int ClientCount { get { lock (_gate) return _clients.Count; } }

    public void StartServer(IPAddress address, int port)
    {
        if (_stop is not null) throw new InvalidOperationException("Komunikace již běží.");
        var listener = new TcpListener(address, port);
        listener.Start();
        _stop = new CancellationTokenSource();
        _listener = listener;
        _ = AcceptLoop(listener, _stop.Token);
        Status?.Invoke($"Naslouchá na {address}:{((IPEndPoint)listener.LocalEndpoint).Port}");
    }

    public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        if (_stop is not null) throw new InvalidOperationException("Komunikace již běží.");
        var client = new TcpClient();
        var stop = new CancellationTokenSource();
        _stop = stop;
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stop.Token);
            await client.ConnectAsync(host, port, linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            lock (_gate) _clients.Add(client);
            Status?.Invoke($"Připojeno k {host}:{port}");
            _ = ReceiveLoop(client, client.Client.RemoteEndPoint?.ToString() ?? host, stop.Token, serverSide: false);
        }
        catch
        {
            client.Dispose();
            if (ReferenceEquals(_stop, stop))
            {
                _stop = null;
                stop.Dispose();
            }
            throw;
        }
    }

    public void Stop()
    {
        if (_stop is null) return;
        _stop.Cancel();
        _listener?.Stop();
        _listener = null;
        lock (_gate)
        {
            foreach (var client in _clients) client.Dispose();
            _clients.Clear();
        }
        _stop.Dispose();
        _stop = null;
        Status?.Invoke("Zastaveno");
    }

    public async Task SendAsync(string message, CancellationToken cancellationToken)
    {
        if (message.Length == 0) throw new ArgumentException("Zadejte text zprávy.");
        TcpClient[] clients;
        lock (_gate) clients = _clients.ToArray();
        if (clients.Length == 0) throw new InvalidOperationException("Není připojený žádný klient.");

        var bytes = Encoding.UTF8.GetBytes(message);
        await _sendGate.WaitAsync(cancellationToken);
        try
        {
            var sent = 0;
            foreach (var client in clients)
            {
                try
                {
                    await client.GetStream().WriteAsync(bytes, cancellationToken);
                    sent++;
                }
                catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
                {
                    Log?.Invoke($"Odeslání klientovi selhalo: {ex.Message}");
                    client.Dispose();
                }
            }
            if (sent == 0) throw new IOException("Zprávu se nepodařilo odeslat.");
            Log?.Invoke($"Odesláno {bytes.Length} B ({sent} klientů): {Preview(message)}");
        }
        finally { _sendGate.Release(); }
    }

    private async Task AcceptLoop(TcpListener listener, CancellationToken stop)
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(stop);
                bool admitted;
                lock (_gate)
                {
                    admitted = _clients.Count < MaxClients;
                    if (admitted) _clients.Add(client);
                }
                if (!admitted)
                {
                    client.Dispose();
                    Log?.Invoke($"Odmítnut klient: limit {MaxClients} spojení.");
                    continue;
                }
                var peer = client.Client.RemoteEndPoint?.ToString() ?? "neznámý klient";
                Log?.Invoke($"Klient {peer} připojen.");
                _ = ReceiveLoop(client, peer, stop, serverSide: true);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (SocketException) when (stop.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Stop();
            Status?.Invoke($"Chyba TCP serveru: {ex.Message}");
        }
    }

    private async Task ReceiveLoop(TcpClient client, string peer, CancellationToken stop, bool serverSide)
    {
        try
        {
            var stream = client.GetStream();
            var decoder = Encoding.UTF8.GetDecoder();
            var bytes = new byte[1024];
            var chars = new char[1024];
            while (!stop.IsCancellationRequested)
            {
                using var idle = serverSide ? CancellationTokenSource.CreateLinkedTokenSource(stop) : null;
                idle?.CancelAfter(IdleTimeout);
                var count = await stream.ReadAsync(bytes, idle?.Token ?? stop);
                if (count == 0) break;
                // Decoder drží neúplný UTF-8 znak mezi dvěma TCP čteními.
                var length = decoder.GetChars(bytes, 0, count, chars, 0, false);
                if (length == 0) continue;
                var text = new string(chars, 0, length);
                Received?.Invoke(peer, text);
                Log?.Invoke($"Přijato od {peer}: {Preview(text)}");
            }
        }
        catch (OperationCanceledException) when (serverSide && !stop.IsCancellationRequested)
        {
            Log?.Invoke($"Klient {peer} odpojen po nečinnosti.");
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        catch (Exception ex) { Log?.Invoke($"Chyba klienta {peer}: {ex.Message}"); }
        finally
        {
            lock (_gate) _clients.Remove(client);
            client.Dispose();
            Log?.Invoke($"Klient {peer} odpojen.");
            if (!serverSide && !stop.IsCancellationRequested) Stop();
        }
    }

    private static string Preview(string text) => text.Replace("\r", "\\r").Replace("\n", "\\n");

    public void Dispose()
    {
        Stop();
    }
}
