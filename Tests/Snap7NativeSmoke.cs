using ModbusServer;

using var server = new Snap7Transport();
using var client = new Snap7Transport();
const int port = 1102;
var word = Snap7Codec.Parse("DB1.DBW12", 1, Snap7ValueType.Int16);
var bit = Snap7Codec.Parse("DB1.DBX10.2", 1, Snap7ValueType.Bool);

server.StartServer("127.0.0.1", port, 1, 256);
server.Write(word, Snap7ValueType.Int16, Snap7Codec.Encode("-123", Snap7ValueType.Int16));
client.Connect("127.0.0.1", port, 0, 1);
if (!client.CheckConnection()) throw new Exception("Snap7 connection probe failed on a healthy server.");
if (Snap7Codec.Decode(client.Read(word, Snap7ValueType.Int16), Snap7ValueType.Int16, 0) != "-123")
    throw new Exception("Snap7 client could not read server DB.");
client.Write(bit, Snap7ValueType.Bool, [1]);
if (Snap7Codec.Decode(server.Read(bit, Snap7ValueType.Bool), Snap7ValueType.Bool, 2) != "1")
    throw new Exception("Snap7 client bit write failed.");
client.Write(word, Snap7ValueType.Int16, Snap7Codec.Encode("321", Snap7ValueType.Int16));
if (Snap7Codec.Decode(server.Read(word, Snap7ValueType.Int16), Snap7ValueType.Int16, 0) != "321")
    throw new Exception("Snap7 client word write failed.");
var dbPage = server.ReadServerBytes(10, 16);
if (dbPage[0] != 0x04 || dbPage[2] != 0x01 || dbPage[3] != 0x41)
    throw new Exception("Server DB snapshot does not show client writes.");
try { server.ReadServerBytes(255, 2); throw new Exception("Out-of-range DB snapshot accepted."); }
catch (ArgumentOutOfRangeException) { }
Thread.Sleep(100);
var events = server.PickEvents();
if (!events.Any(e => e.Contains("Read", StringComparison.OrdinalIgnoreCase)) ||
    !events.Any(e => e.Contains("Write", StringComparison.OrdinalIgnoreCase)))
    throw new Exception("Snap7 read/write log events missing: " + string.Join(" | ", events));
server.Stop();
for (var i = 0; i < 3 && client.IsRunning; i++) { client.CheckConnection(); Thread.Sleep(100); }
if (client.IsRunning) throw new Exception("Snap7 client did not notice server disconnect.");
Console.WriteLine("OK: Snap7 native client/server, DB read/write and events");
