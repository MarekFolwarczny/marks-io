using System.Windows.Forms;

namespace ModbusServer;

internal enum UiLanguage { English, Czech }

/// <summary>České zprávy protokolových tříd převádí pro anglické rozhraní; data komunikace nemění.</summary>
internal static class UiText
{
    internal static UiLanguage Language { get; set; } = UiLanguage.English;

    private static readonly Dictionary<string, string> Exact = new(StringComparer.Ordinal)
    {
        ["Spustit"] = "Start", ["Zastavit"] = "Stop", ["Připojit"] = "Connect",
        ["Odpojit"] = "Disconnect", ["Vyčíst"] = "Read", ["Zapsat"] = "Write",
        ["Zastaveno"] = "Stopped", ["Odpojeno"] = "Disconnected", ["prázdné"] = "empty",
        ["Modbus TCP server – simulátor registrů"] = "Modbus TCP server – register simulator",
        ["Modbus TCP klient"] = "Modbus TCP client",
        ["TCP/IP textový server"] = "TCP/IP text server",
        ["TCP/IP textový klient"] = "TCP/IP text client",
        ["Snap7 klient"] = "Snap7 client", ["OPC UA klient"] = "OPC UA client",
        ["Uložit konfiguraci"] = "Save configuration", ["Uložit změnu"] = "Save change",
        ["Přidat"] = "Add", ["Smazat"] = "Delete", ["Odebrat"] = "Remove",
        ["Přidat uzel"] = "Add node", ["Odeslat text"] = "Send text",
        ["Název"] = "Name", ["Název:"] = "Name:",
        ["Adresa"] = "Address", ["Adresa:"] = "Address:",
        ["Adresa (0-based)"] = "Address (0-based)",
        ["Typ"] = "Type", ["Typ:"] = "Type:",
        ["Délka"] = "Length", ["Hodnota"] = "Value", ["Hodnota:"] = "Value:",
        ["Bit 0–15"] = "Bit 0–15", ["Registry"] = "Registers",
        ["Log serveru"] = "Server log", ["Log komunikace"] = "Communication log",
        ["Obsah DB"] = "DB contents", ["Od bajtu:"] = "From byte:",
        ["256 bajtů na stránku · hex | ASCII"] = "256 bytes per page · hex | ASCII",
        ["Výchozí DB:"] = "Default DB:", ["Velikost DB (byte):"] = "DB size (bytes):",
        ["Naslouchat na IP:"] = "Listen on IP:", ["IP PLC:"] = "PLC IP:",
        ["Odeslat UTF-8 text"] = "Send UTF-8 text", ["Přijatý text"] = "Received text",
        ["Příklady zpráv: STATUS 1 + nový řádek    |    <STATUS>1</STATUS>"] =
            "Examples: STATUS 1 + newline    |    <STATUS>1</STATUS>",
        ["Profil certifikátů:"] = "Certificate profile:",
        ["Uzly serveru (změna seznamu vyžaduje restart serveru)"] =
            "Server nodes (restart the server to change the list)",
        ["Spusťte server pro zobrazení DB."] = "Start the server to view the DB.",
        ["Server je zastaven."] = "Server stopped.",
        ["Spojení ukončeno."] = "Connection closed.",
        ["Hodnota zapsána."] = "Value written.",
        ["Konfigurace"] = "Configuration", ["Modbus server"] = "Modbus server",
        ["Záznam proměnné je prázdný."] = "Variable entry is empty.",
        ["Bit musí být číslo 0 až 15."] = "Bit must be a number from 0 to 15.",
        ["Zadejte platnou IPv4 nebo IPv6 adresu."] = "Enter a valid IPv4 or IPv6 address.",
        ["Zadejte platnou IP adresu."] = "Enter a valid IP address.",
        ["Zadejte host nebo IP adresu."] = "Enter a host name or IP address.",
        ["Zadejte název uzlu."] = "Enter a node name.",
        ["Název a NodeId musí být jedinečné."] = "Name and NodeId must be unique.",
        ["Endpoint musí mít tvar opc.tcp://host:port[/cesta]."] =
            "Endpoint must have the form opc.tcp://host:port[/path].",
        ["Zadejte platný namespace URI."] = "Enter a valid namespace URI.",
        ["Přidejte alespoň jeden uzel."] = "Add at least one node.",
        ["Konfiguraci je nutné opravit a uložit ručně."] =
            "Fix the configuration and save it manually.",
        ["Server již běží."] = "Server is already running.",
        ["Klient je již připojen."] = "Client is already connected.",
        ["Klient není připojen."] = "Client is not connected.",
        ["Komunikace již běží."] = "Communication is already running.",
        ["Zadejte text zprávy."] = "Enter message text.",
        ["Není připojený žádný klient."] = "No client is connected.",
        ["Zprávu se nepodařilo odeslat."] = "Could not send the message.",
        ["FC03 dovoluje 1 až 125 registrů v rozsahu 0 až 65535."] =
            "FC03 allows 1 to 125 registers within 0 to 65535.",
        ["Jedna hodnota musí zabírat 1 až 123 registrů v rozsahu 0 až 65535."] =
            "One value must occupy 1 to 123 registers within 0 to 65535.",
        ["Nesprávná délka odpovědi FC03."] = "Invalid FC03 response length.",
        ["Nesprávná odpověď FC06."] = "Invalid FC06 response.",
        ["Nesprávná odpověď FC16."] = "Invalid FC16 response.",
        ["Nesprávná odpověď FC22."] = "Invalid FC22 response.",
        ["Neplatná hlavička odpovědi Modbus TCP."] = "Invalid Modbus TCP response header.",
        ["Neplatná Modbus výjimka."] = "Invalid Modbus exception.",
        ["Odpověď obsahuje jinou funkci."] = "Response contains a different function.",
        ["Snap7 klienta se nepodařilo vytvořit."] = "Could not create the Snap7 client.",
        ["Snap7 server se nepodařilo vytvořit."] = "Could not create the Snap7 server.",
        ["Snap7 server není spuštěný."] = "Snap7 server is not running.",
        ["Nejdříve spusťte server nebo připojte klienta."] =
            "Start the server or connect the client first.",
        ["Požadovaný rozsah je mimo DB."] = "Requested range is outside the DB.",
        ["Nesprávná délka hodnoty."] = "Invalid value length.",
        ["Nastavení portu"] = "Set port", ["Připojení"] = "Connect",
        ["Registrace DB"] = "Register DB", ["Spuštění serveru"] = "Start server",
        ["Čtení DB"] = "Read DB", ["Zápis DB"] = "Write DB",
        ["Zápis bitu"] = "Write bit", ["Čtení logu serveru"] = "Read server log",
        ["Zamčení DB"] = "Lock DB", ["Odemčení DB"] = "Unlock DB",
        ["Adresa musí být například 10.2, DB1.DBW12 nebo DB1.DBD20."] =
            "Address must look like 10.2, DB1.DBW12 or DB1.DBD20.",
        ["Neplatné číslo DB."] = "Invalid DB number.",
        ["DB nebo byte offset je mimo povolený rozsah."] = "DB or byte offset is out of range.",
        ["BOOL používá DBX a bit 0–7."] = "BOOL uses DBX and bit 0–7.",
        ["Tvar adresy neodpovídá vybranému typu."] = "Address format does not match the selected type.",
        ["BOOL musí být 0, 1, false nebo true."] = "BOOL must be 0, 1, false or true.",
        ["DWORD_ASCII přijímá nejvýše čtyři ASCII znaky."] =
            "DWORD_ASCII accepts at most four ASCII characters.",
        ["Profil neobsahuje soukromý klíč."] = "Profile has no private key.",
        ["Vlastní certifikát není časově platný."] = "Own certificate is expired or not yet valid.",
        ["Application URI neodpovídá URI v certifikátu."] =
            "Application URI does not match the certificate URI.",
        ["Profil neobsahuje schválené certifikáty v trusted/."] =
            "Profile has no approved certificates in trusted/.",
        ["Certifikát serveru nemá Subject Alternative Name."] =
            "Server certificate has no Subject Alternative Name.",
        ["Typ hodnoty neodpovídá uzlu."] = "Value type does not match the node.",
        ["Serverový certifikát nemá Application URI v SAN."] =
            "Server certificate has no Application URI in SAN.",
        ["OPC UA session nebyla navázána."] = "OPC UA session was not established.",
        ["Server nenabízí zadaný namespace URI."] = "Server does not expose the given namespace URI.",
        ["Uzel vrátil prázdnou hodnotu."] = "Node returned an empty value.",
        ["Nepodporovaný skalární typ OPC UA."] = "Unsupported OPC UA scalar type.",
        ["Text smí mít nejvýše 255 znaků."] = "Text may contain at most 255 characters.",
        ["Neplatná hodnota typu Boolean. Použijte true/false nebo 1/0."] =
            "Invalid Boolean value. Use true/false or 1/0.",
        ["Hodnota musí být konečné číslo."] = "Value must be a finite number.",
        ["Zadejte NodeId bez ns=; namespace se zadává jako URI."] =
            "Enter NodeId without ns=; enter the namespace as a URI.",
        ["NodeId musí být i=kladné číslo, s=text, g=UUID nebo b=base64."] =
            "NodeId must be i=positive number, s=text, g=UUID or b=base64.",
        ["Zadejte název proměnné."] = "Enter a variable name.",
        ["Hodnota proměnné nesmí být null."] = "Variable value cannot be null.",
        ["Délka musí být 1 až 125 registrů."] = "Length must be 1 to 125 registers.",
        ["Adresa nebo rozsah registrů je mimo 0 až 65535."] =
            "Address or register range is outside 0 to 65535.",
        ["Bit 0 až 15 lze zadat pouze pro BOOL."] = "Bit 0 to 15 can only be set for BOOL.",
        ["REAL musí být číslo."] = "REAL must be a number.",
        ["Textové registry podporují pouze ASCII znaky."] =
            "Text registers support ASCII characters only."
    };

    // Části vět zachytí proměnlivé adresy, hodnoty a identifikátory v logu.
    private static readonly (string Czech, string English)[] Parts =
    [
        ("Chyba: vypršel časový limit komunikace.", "Error: communication timed out."),
        ("Konfiguraci nelze načíst: ", "Cannot load configuration: "),
        ("Konfiguraci nelze uložit: ", "Cannot save configuration: "),
        ("Konfiguraci je nutné opravit", "Configuration must be fixed"),
        (" neodpovídá ", " does not match "),
        ("Proměnnou ", "Variable "), (" nelze načíst: ", " could not be loaded: "),
        ("(bez názvu)", "(unnamed)"),
        ("Připojeno k ", "Connected to "), ("Připojeno: ", "Connected: "),
        ("Naslouchá na ", "Listening on "), ("Server běží: ", "Server running: "),
        ("Klient připojen: ", "Client connected: "), ("Klient odpojen: ", "Client disconnected: "),
        ("Odmítnut klient: limit ", "Client rejected: limit of "),
        ("Klient ", "Client "), (" připojen.", " connected."),
        (" odpojen po nečinnosti.", " disconnected after inactivity."),
        (" odpojen.", " disconnected."), (" spojení.", " connections."),
        ("neznámý klient", "unknown client"),
        ("Chyba TCP serveru: ", "TCP server error: "),
        ("Chyba serveru: ", "Server error: "),
        ("Chyba klienta ", "Client error for "),
        ("Chyba logu: ", "Log error: "),
        ("Chyba kontroly spojení: ", "Connection check error: "),
        ("Chyba: ", "Error: "),
        ("vypršel časový limit požadavku.", "request timed out."),
        ("neplatná hlavička Modbus TCP.", "invalid Modbus TCP header."),
        ("odmítnut FC", "rejected FC"), ("chyba Modbus ", "Modbus error "),
        ("maskovaný zápis HR ", "masked write HR "),
        ("čtení HR ", "read HR "), ("zápis HR ", "write HR "),
        ("FC03 čtení HR ", "FC03 read HR "),
        ("Zápis HR ", "Write HR "),
        ("Čtení DB", "Read DB"), ("Čtení ", "Read "),
        ("Zápis ", "Write "), ("Odmítnut zápis ", "Write rejected for "),
        ("klient nemá oprávnění.", "client lacks permission."),
        ("nesprávný typ.", "wrong type."),
        ("text je příliš dlouhý.", "text is too long."),
        ("nekonečná nebo NaN hodnota.", "infinite or NaN value."),
        ("Přijato od ", "Received from "),
        ("Odeslání klientovi selhalo: ", "Send to client failed: "),
        ("Odesláno ", "Sent "), (" klientů): ", " clients): "),
        ("Přidán uzel ", "Added node "),
        ("Obsah DB", "DB contents"), (" bajtů)", " bytes)"),
        ("Uloženo: ", "Saved: "),
        ("Spojení se Snap7 serverem bylo ukončeno.", "Snap7 server connection closed."),
        ("Snap7 událost ", "Snap7 event "), ("výsledek ", "result "),
        ("Server nabízí pouze DB", "Server exposes only DB"),
        ("Požadovaný rozsah je mimo DB.", "Requested range is outside DB."),
        (" offset ", " offset "),
        ("Host ", "Host "), (" není uveden v SAN certifikátu serveru.", " is not listed in the server certificate SAN."),
        (" musí být také ve složce trusted/.", " must also be in trusted/."),
        ("Modbus odmítl FC", "Modbus rejected FC"), ("chyba ", "error "),
        ("Název ", "Name "), (" již existuje.", " already exists."),
        ("Proměnná se překrývá s ", "Variable overlaps with "),
        (" na registru ", " at register "),
        ("Text se nevejde do ", "Text does not fit in "),
        (" registrů.", " registers."),
        (": chyba Snap7 ", ": Snap7 error ")
    ];

    internal static string Translate(string value)
    {
        if (Language == UiLanguage.Czech || string.IsNullOrEmpty(value)) return value;
        if (Exact.TryGetValue(value, out var translated)) return translated;
        foreach (var (czech, english) in Parts)
            value = value.Replace(czech, english, StringComparison.Ordinal);
        return value;
    }

    internal static string TranslateLog(string message)
    {
        if (Language == UiLanguage.Czech) return message;
        // Za dvojtečkou následuje u těchto událostí uživatelská hodnota nebo text TCP zprávy.
        if (message.StartsWith("Přijato od ", StringComparison.Ordinal) ||
            message.StartsWith("Odesláno ", StringComparison.Ordinal) ||
            message.StartsWith("FC03 čtení HR ", StringComparison.Ordinal) ||
            message.StartsWith("Zápis HR ", StringComparison.Ordinal) ||
            message.StartsWith("Čtení ", StringComparison.Ordinal) ||
            message.StartsWith("Zápis ", StringComparison.Ordinal))
        {
            var separator = message.IndexOf(": ", StringComparison.Ordinal);
            if (separator >= 0)
                return Translate(message[..(separator + 2)]) + message[(separator + 2)..];
        }
        return Translate(message);
    }

    internal static DialogResult Show(string message, string caption, MessageBoxButtons buttons, MessageBoxIcon icon) =>
        MessageBox.Show(Translate(message), Translate(caption), buttons, icon);
}
