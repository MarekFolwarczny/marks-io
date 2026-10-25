# Marks IO v1.0.0

Marks IO is a small Windows desktop application for testing industrial communication. Choose one server or client mode when it starts, then inspect values and communication events in the same window. It simulates protocol data and does not execute a PLC program.

The interface starts in **English**. Select **Čeština** in the mode selection window to use Czech for that instance. Protocol values and text payloads are never translated.

[Český návod](README.cs.md) · [Technical details](docs/TECHNICAL.md) · [OPC UA certificates](docs/OPC_UA_CERTIFICATES.md) · [Publishing checklist](docs/PUBLISHING.md)

## Modes

| Mode | What you can do |
| --- | --- |
| Modbus TCP server | Define and edit holding registers; inspect raw words, client reads and writes. |
| Modbus TCP client | Manually read and write registers on another server. |
| TCP/IP text server | Receive UTF-8 data and send text to connected clients. |
| TCP/IP text client | Connect to a TCP endpoint and exchange UTF-8 text. |
| Snap7 server | Expose one S7 data block (DB) and inspect its live bytes. |
| Snap7 client | Read and write DB values by rack, slot, DB and byte address. |
| OPC UA server | Expose typed scalar nodes under a namespace URI. |
| OPC UA client | Read or write a node using its namespace URI and NodeId. |

Only one mode runs per application instance. To use two modes together, launch two instances.

## Download and run

The project targets **Windows x64**. A self-contained release needs no separate .NET installation. Keep the entire published `dist/` folder together; it contains:

```text
MarksIO.exe
snap7.dll
LICENSE
THIRD_PARTY_NOTICES.md
lgpl-3.0.txt
opc-foundation-mit.txt
gpl-3.0.txt
bitfaster-mit.txt
newtonsoft-mit.txt
dotnet-mit.txt
```

The icon, logo and launcher artwork are included in `Assets/`. New Modbus, TCP text and Snap7 servers listen on `127.0.0.1` by default. These protocols have no built-in authentication or encryption in this simulator. To accept clients from another machine, select a reachable local address deliberately and configure Windows Firewall. An existing saved Modbus configuration keeps its previous listen address.

## Quick start

1. Run `MarksIO.exe`, choose a language and select a mode.
2. Set the endpoint and any protocol options, then click **Start** or **Connect**.
3. Use **Read** and **Write**, or connect an external client. The log shows connection changes and data operations.
4. Click **Stop** before changing the server address, data block or OPC UA node list.

### Modbus TCP

The server defaults to port `1502` and accepts function codes **03** (read holding registers), **06** (write one), **16** (write several) and **22** (mask a register). Addresses start at **zero**: address `0` is often shown as `40001` by other tools. Unassigned addresses read as zero. All Unit IDs share one register map.

Define a variable with a name, address, type and value. `Bool` can use a whole register or a selected bit `0–15`. Text types also need a register count. `Int16`, `UInt16` and `Bool` use one register; `Int32`, `UInt32`, `Float32` and `DwordAscii` use two. `String` stores two ASCII bytes per register; `RegisterAscii` stores one ASCII byte in each register's high byte.

Multi-register numbers use **high byte and high word first**. For example, `0x12345678` occupies `0x1234` then `0x5678`. The client uses FC22 for a selected Boolean bit so it does not overwrite other bits in that register. Server definitions and raw register values are saved in `%LOCALAPPDATA%\ModbusPlcSimulator\config.json`.

### TCP/IP text

The text modes send exactly the entered UTF-8 text. They do not add a newline, length prefix or other framing. For a line-based peer, type the newline yourself. TCP is a byte stream: one receive event may contain part of a message or several messages. For example, you can send `STATUS 1` followed by a newline, or a tagged message such as `<STATUS>1</STATUS>` if the peer expects it.

### Snap7

The server exposes one DB (DB1 by default) and shows its current bytes in hex and ASCII. Select a byte offset to view another part of a larger DB. Examples: `DB1.DBX10.2` is Boolean bit 2 at byte 10, `DB1.DBW12` is a signed 16-bit integer and `DB1.DBD20` is a 32-bit value. Numbers use big-endian byte order. The client defaults to rack `0`, slot `1` and port `102`. DB contents remain in memory only.

### OPC UA

Both modes require **Basic256Sha256 / SignAndEncrypt** and manually approved application certificates. The server's default endpoint is `opc.tcp://127.0.0.1:4840`. Add nodes before starting it, for example namespace URI `urn:example:line1`, NodeId `s=Trigger`, type `Boolean`, value `false`. The client resolves the namespace URI after connecting, so enter `s=Trigger` **without `ns=`**. Supported scalar types are `Boolean`, `Int16`, `UInt16`, `Int32`, `UInt32`, `Int64`, `UInt64`, `Float`, `Double` and `String` (up to 255 Unicode characters).

A trusted client may read server nodes. Only the client whose certificate matches the server profile's `writer.der` may write. The server displays live values and logs accepted and rejected writes. Node definitions and values are not saved. The client performs manual operations; it does not poll or reconnect automatically.

See [OPC UA certificate setup](docs/OPC_UA_CERTIFICATES.md) for profile layout, certificate creation, mutual trust and common connection errors.

## Build and test

Install the **.NET 8 SDK** and run from this repository's root:

```powershell
dotnet publish MarksIO.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o dist
```

The release files appear in `dist/`. On Windows, `watch-build.cmd` publishes the EXE immediately and again after each application `.cs`, `.csproj` or `Assets/` artwork change while its window remains open.

```powershell
dotnet run --project Tests\Tests.csproj
dotnet run --project Tests\Snap7NativeTests.csproj
dotnet run --project Tests\OpcUaTests.csproj
```

The native Snap7 test uses loopback port `1102`; the OPC UA test uses `14840` and creates temporary certificates. See [technical documentation](docs/TECHNICAL.md) for architecture, protocol limits and test scope.

## License

The original application source and artwork are released under the [MIT License](LICENSE). Snap7 and the OPC Foundation stack retain their own licenses; see [third-party notices](THIRD_PARTY_NOTICES.md), including the pinned source for the bundled Snap7 DLL. Keep the notices with distributed binaries.
