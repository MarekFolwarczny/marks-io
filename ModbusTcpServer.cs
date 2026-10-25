using System.Net;
using System.Net.Sockets;

namespace ModbusServer;

/// <summary>Modbus TCP server nad jednou sdílenou mapou holding registrů.</summary>
/// <remarks>FC03 čte; FC06, FC16 a FC22 zapisují. Všechna Unit ID sdílejí stejná data.</remarks>
public sealed class ModbusTcpServer
{
    private const int MaxClients = 32;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(5);
    private readonly RegisterBank _bank;
    private readonly HashSet<TcpClient> _clients = [];
    private readonly object _clientsGate = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _stop;

    // Události mohou přijít z vláken obsluhujících TCP; UI je musí převést na své vlákno.
    public event Action<string>? Status;
    public event Action<string>? Log;
    /// <summary>Skutečný naslouchací port; nula znamená zastavený server.</summary>
    public int Port => (_listener?.LocalEndpoint as IPEndPoint)?.Port ?? 0;

    public ModbusTcpServer(RegisterBank bank) => _bank = bank;

    /// <summary>Spustí naslouchání; port nula lze použít v testech pro volný port.</summary>
    public void Start(IPAddress address, int port)
    {
        if (_listener is not null) throw new InvalidOperationException("Server již běží.");
        var listener = new TcpListener(address, port);
        listener.Start();
        _listener = listener;
        _stop = new CancellationTokenSource();
        // Přijímání spojení běží na pozadí, aby UI nečekalo na klienta.
        _ = AcceptLoop(listener, _stop.Token);
        Status?.Invoke($"Naslouchá na {address}:{port}");
    }

    /// <summary>Ukončí naslouchání i aktivní klientská spojení. Opakované volání nevadí.</summary>
    public void Stop()
    {
        // Zavření spojení ukončí i čekající ReadExactlyAsync u klientů.
        _stop?.Cancel();
        _listener?.Stop();
        lock (_clientsGate)
        {
            foreach (var client in _clients) client.Close();
            _clients.Clear();
        }
        _listener = null;
        _stop?.Dispose();
        _stop = null;
        Status?.Invoke("Zastaveno");
    }

    private async Task AcceptLoop(TcpListener listener, CancellationToken stop)
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(stop);
                bool admitted;
                lock (_clientsGate)
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
                // Každý klient může poslat více požadavků po jednom spojení.
                _ = HandleClient(client, peer, stop);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (SocketException) when (stop.IsCancellationRequested) { }
        catch (Exception ex)
        {
            // Po nečekané chybě uvolníme port a spojení, aby šel server znovu spustit.
            Stop();
            Status?.Invoke($"Chyba serveru: {ex.Message}");
        }
    }

    private async Task HandleClient(TcpClient client, string peer, CancellationToken stop)
    {
        try
        {
            using (client)
            {
                var stream = client.GetStream();
                var header = new byte[7];
                while (!stop.IsCancellationRequested)
                {
                    using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(stop);
                    requestTimeout.CancelAfter(RequestTimeout);
                    // MBAP: ID transakce (2), ID protokolu (2), délka (2), Unit ID (1).
                    await stream.ReadExactlyAsync(header, requestTimeout.Token);
                    var length = (header[4] << 8) | header[5];
                    // Délka zahrnuje Unit ID a PDU; omezení brání velkým alokacím.
                    if (header[2] != 0 || header[3] != 0 || length < 2 || length > 254)
                    {
                        Log?.Invoke($"{peer}: neplatná hlavička Modbus TCP.");
                        break;
                    }

                    var request = new byte[length - 1];
                    await stream.ReadExactlyAsync(request, requestTimeout.Token);
                    var responsePdu = Process(request, peer);
                    if ((responsePdu[0] & 0x80) != 0)
                        Log?.Invoke($"{peer}: odmítnut FC{request[0]:D2}, chyba Modbus {responsePdu[1]}.");
                    var response = new byte[7 + responsePdu.Length];
                    // ID transakce i Unit ID musí v odpovědi odpovídat požadavku.
                    response[0] = header[0];
                    response[1] = header[1];
                    response[4] = (byte)((responsePdu.Length + 1) >> 8);
                    response[5] = (byte)(responsePdu.Length + 1);
                    response[6] = header[6];
                    responsePdu.AsSpan().CopyTo(response.AsSpan(7));
                    await stream.WriteAsync(response, requestTimeout.Token);
                }
            }
        }
        // Běžné odpojení klienta a zastavení serveru nejsou chyby aplikace.
        catch (EndOfStreamException) { }
        catch (IOException) { }
        catch (OperationCanceledException) when (!stop.IsCancellationRequested)
        {
            Log?.Invoke($"{peer}: vypršel časový limit požadavku.");
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (Exception ex) { Status?.Invoke($"Chyba klienta {peer}: {ex.Message}"); }
        finally
        {
            lock (_clientsGate) _clients.Remove(client);
            client.Dispose();
            Log?.Invoke($"Klient {peer} odpojen.");
        }
    }

    private byte[] Process(byte[] pdu, string peer)
    {
        var function = pdu[0];
        // Kódy výjimek: 1 nepodporovaná funkce, 2 adresa, 3 formát/hodnota.
        if (function is not (3 or 6 or 16 or 22)) return ExceptionResponse(function, 1);
        if (pdu.Length < 5) return ExceptionResponse(function, 3);
        var address = (pdu[1] << 8) | pdu[2];
        var count = (pdu[3] << 8) | pdu[4];

        if (function == 3)
        {
            // FC03 dovoluje nejvýše 125 slov, tedy 250 datových bajtů.
            if (pdu.Length != 5 || count is < 1 or > 125) return ExceptionResponse(function, 3);
            if (address + count > 65536) return ExceptionResponse(function, 2);
            var words = _bank.Read(address, count);
            var response = new byte[2 + count * 2];
            response[0] = function;
            response[1] = (byte)(count * 2);
            for (var i = 0; i < count; i++)
            {
                // Modbus posílá vyšší bajt každého 16bitového registru první.
                response[2 + i * 2] = (byte)(words[i] >> 8);
                response[3 + i * 2] = (byte)words[i];
            }
            Log?.Invoke($"{peer}: FC03 čtení HR {address}–{address + count - 1} ({count}) = {FormatValues(words)}");
            return response;
        }

        if (function == 6)
        {
            // Ve FC06 mají poslední dva bajty požadavku význam hodnoty, ne počtu.
            if (pdu.Length != 5) return ExceptionResponse(function, 3);
            _bank.Write(address, [(ushort)count]);
            Log?.Invoke($"{peer}: FC06 zápis HR {address} = 0x{count:X4}");
            return pdu;
        }

        if (function == 22)
        {
            if (pdu.Length != 7) return ExceptionResponse(function, 3);
            var orMask = (ushort)((pdu[5] << 8) | pdu[6]);
            var value = _bank.MaskWrite(address, (ushort)count, orMask);
            Log?.Invoke($"{peer}: FC22 maskovaný zápis HR {address} = 0x{value:X4}");
            return pdu;
        }

        // FC16 kontroluje počet slov, počet bajtů i skutečnou délku rámce.
        if (pdu.Length < 6 || count is < 1 or > 123 || pdu[5] != count * 2 || pdu.Length != 6 + pdu[5])
            return ExceptionResponse(function, 3);
        if (address + count > 65536) return ExceptionResponse(function, 2);
        var data = new ushort[count];
        for (var i = 0; i < count; i++) data[i] = (ushort)((pdu[6 + i * 2] << 8) | pdu[7 + i * 2]);
        _bank.Write(address, data);
        Log?.Invoke($"{peer}: FC16 zápis HR {address}–{address + count - 1} ({count}) = {FormatValues(data)}");
        return [function, pdu[1], pdu[2], pdu[3], pdu[4]];
    }

    // Náhled logu zůstává krátký i při čtení maximálního bloku registrů.
    private static string FormatValues(ushort[] values) =>
        string.Join(" ", values.Take(8).Select(value => $"0x{value:X4}")) +
        (values.Length > 8 ? $" … (+{values.Length - 8})" : "");

    private static byte[] ExceptionResponse(byte function, byte code) => [(byte)(function | 0x80), code];
}
