using System.Runtime.InteropServices;
using System.Text;

namespace ModbusServer;

// Úzká P/Invoke vazba na snap7.dll; DB buffer musí být připnutý po celou dobu běhu serveru.
internal static class Snap7Native
{
    private const string Library = "snap7.dll";

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct Event
    {
        public IntPtr Time;
        public int Sender;
        public uint Code;
        public ushort ReturnCode, Param1, Param2, Param3, Param4;
    }

    [DllImport(Library)] internal static extern IntPtr Cli_Create();
    [DllImport(Library)] internal static extern void Cli_Destroy(ref IntPtr client);
    [DllImport(Library, CharSet = CharSet.Ansi)] internal static extern int Cli_ConnectTo(IntPtr client, string host, int rack, int slot);
    [DllImport(Library)] internal static extern int Cli_Disconnect(IntPtr client);
    [DllImport(Library)] internal static extern int Cli_GetConnected(IntPtr client, ref int connected);
    [DllImport(Library)] internal static extern int Cli_GetPlcStatus(IntPtr client, ref int status);
    [DllImport(Library)] internal static extern int Cli_SetParam(IntPtr client, int parameter, ref ushort value);
    [DllImport(Library)] internal static extern int Cli_DBRead(IntPtr client, int db, int start, int size, [Out] byte[] data);
    [DllImport(Library)] internal static extern int Cli_DBWrite(IntPtr client, int db, int start, int size, byte[] data);
    [DllImport(Library)] internal static extern int Cli_WriteArea(IntPtr client, int area, int db, int start, int amount, int wordLength, byte[] data);

    [DllImport(Library)] internal static extern IntPtr Srv_Create();
    [DllImport(Library)] internal static extern void Srv_Destroy(ref IntPtr server);
    [DllImport(Library)] internal static extern int Srv_SetParam(IntPtr server, int parameter, ref ushort value);
    [DllImport(Library)] internal static extern int Srv_RegisterArea(IntPtr server, int area, int index, IntPtr data, int size);
    [DllImport(Library)] internal static extern int Srv_UnregisterArea(IntPtr server, int area, int index);
    [DllImport(Library, CharSet = CharSet.Ansi)] internal static extern int Srv_StartTo(IntPtr server, string address);
    [DllImport(Library)] internal static extern int Srv_Stop(IntPtr server);
    [DllImport(Library)] internal static extern int Srv_LockArea(IntPtr server, int area, int index);
    [DllImport(Library)] internal static extern int Srv_UnlockArea(IntPtr server, int area, int index);
    [DllImport(Library)] internal static extern int Srv_PickEvent(IntPtr server, ref Event item, ref int ready);
    [DllImport(Library, CharSet = CharSet.Ansi)] internal static extern int Srv_EventText(ref Event item, StringBuilder text, int length);
}

/// <summary>Spravuje nativní Snap7 klient/server a životnost připnutého DB bufferu.</summary>
public sealed class Snap7Transport : IDisposable
{
    private const int ServerDbArea = 5;
    private readonly object _gate = new();
    private IntPtr _client, _server;
    private GCHandle _pinned;
    private byte[]? _dbData;
    private int _dbNumber;

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                if (_server != IntPtr.Zero) return true;
                if (_client == IntPtr.Zero) return false;
                var connected = 0;
                if (Snap7Native.Cli_GetConnected(_client, ref connected) == 0 && connected != 0) return true;
                StopCore();
                return false;
            }
        }
    }
    public bool IsServer { get { lock (_gate) return _server != IntPtr.Zero; } }

    public bool CheckConnection()
    {
        lock (_gate)
        {
            if (_client == IntPtr.Zero) return false;
            // GetConnected vrací uložený stav; krátký požadavek odhalí i tiché ukončení TCP spojení.
            var status = 0;
            _ = Snap7Native.Cli_GetPlcStatus(_client, ref status);
            var connected = 0;
            if (Snap7Native.Cli_GetConnected(_client, ref connected) == 0 && connected != 0) return true;
            StopCore();
            return false;
        }
    }

    public void Connect(string host, int port, int rack, int slot)
    {
        lock (_gate)
        {
            StopCore();
            _client = Snap7Native.Cli_Create();
            if (_client == IntPtr.Zero) throw new InvalidOperationException("Snap7 klienta se nepodařilo vytvořit.");
            try
            {
                var remotePort = checked((ushort)port);
                Check(Snap7Native.Cli_SetParam(_client, 2, ref remotePort), "Nastavení portu");
                Check(Snap7Native.Cli_ConnectTo(_client, host, rack, slot), "Připojení");
            }
            catch { StopCore(); throw; }
        }
    }

    public void StartServer(string host, int port, int dbNumber, int size)
    {
        lock (_gate)
        {
            StopCore();
            _server = Snap7Native.Srv_Create();
            if (_server == IntPtr.Zero) throw new InvalidOperationException("Snap7 server se nepodařilo vytvořit.");
            try
            {
                _dbNumber = dbNumber;
                _dbData = new byte[size];
                _pinned = GCHandle.Alloc(_dbData, GCHandleType.Pinned);
                var localPort = checked((ushort)port);
                Check(Snap7Native.Srv_SetParam(_server, 1, ref localPort), "Nastavení portu");
                Check(Snap7Native.Srv_RegisterArea(_server, ServerDbArea, dbNumber, _pinned.AddrOfPinnedObject(), size), "Registrace DB");
                Check(Snap7Native.Srv_StartTo(_server, host), "Spuštění serveru");
            }
            catch { StopCore(); throw; }
        }
    }

    public byte[] Read(Snap7Address address, Snap7ValueType type)
    {
        lock (_gate)
        {
            var data = new byte[Snap7Codec.Size(type)];
            if (_client != IntPtr.Zero)
                Check(Snap7Native.Cli_DBRead(_client, address.Db, address.Byte, data.Length, data), "Čtení DB");
            else if (_server != IntPtr.Zero)
            {
                ValidateLocalAddress(address, data.Length);
                WithServerArea(() => Array.Copy(_dbData!, address.Byte, data, 0, data.Length));
            }
            else throw new InvalidOperationException("Nejdříve spusťte server nebo připojte klienta.");
            return data;
        }
    }

    /// <summary>Vrátí souvislý snímek části DB, kterou server právě nabízí klientům.</summary>
    public byte[] ReadServerBytes(int offset, int count)
    {
        lock (_gate)
        {
            if (_server == IntPtr.Zero || _dbData is null)
                throw new InvalidOperationException("Snap7 server není spuštěný.");
            if (offset < 0 || count < 1 || offset > _dbData.Length - count)
                throw new ArgumentOutOfRangeException(nameof(offset), "Požadovaný rozsah je mimo DB.");
            var snapshot = new byte[count];
            WithServerArea(() => Array.Copy(_dbData, offset, snapshot, 0, count));
            return snapshot;
        }
    }

    public void Write(Snap7Address address, Snap7ValueType type, byte[] data)
    {
        if (data.Length != Snap7Codec.Size(type)) throw new ArgumentException("Nesprávná délka hodnoty.");
        lock (_gate)
        {
            if (_client != IntPtr.Zero)
            {
                // S7WLBit mění jediný bit; čtení a následný zápis celého bajtu by přepsal souběžné změny PLC.
                if (type == Snap7ValueType.Bool)
                    Check(Snap7Native.Cli_WriteArea(_client, 0x84, address.Db, address.Byte * 8 + address.Bit, 1, 0x01, data), "Zápis bitu");
                else Check(Snap7Native.Cli_DBWrite(_client, address.Db, address.Byte, data.Length, data), "Zápis DB");
            }
            else if (_server != IntPtr.Zero)
            {
                ValidateLocalAddress(address, data.Length);
                WithServerArea(() =>
                {
                    if (type == Snap7ValueType.Bool)
                    {
                        var mask = 1 << address.Bit;
                        _dbData![address.Byte] = (byte)((_dbData[address.Byte] & ~mask) | (data[0] != 0 ? mask : 0));
                    }
                    else Array.Copy(data, 0, _dbData!, address.Byte, data.Length);
                });
            }
            else throw new InvalidOperationException("Nejdříve spusťte server nebo připojte klienta.");
        }
    }

    public IReadOnlyList<string> PickEvents()
    {
        lock (_gate)
        {
            var result = new List<string>();
            if (_server == IntPtr.Zero) return result;
            // Čas na UI vlákně je omezený i při velkém provozu.
            for (var i = 0; i < 100; i++)
            {
                var item = new Snap7Native.Event();
                var ready = 0;
                Check(Snap7Native.Srv_PickEvent(_server, ref item, ref ready), "Čtení logu serveru");
                if (ready == 0) break;
                var message = new StringBuilder(1024);
                if (Snap7Native.Srv_EventText(ref item, message, message.Capacity) == 0)
                    result.Add(message.ToString());
                else result.Add($"Snap7 událost 0x{item.Code:X8}, výsledek 0x{item.ReturnCode:X4}");
            }
            return result;
        }
    }

    public void Stop() { lock (_gate) StopCore(); }
    public void Dispose() => Stop();

    private void ValidateLocalAddress(Snap7Address address, int length)
    {
        if (address.Db != _dbNumber || _dbData is null || address.Byte + length > _dbData.Length)
            throw new ArgumentException($"Server nabízí pouze DB{_dbNumber}, offset 0–{_dbData!.Length - 1}.");
    }

    private void WithServerArea(Action action)
    {
        Check(Snap7Native.Srv_LockArea(_server, ServerDbArea, _dbNumber), "Zamčení DB");
        try { action(); }
        finally { Check(Snap7Native.Srv_UnlockArea(_server, ServerDbArea, _dbNumber), "Odemčení DB"); }
    }

    private void StopCore()
    {
        if (_client != IntPtr.Zero)
        {
            Snap7Native.Cli_Disconnect(_client);
            Snap7Native.Cli_Destroy(ref _client);
        }
        if (_server != IntPtr.Zero)
        {
            Snap7Native.Srv_Stop(_server);
            Snap7Native.Srv_UnregisterArea(_server, ServerDbArea, _dbNumber);
            Snap7Native.Srv_Destroy(ref _server);
        }
        if (_pinned.IsAllocated) _pinned.Free();
        _dbData = null;
    }

    private static void Check(int result, string operation)
    {
        if (result != 0) throw new InvalidOperationException($"{operation}: chyba Snap7 0x{result:X8}.");
    }
}
