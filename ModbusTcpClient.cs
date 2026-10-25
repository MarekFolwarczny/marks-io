using System.Net.Sockets;

namespace ModbusServer;

/// <summary>Trvalé Modbus TCP spojení; požadavky serializuje a páruje podle ID transakce.</summary>
public sealed class ModbusTcpClient : IDisposable
{
    private readonly SemaphoreSlim _requests = new(1, 1);
    private TcpClient? _client;
    private NetworkStream? _stream;
    private ushort _transaction;

    public event Action<string>? Status;
    public bool IsConnected
    {
        get
        {
            if (_stream is null || _client is null) return false;
            try { return !(_client.Client.Poll(0, SelectMode.SelectRead) && _client.Available == 0); }
            catch (SocketException) { return false; }
            catch (ObjectDisposedException) { return false; }
        }
    }

    public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        if (IsConnected) throw new InvalidOperationException("Klient je již připojen.");
        Disconnect();
        var client = new TcpClient();
        try
        {
            await client.ConnectAsync(host, port, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _stream = client.GetStream();
            _client = client;
            Status?.Invoke($"Připojeno k {host}:{port}");
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public void Disconnect()
    {
        if (_client is null) return;
        _stream = null;
        _client.Dispose();
        _client = null;
        Status?.Invoke("Odpojeno");
    }

    public async Task<ushort[]> ReadHoldingRegistersAsync(byte unitId, int address, int count, CancellationToken cancellationToken)
    {
        if (count is < 1 or > 125 || address < 0 || address + count > 65536)
            throw new ArgumentOutOfRangeException(nameof(count), "FC03 dovoluje 1 až 125 registrů v rozsahu 0 až 65535.");

        byte[] request = [3, (byte)(address >> 8), (byte)address, (byte)(count >> 8), (byte)count];
        var response = await ExchangeAsync(unitId, request, cancellationToken);
        if (response.Length != 2 + count * 2 || response[1] != count * 2)
        {
            Disconnect();
            throw new InvalidDataException("Nesprávná délka odpovědi FC03.");
        }

        var words = new ushort[count];
        for (var i = 0; i < count; i++)
            words[i] = (ushort)((response[2 + i * 2] << 8) | response[3 + i * 2]);
        return words;
    }

    public async Task WriteHoldingRegistersAsync(byte unitId, int address, ushort[] values, CancellationToken cancellationToken)
    {
        if (values.Length is < 1 or > 123 || address < 0 || address + values.Length > 65536)
            throw new ArgumentOutOfRangeException(nameof(values), "Jedna hodnota musí zabírat 1 až 123 registrů v rozsahu 0 až 65535.");

        if (values.Length == 1)
        {
            var value = values[0];
            byte[] request = [6, (byte)(address >> 8), (byte)address, (byte)(value >> 8), (byte)value];
            var response = await ExchangeAsync(unitId, request, cancellationToken);
            if (!response.AsSpan().SequenceEqual(request))
            {
                Disconnect();
                throw new InvalidDataException("Nesprávná odpověď FC06.");
            }
        }
        else
        {
            var count = values.Length;
            var request = new byte[6 + count * 2];
            request[0] = 16;
            request[1] = (byte)(address >> 8);
            request[2] = (byte)address;
            request[3] = (byte)(count >> 8);
            request[4] = (byte)count;
            request[5] = (byte)(count * 2);
            for (var i = 0; i < count; i++)
            {
                request[6 + i * 2] = (byte)(values[i] >> 8);
                request[7 + i * 2] = (byte)values[i];
            }
            var response = await ExchangeAsync(unitId, request, cancellationToken);
            if (response.Length != 5 || !response.AsSpan(1).SequenceEqual(request.AsSpan(1, 4)))
            {
                Disconnect();
                throw new InvalidDataException("Nesprávná odpověď FC16.");
            }
        }
    }

    public async Task MaskWriteRegisterAsync(byte unitId, int address, int bit, bool value, CancellationToken cancellationToken)
    {
        if (address is < 0 or > 65535) throw new ArgumentOutOfRangeException(nameof(address));
        if (bit is < 0 or > 15) throw new ArgumentOutOfRangeException(nameof(bit));
        var andMask = (ushort)~(1 << bit);
        var orMask = (ushort)(value ? 1 << bit : 0);
        byte[] request = [22, (byte)(address >> 8), (byte)address,
            (byte)(andMask >> 8), (byte)andMask, (byte)(orMask >> 8), (byte)orMask];
        var response = await ExchangeAsync(unitId, request, cancellationToken);
        if (!response.AsSpan().SequenceEqual(request))
        {
            Disconnect();
            throw new InvalidDataException("Nesprávná odpověď FC22.");
        }
    }

    private async Task<byte[]> ExchangeAsync(byte unitId, byte[] pdu, CancellationToken cancellationToken)
    {
        await _requests.WaitAsync(cancellationToken);
        try
        {
            var stream = _stream ?? throw new InvalidOperationException("Klient není připojen.");
            var transaction = unchecked(++_transaction);
            // MBAP: transakce, protokol 0, délka Unit ID + PDU, Unit ID.
            byte[] header = [(byte)(transaction >> 8), (byte)transaction, 0, 0,
                (byte)((pdu.Length + 1) >> 8), (byte)(pdu.Length + 1), unitId];
            await stream.WriteAsync(header, cancellationToken);
            await stream.WriteAsync(pdu, cancellationToken);

            var replyHeader = new byte[7];
            await stream.ReadExactlyAsync(replyHeader, cancellationToken);
            var length = (replyHeader[4] << 8) | replyHeader[5];
            if (replyHeader[0] != header[0] || replyHeader[1] != header[1] ||
                replyHeader[2] != 0 || replyHeader[3] != 0 || replyHeader[6] != unitId ||
                length is < 2 or > 254)
                throw new InvalidDataException("Neplatná hlavička odpovědi Modbus TCP.");

            var response = new byte[length - 1];
            await stream.ReadExactlyAsync(response, cancellationToken);
            if (response[0] == (pdu[0] | 0x80))
            {
                if (response.Length != 2) throw new InvalidDataException("Neplatná Modbus výjimka.");
                throw new InvalidOperationException($"Modbus odmítl FC{pdu[0]:D2}, chyba {response[1]}.");
            }
            if (response[0] != pdu[0]) throw new InvalidDataException("Odpověď obsahuje jinou funkci.");
            return response;
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or OperationCanceledException or InvalidDataException)
        {
            // Po neúplné odpovědi nelze bezpečně přiřadit další rámec k transakci.
            Disconnect();
            throw;
        }
        finally { _requests.Release(); }
    }

    public void Dispose()
    {
        Disconnect();
    }
}
