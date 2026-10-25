# Marks IO v1.0.0

**Stažení pro Windows x64:** [MarksIO-v1.0.0-win-x64.zip](https://github.com/MarekFolwarczny/marks-io/releases/download/v1.0.0/MarksIO-v1.0.0-win-x64.zip). ZIP rozbalte a spusťte `MarksIO.exe`; ostatní soubory ponechte ve stejné složce.

Marks IO je jednoduchá aplikace pro Windows určená k testování průmyslové komunikace. Po spuštění zvolíte jeden režim serveru nebo klienta; další režim lze spustit v druhé instanci. Aplikace simuluje data protokolů, nikoli program nebo cyklus PLC.

Výchozím jazykem rozhraní je **angličtina**. V úvodním okně lze pro danou instanci zvolit **Čeština**. Hodnoty proměnných a textové zprávy přenášené protokoly se nepřekládají.

[English README](README.md) · [Technické podrobnosti](docs/TECHNICAL.md) · [Certifikáty OPC UA](docs/OPC_UA_CERTIFICATES.md) · [Příprava publikace](docs/PUBLISHING.md)

## Režimy

| Režim | Funkce |
| --- | --- |
| Modbus TCP server | Definice holding registrů, živé hodnoty, surová slova a log přístupů. |
| Modbus TCP klient | Ruční čtení a zápis registrů vzdáleného serveru. |
| TCP/IP textový server | Příjem UTF-8 dat a odeslání textu připojeným klientům. |
| TCP/IP textový klient | Připojení k TCP endpointu a výměna UTF-8 textu. |
| Snap7 server | Jeden datablock DB a živý přehled jeho bajtů. |
| Snap7 klient | Čtení a zápis DB podle racku, slotu, čísla DB a adresy. |
| OPC UA server | Typované skalární uzly v zadaném namespace URI. |
| OPC UA klient | Ruční čtení a zápis uzlu podle namespace URI a NodeId. |

## Spuštění a sestavení

Cílovou platformou je **Windows x64**. Samostatně publikované `MarksIO.exe` má přibalený .NET runtime. Při kopírování ponechte pohromadě **celý obsah `dist/`**, tedy EXE, `snap7.dll`, licenci aplikace a všechny licenční texty závislostí. Ikona, logo a grafika úvodního okna jsou ve složce `Assets/`. Nové servery Modbus, TCP text a Snap7 naslouchají pouze na `127.0.0.1`. Tyto režimy nemají vlastní autentizaci ani šifrování; pro vzdálené klienty nastavte dosažitelnou místní adresu vědomě a povolte port ve Windows Firewallu. Dříve uložená konfigurace Modbusu si zachová původní adresu.

S .NET 8 SDK sestavíte vydání příkazem:

```powershell
dotnet publish MarksIO.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o dist
```

Výsledek najdete v `dist/`. Skript `watch-build.cmd` vytvoří EXE hned po spuštění a znovu po změně aplikačního souboru `.cs`, `.csproj` nebo grafiky v `Assets/`, dokud jeho okno nezavřete.

## Použití

Po výběru režimu nastavte adresu a port a stiskněte **Spustit** nebo **Připojit**. Tlačítky **Vyčíst** a **Zapsat** pracujete s vybranou adresou. Log zobrazuje stav komunikace, čtení, zápisy i chyby. Pro změnu nastavení serveru jej nejprve zastavte.

### Modbus TCP

Server má výchozí port `1502` a nabízí holding registry. Podporuje funkce **03**, **06**, **16** a **22**. Adresy začínají **nulou**; adresa `0` odpovídá často používanému označení `40001`. Dosud nedefinované registry vracejí nulu. Všechna Unit ID sdílejí jednu mapu.

Každá proměnná má název, adresu, typ a hodnotu. `Bool` může obsadit celý registr nebo jediný bit `0–15`. Pro text určete i délku v registrech. `String` ukládá dva ASCII bajty na registr, `RegisterAscii` jeden znak do horního bajtu každého registru a `DwordAscii` čtyři ASCII bajty do dvou registrů. Víceslovná čísla používají **nejvyšší bajt a nejvyšší slovo první**: `0x12345678` tvoří registry `0x1234`, `0x5678`. Klient mění jednotlivý bit pomocí FC22. Definice a syrová slova se ukládají do `%LOCALAPPDATA%\ModbusPlcSimulator\config.json`.

### TCP/IP text

Odešle se přesně zadaný UTF-8 text, bez automatického odřádkování nebo délkové hlavičky. Pro řádkový protokol přidejte konec řádku sami. TCP je proud bajtů, proto jedna přijatá událost nemusí odpovídat jedné celé zprávě. Podle protokolu protistrany lze poslat třeba `STATUS 1` s koncem řádku nebo `<STATUS>1</STATUS>`.

### Snap7

Server nabízí jeden DB, výchozí DB1, a zobrazuje jeho obsah po stránkách v hexadecimálním a ASCII tvaru. `DB1.DBX10.2` označuje bit 2 v bajtu 10, `DB1.DBW12` podepsané 16bitové číslo a `DB1.DBD20` 32bitovou hodnotu. Čísla jsou big-endian. Klient má výchozí rack `0`, slot `1` a port `102`. Obsah DB se po ukončení neukládá.

### OPC UA

Server a klient používají **Basic256Sha256 / SignAndEncrypt** a předem schválené aplikační certifikáty. Výchozí endpoint serveru je `opc.tcp://127.0.0.1:4840`. Před startem přidejte uzly, například namespace URI `urn:example:line1`, NodeId `s=Trigger`, typ `Boolean` a hodnotu `false`. NodeId zadávejte **bez `ns=`**; klient přeloží namespace URI na aktuální index po připojení.

Podporované skalární typy jsou `Boolean`, `Int16`, `UInt16`, `Int32`, `UInt32`, `Int64`, `UInt64`, `Float`, `Double` a `String` do 255 znaků Unicode. Schválení klienti smějí číst; zapisovat smí pouze certifikát uvedený v `writer.der` serverového profilu. Server ukazuje živé hodnoty a loguje i odmítnuté zápisy. Seznam uzlů lze měnit po zastavení serveru, hodnoty se neukládají. Klient čte a zapisuje ručně bez automatického reconnectu.

Postup vydání certifikátů, rozložení profilů a výměnu veřejných certifikátů popisuje [návod OPC UA](docs/OPC_UA_CERTIFICATES.md).

## Testy

```powershell
dotnet run --project Tests\Tests.csproj
dotnet run --project Tests\Snap7NativeTests.csproj
dotnet run --project Tests\OpcUaTests.csproj
```

Integrační test Snap7 používá lokální port `1102`, test OPC UA port `14840` a dočasné certifikáty. Protokolové hranice a architekturu popisuje [technická dokumentace](docs/TECHNICAL.md).

## Licence

Vlastní zdrojové kódy a grafika jsou pod [licencí MIT](LICENSE). Snap7 a knihovny OPC Foundation mají vlastní licence uvedené v [oznámení o závislostech](THIRD_PARTY_NOTICES.md), kde je také odkaz na přesné zdroje přibalené knihovny Snap7. Licenční soubory ponechte i u distribuovaného EXE.
