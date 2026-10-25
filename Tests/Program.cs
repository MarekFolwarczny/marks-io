using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ModbusServer;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

var s7Bit = Snap7Codec.Parse("DB1.DBX10.2", 1, Snap7ValueType.Bool);
Check(s7Bit == new Snap7Address(1, 10, 2), "Snap7 DBX address");
Check(Snap7Codec.Decode([0b_0000_0100], Snap7ValueType.Bool, s7Bit.Bit) == "1", "Snap7 BOOL bit");
Check(Snap7Codec.Encode("-123", Snap7ValueType.Int16).SequenceEqual(new byte[] { 0xFF, 0x85 }), "Snap7 INT big endian");
Check(Snap7Codec.Decode([0x12, 0x34, 0x56, 0x78], Snap7ValueType.Dword, 0) == "305419896", "Snap7 DWORD big endian");
Check(Snap7Codec.Parse("DB1.DBD20", 1, Snap7ValueType.Dword) == new Snap7Address(1, 20, 0), "Snap7 DBD address");
Check(Snap7Codec.Encode("OK", Snap7ValueType.DwordAscii).SequenceEqual("OK  "u8.ToArray()), "Snap7 DWORD_ASCII padding");
Check(Snap7Codec.Decode("OK  "u8, Snap7ValueType.DwordAscii, 0) == "OK", "Snap7 DWORD_ASCII read");
try { Snap7Codec.Parse("DB1.DBW10.2", 1, Snap7ValueType.Int16); throw new Exception("Invalid Snap7 bit address accepted"); }
catch (ArgumentException) { }

var bank = new RegisterBank();
// Stejné bity musí být vidět v interní mapě i v odpovědi skutečného TCP klienta.
var real = new Variable { Name = "Teplota", Address = 10, Type = VariableType.Float32, Value = "12.5" };
VariableCodec.Validate(real, []);
bank.SetVariable(real);
Check(bank.Read(10, 2).SequenceEqual(new ushort[] { 0x4148, 0x0000 }), "REAL byte/word order");

var bit = new Variable { Name = "Ready", Address = 12, Type = VariableType.Bool, Bit = 8, Value = "1" };
VariableCodec.Validate(bit, []);
bank.SetVariable(bit);
Check(bank.Read(12, 1)[0] == 0x0100, "BOOL bit placement");
VariableCodec.Validate(new Variable { Name = "Other bit", Address = 12, Type = VariableType.Bool, Bit = 0, Value = "0" }, [bit]);
try
{
    VariableCodec.Validate(new Variable { Name = "Same bit", Address = 12, Type = VariableType.Bool, Bit = 8, Value = "0" }, [bit]);
    throw new Exception("Overlapping bit accepted");
}
catch (ArgumentException) { }

var text = new Variable { Name = "Code", Address = 20, Type = VariableType.String, Length = 2, Value = "AB" };
bank.SetVariable(text);
Check(bank.Read(20, 2).SequenceEqual(new ushort[] { 0x4142, 0 }), "STRING encoding");

var ascii = new Variable { Name = "Ascii", Address = 30, Type = VariableType.RegisterAscii, Length = 2, Value = "AB" };
bank.SetVariable(ascii);
Check(bank.Read(30, 2).SequenceEqual(new ushort[] { 0x4100, 0x4200 }), "REGISTER_ASCII encoding");

var signed = new Variable { Name = "Signed", Address = 40, Type = VariableType.Int16, Value = "-1" };
bank.SetVariable(signed);
Check(bank.Read(40, 1)[0] == 0xFFFF && VariableCodec.Decode(signed, bank.Read(40, 1)) == "-1", "INT16 signed value");

var dword = new Variable { Name = "Dword", Address = 41, Type = VariableType.UInt32, Value = "305419896" };
bank.SetVariable(dword);
Check(bank.Read(41, 2).SequenceEqual(new ushort[] { 0x1234, 0x5678 }), "DWORD word order");

var dint = new Variable { Name = "Dint", Address = 43, Type = VariableType.Int32, Value = "-2023406815" };
bank.SetVariable(dint);
Check(bank.Read(43, 2).SequenceEqual(new ushort[] { 0x8765, 0x4321 }) &&
      VariableCodec.Decode(dint, bank.Read(43, 2)) == dint.Value, "DINT signed byte/word order");

var reinterpret = new Variable { Name = "Reinterpret", Address = 45, Type = VariableType.Int32 };
ushort[] allBits = [0xFFFF, 0xFFFF];
reinterpret.Value = VariableCodec.Decode(reinterpret, allBits);
VariableCodec.Validate(reinterpret, []);
Check(reinterpret.Value == "-1" && VariableCodec.Encode(reinterpret).SequenceEqual(allBits),
      "UInt32 raw words can be reinterpreted as Int32 without data loss");

try
{
    VariableCodec.Validate(null!, []);
    throw new Exception("Null config variable accepted");
}
catch (ArgumentNullException) { }

var controlText = new Variable { Name = "Control", Address = 50, Type = VariableType.String, Length = 2, Value = "A\u0001" };
VariableCodec.Validate(controlText, []);
Check(VariableCodec.Decode(controlText, VariableCodec.Encode(controlText)) == controlText.Value, "ASCII control byte roundtrip");

var rawBool = new Variable { Name = "RawBool", Address = 60, Type = VariableType.Bool, Value = "0" };
var rawText = new Variable { Name = "RawText", Address = 61, Type = VariableType.String, Value = "?" };
bank.SetVariable(rawBool);
bank.SetVariable(rawText);
bank.Write(60, [0x0002, 0xFF00]);
var variables = new List<Variable> { rawBool, rawText };
rawBool.Value = VariableCodec.Decode(rawBool, bank.Read(60, 1));
rawText.Value = VariableCodec.Decode(rawText, bank.Read(61, 1));
var saved = JsonSerializer.Deserialize<SimulatorConfig>(JsonSerializer.Serialize(new SimulatorConfig
{
    Variables = variables, RegisterValues = bank.Capture(variables)
}))!;
var restored = new RegisterBank();
foreach (var variable in saved.Variables) restored.SetVariable(variable);
restored.Restore(saved.Variables, saved.RegisterValues);
Check(restored.Read(60, 2).SequenceEqual(new ushort[] { 0x0002, 0xFF00 }), "Raw config values survive restart");

var server = new ModbusTcpServer(bank);
var events = new ConcurrentQueue<string>();
var disconnected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
server.Log += message =>
{
    events.Enqueue(message);
    if (message.Contains("odpojen")) disconnected.TrySetResult(true);
};
server.Start(IPAddress.Loopback, 0);
try
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    using var client = new TcpClient();
    await client.ConnectAsync(IPAddress.Loopback, server.Port).WaitAsync(timeout.Token);
    var stream = client.GetStream();

    async Task<byte[]> Send(params byte[] pdu)
    {
        byte[] request = [0, 1, 0, 0, 0, (byte)(pdu.Length + 1), 1, .. pdu];
        await stream.WriteAsync(request, timeout.Token);
        var header = new byte[7];
        await stream.ReadExactlyAsync(header, timeout.Token);
        Check(header[0] == 0 && header[1] == 1 && header[6] == 1, "MBAP header");
        var response = new byte[((header[4] << 8) | header[5]) - 1];
        await stream.ReadExactlyAsync(response, timeout.Token);
        return response;
    }

    Check((await Send(3, 0, 10, 0, 2)).SequenceEqual(new byte[] { 3, 4, 0x41, 0x48, 0, 0 }), "FC03");
    Check((await Send(3, 0, 43, 0, 2)).SequenceEqual(new byte[] { 3, 4, 0x87, 0x65, 0x43, 0x21 }), "FC03 DINT bytes");
    Check((await Send(6, 0, 12, 0, 5)).SequenceEqual(new byte[] { 6, 0, 12, 0, 5 }), "FC06");
    Check(bank.Read(12, 1)[0] == 5, "FC06 bank update");
    Check((await Send(16, 0, 10, 0, 2, 4, 0, 1, 0, 2)).SequenceEqual(new byte[] { 16, 0, 10, 0, 2 }), "FC16");
    Check(bank.Read(10, 2).SequenceEqual(new ushort[] { 1, 2 }), "FC16 bank update");
    Check((await Send(3, 0, 0, 0, 0)).SequenceEqual(new byte[] { 0x83, 3 }), "Invalid quantity");
    Check((await Send(3, 255, 255, 0, 2)).SequenceEqual(new byte[] { 0x83, 2 }), "Invalid address");
    Check((await Send(16, 0, 10, 0, 2, 2, 0, 1)).SequenceEqual(new byte[] { 0x90, 3 }), "Invalid FC16 byte count");
    Check((await Send(22, 0, 12, 0xFE, 0xFF, 1, 0)).SequenceEqual(new byte[] { 22, 0, 12, 0xFE, 0xFF, 1, 0 }), "FC22 response");
    Check(bank.Read(12, 1)[0] == 0x0105, "FC22 preserves other bits");

    using var modbusClient = new ModbusTcpClient();
    await modbusClient.ConnectAsync("127.0.0.1", server.Port, timeout.Token);
    Check((await modbusClient.ReadHoldingRegistersAsync(1, 41, 2, timeout.Token))
        .SequenceEqual(new ushort[] { 0x1234, 0x5678 }), "Modbus client FC03");
    await modbusClient.WriteHoldingRegistersAsync(1, 12, [0x0100], timeout.Token);
    Check(bank.Read(12, 1)[0] == 0x0100, "Modbus client FC06");
    await modbusClient.WriteHoldingRegistersAsync(1, 41, [0xABCD, 0x1234], timeout.Token);
    Check(bank.Read(41, 2).SequenceEqual(new ushort[] { 0xABCD, 0x1234 }), "Modbus client FC16");
    await modbusClient.MaskWriteRegisterAsync(1, 12, 0, false, timeout.Token);
    Check(bank.Read(12, 1)[0] == 0x0100, "Modbus client FC22 clears only selected bit");
    var maxWrite = Enumerable.Range(0, 123).Select(i => (ushort)i).ToArray();
    await modbusClient.WriteHoldingRegistersAsync(1, 100, maxWrite, timeout.Token);
    Check((await modbusClient.ReadHoldingRegistersAsync(1, 100, 123, timeout.Token))
        .SequenceEqual(maxWrite), "Modbus client FC16 maximum length");
    try
    {
        await modbusClient.WriteHoldingRegistersAsync(1, 100, new ushort[124], timeout.Token);
        throw new Exception("Partial value write accepted");
    }
    catch (ArgumentOutOfRangeException) { }
    modbusClient.Disconnect();

    Check(events.Any(message => message.Contains("připojen")), "Client connection log");
    Check(events.Any(message => message.Contains("FC03 čtení HR 10–11")), "Read log");
    Check(events.Any(message => message.Contains("FC06 zápis HR 12")), "Single write log");
    Check(events.Any(message => message.Contains("FC16 zápis HR 10–11")), "Multiple write log");
    Check(events.Any(message => message.Contains("odmítnut FC16")), "Rejected request log");
    client.Close();
    await disconnected.Task.WaitAsync(timeout.Token);
}
finally { server.Stop(); }

// Klient nesmí přijmout odpověď určenou jiné Modbus transakci.
using (var malformedListener = new TcpListener(IPAddress.Loopback, 0))
using (var malformedClient = new ModbusTcpClient())
using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
{
    malformedListener.Start();
    var connect = malformedClient.ConnectAsync("127.0.0.1", ((IPEndPoint)malformedListener.LocalEndpoint).Port, timeout.Token);
    using var peer = await malformedListener.AcceptTcpClientAsync(timeout.Token);
    await connect;
    var read = malformedClient.ReadHoldingRegistersAsync(1, 0, 1, timeout.Token);
    var request = new byte[12];
    await peer.GetStream().ReadExactlyAsync(request, timeout.Token);
    await peer.GetStream().WriteAsync(new byte[] { 0, 2, 0, 0, 0, 2, 1 }, timeout.Token);
    try { await read; throw new Exception("Mismatched transaction accepted"); }
    catch (InvalidDataException) { }
    Check(!malformedClient.IsConnected, "Malformed response did not close Modbus connection");
}

// Nečekaná chyba přijímání spojení musí uvolnit port pro další spuštění.
var faultyServer = new ModbusTcpServer(new RegisterBank());
var failed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
faultyServer.Log += _ => throw new InvalidOperationException("Test chyby logování");
faultyServer.Status += message =>
{
    if (message.StartsWith("Chyba serveru:", StringComparison.Ordinal)) failed.TrySetResult(true);
};
faultyServer.Start(IPAddress.Loopback, 0);
try
{
    using var client = new TcpClient();
    await client.ConnectAsync(IPAddress.Loopback, faultyServer.Port);
    await failed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Check(faultyServer.Port == 0, "Listener remains open after failure");
}
finally { faultyServer.Stop(); }

using var tcpServer = new TcpTextTransport();
using var tcpClient = new TcpTextTransport();
var tcpConnected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
var serverReceived = new StringBuilder();
var clientReceived = new StringBuilder();
var serverMessage = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
var clientMessage = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
tcpServer.Log += message => { if (message.Contains("připojen.")) tcpConnected.TrySetResult(true); };
tcpServer.Received += (_, message) =>
{
    serverReceived.Append(message);
    if (serverReceived.ToString().Contains("STATUS 1\n")) serverMessage.TrySetResult(true);
};
tcpClient.Received += (_, message) =>
{
    clientReceived.Append(message);
    if (clientReceived.ToString().Contains("<READY>1</READY>")) clientMessage.TrySetResult(true);
};
tcpServer.StartServer(IPAddress.Loopback, 0);
try
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    await tcpClient.ConnectAsync("127.0.0.1", tcpServer.Port, timeout.Token);
    await tcpConnected.Task.WaitAsync(timeout.Token);
    await tcpClient.SendAsync("STATUS 1\n", timeout.Token);
    await serverMessage.Task.WaitAsync(timeout.Token);
    await tcpServer.SendAsync("<READY>1</READY>", timeout.Token);
    await clientMessage.Task.WaitAsync(timeout.Token);
}
finally { tcpClient.Stop(); tcpServer.Stop(); }

Console.WriteLine("OK: Modbus server/klient a TCP/IP server/klient");
