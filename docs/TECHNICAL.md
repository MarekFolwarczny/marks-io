# Technical notes

## Architecture

`Program` shows `ModeSelectionForm` and starts exactly one protocol form. Each form owns its transport for the lifetime of the window. The modes do not share process data. Network work runs outside the UI thread; event handlers marshal control changes back to the Windows Forms thread. The application is a protocol simulator, not a PLC runtime.

The launcher defaults to English and can select Czech for the current instance. `UiText` translates application labels, status and log messages at the UI boundary. Network payloads, node values, register data and user-entered text remain unchanged.

| Mode | Main classes | Data source |
| --- | --- | --- |
| Modbus TCP server | `MainForm`, `ModbusTcpServer`, `RegisterBank` | 16-bit holding register map |
| Modbus TCP client | `ModbusClientForm`, `ModbusTcpClient`, `VariableCodec` | Remote holding registers |
| TCP/IP text | `TcpTextForm`, `TcpTextTransport` | Received and entered UTF-8 text |
| Snap7 | `Snap7Form`, `Snap7Transport`, `Snap7Codec` | One server DB or a remote S7 DB |
| OPC UA | `OpcUaForm`, `OpcUaServer`, `OpcUaClient`, `OpcUaNodeManager` | Typed scalar nodes |

## Modbus TCP

Addresses are zero-based holding register addresses. FC03 reads 1–125 registers, FC06 writes one, FC16 writes 1–123 and FC22 masks selected bits in one register. The server accepts any Unit ID but all IDs access the same map. Unassigned registers read as zero. The server limits concurrent clients to 32 and closes idle connections after five minutes.

`RegisterBank` locks multi-register reads and writes so an FC16 update cannot be observed halfway through. It raises its change event after releasing the lock; a subscriber may then read a fresh snapshot. A Boolean variable can share a register with other Boolean variables if each uses a different bit. Other overlapping definitions are rejected. The client uses FC22 when writing a selected bit to avoid a read/modify/write race.

`VariableCodec` converts UI values to network register words. Multi-byte numbers use high byte first and high word first. `String` stores two ASCII bytes per register; `RegisterAscii` uses the high byte only; `DwordAscii` uses exactly four ASCII bytes over two registers. The UI decodes each variable and its raw words from the same register snapshot. The server persists both variable definitions and raw register words in `%LOCALAPPDATA%\ModbusPlcSimulator\config.json`; this preserves bits that a typed display cannot represent exactly. A temporary file is written before replacing the previous configuration. If part of a configuration is invalid, automatic saving is disabled until the user explicitly saves, preventing silent loss of the invalid entries.

The server log queues events from client tasks and the form displays at most ten updates per second. The log and pending queue are bounded; they are diagnostics, not a durable traffic archive.

## TCP/IP text

`TcpTextTransport` sends the UTF-8 bytes of the entered text without a length header or delimiter. The receive decoder keeps incomplete UTF-8 characters between socket reads, but does not reconstruct application messages: TCP read boundaries are not message boundaries. The server accepts up to 32 clients and closes idle connections after five minutes. Sending from the server broadcasts to its current clients. Text and log history are not persisted.

## Snap7

`Snap7Transport` P/Invokes the bundled native `snap7.dll`. The server registers one DB area backed by a pinned byte array for its entire running lifetime. Local reads, writes and UI snapshots lock the native server area, so they do not race with client operations. The UI presents 256 bytes at a time as hex and ASCII; it refreshes while the server runs. The client checks its connection every two seconds with a short PLC status request because the native connected flag alone can lag behind a broken TCP connection.

`Snap7Codec` accepts full addresses such as `DB1.DBX10.2`, `DB1.DBW12` and `DB1.DBD20`, or a byte offset with the selected default DB. The supported values are Boolean, signed 16-bit integer, unsigned 32-bit value and four-byte ASCII text. Multi-byte numbers use big-endian order. A client Boolean write uses Snap7's bit operation instead of replacing the complete byte. The DB is memory-only and does not execute PLC logic. Rack and slot select a client's remote endpoint; the local server does not model separate rack or slot memories.

Snap7 is a separately replaceable DLL beside the EXE. Keep its LGPL license and source availability information with any distributed build; see [third-party notices](../THIRD_PARTY_NOTICES.md).

## OPC UA

`OpcUaSecurity` loads a DER application certificate, its matching RSA PEM key and the approved peer certificates. The application's URI must match the URI in its own certificate's Subject Alternative Name (SAN). Only `Basic256Sha256 / SignAndEncrypt` is configured. The client builds its secure endpoint from the pinned `server.der` certificate, without unauthenticated endpoint discovery or automatic trust acceptance. Before opening a session it checks the endpoint host against the certificate's DNS/IP SAN. The OPC Foundation client's built-in domain check does not accept the IP SAN in this path, so that check is disabled only after the explicit local SAN check. The stack still validates the certificate and secured channel.

`OpcUaNodeManager` creates the configured scalar nodes under one namespace URI. NodeIds are stored without a namespace index; the client resolves the URI to the server's current index after connecting. The server's address space is fixed at startup. Local value changes call `ClearChangeMasks` so subscriptions can observe them. An external write must come from a session whose certificate matches `writer.der`, match the CLR type exactly, and pass the string/finite-number checks. Other approved certificates can read; unapproved certificates cannot establish a session. The client rejects Bad or Uncertain reads and writes a single Value attribute per action. It performs no automatic polling or reconnect.

Certificate profiles are reloaded on each start/connect, not while a session is running. An empty issuer store and rejected public certificates are kept in a temporary per-profile directory; private keys are not copied there. See [certificate setup](OPC_UA_CERTIFICATES.md) for layout, issuance and trust exchange.

## Lifecycle and validation

Modbus server configuration is the only persisted process state. Snap7 DB bytes, OPC UA node values, TCP text and all logs are reset when their instance exits. Logs show at most 500 recent entries. New Modbus, TCP text and Snap7 servers default to `127.0.0.1`; saved Modbus settings retain their address. Servers bind the selected local address and port. Select a reachable interface for remote peers, and restrict access at the firewall because these three protocols do not authenticate clients. Firewall and container routing are outside the application.

The project targets .NET 8 Windows Forms and publishes a Windows x64 self-contained executable. `Tests/Tests.csproj` checks conversions and live Modbus/TCP exchanges. `Tests/Snap7NativeTests.csproj` exercises the native S7 client/server on loopback port 1102. `Tests/OpcUaTests.csproj` creates temporary certificates and tests a real encrypted connection, typed read/write, read-only and untrusted clients, hostname validation, and DER/PEM trust entries on port 14840. These tests do not establish compatibility with every PLC or SCADA implementation; validate the intended device and firmware separately.
