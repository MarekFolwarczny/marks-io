using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;

namespace ModbusServer;

/// <summary>Uživatelské interpretace 16bitových holding registrů.</summary>
public enum VariableType
{
    Bool,
    Int16,
    UInt16,
    Int32,
    UInt32,
    Float32,
    String,
    RegisterAscii,
    DwordAscii
}

/// <summary>Popis proměnné uložený v konfiguraci; běžící hodnotu drží RegisterBank.</summary>
public sealed class Variable
{
    public string Name { get; set; } = "";
    public int Address { get; set; }
    public VariableType Type { get; set; }
    public int? Bit { get; set; }
    public int Length { get; set; } = 1;
    public string Value { get; set; } = "0";

    // Počet registrů vyplývá z typu; délka z editoru platí jen pro textové typy.
    [JsonIgnore]
    public int RegisterCount => Type switch
    {
        VariableType.Int32 or VariableType.UInt32 or VariableType.Float32 or VariableType.DwordAscii => 2,
        VariableType.String or VariableType.RegisterAscii => Length,
        _ => 1
    };
}

/// <summary>Nastavení adresy serveru a seznam proměnných v uživatelském JSON.</summary>
public sealed class SimulatorConfig
{
    public string ListenAddress { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 1502;
    public List<Variable> Variables { get; set; } = [];
    // Value je čitelný náhled; tato mapa zachová přesné bity i u neobvyklých zápisů klienta.
    public Dictionary<int, ushort> RegisterValues { get; set; } = [];
}

/// <summary>Jediná zamykaná mapa registrů pro UI a všechny TCP klienty.</summary>
public sealed class RegisterBank
{
    private readonly ushort[] _registers = new ushort[65536];
    private readonly object _gate = new();
    public event Action? Changed;

    /// <summary>Vrátí souvislý snímek registrů od adresy číslované od nuly.</summary>
    public ushort[] Read(int address, int count)
    {
        if (address < 0 || count < 1 || address + count > _registers.Length)
            throw new ArgumentOutOfRangeException(nameof(address));
        // Kopie pod zámkem zajistí souvislý snímek i při souběžném FC16 zápisu.
        lock (_gate)
        {
            var result = new ushort[count];
            Array.Copy(_registers, address, result, 0, count);
            return result;
        }
    }

    /// <summary>Atomicky zapíše několik registrů a poté upozorní UI na změnu.</summary>
    public void Write(int address, ReadOnlySpan<ushort> values)
    {
        if (address < 0 || values.IsEmpty || address + values.Length > _registers.Length)
            throw new ArgumentOutOfRangeException(nameof(address));
        lock (_gate) values.CopyTo(_registers.AsSpan(address));
        // Událost musí přijít až po uvolnění zámku; UI při obnovení čte registry.
        Changed?.Invoke();
    }

    /// <summary>Atomicky změní vybrané bity podle Modbus FC22.</summary>
    public ushort MaskWrite(int address, ushort andMask, ushort orMask)
    {
        if (address is < 0 or > 65535) throw new ArgumentOutOfRangeException(nameof(address));
        ushort result;
        lock (_gate)
        {
            result = (ushort)((_registers[address] & andMask) | (orMask & ~andMask));
            _registers[address] = result;
        }
        Changed?.Invoke();
        return result;
    }

    /// <summary>Atomický snímek všech registrů patřících definovaným proměnným.</summary>
    public Dictionary<int, ushort> Capture(IEnumerable<Variable> variables)
    {
        var values = new Dictionary<int, ushort>();
        lock (_gate)
        {
            foreach (var variable in variables)
                for (var i = 0; i < variable.RegisterCount; i++)
                    values[variable.Address + i] = _registers[variable.Address + i];
        }
        return values;
    }

    /// <summary>Obnoví jen adresy platných proměnných; starý JSON bez mapy zůstává kompatibilní.</summary>
    public void Restore(IEnumerable<Variable> variables, IReadOnlyDictionary<int, ushort>? values)
    {
        if (values is null || values.Count == 0) return;
        lock (_gate)
        {
            foreach (var variable in variables)
                for (var i = 0; i < variable.RegisterCount; i++)
                    if (values.TryGetValue(variable.Address + i, out var value))
                        _registers[variable.Address + i] = value;
        }
        Changed?.Invoke();
    }

    /// <summary>Zapíše hodnotu proměnné; bitový BOOL mění jen určený bit.</summary>
    public void SetVariable(Variable variable)
    {
        var values = VariableCodec.Encode(variable);
        lock (_gate)
        {
            if (variable.Bit is int bit)
            {
                // BOOL s vybraným bitem nesmí přepsat ostatní bity téhož registru.
                var mask = (ushort)(1 << bit);
                _registers[variable.Address] = (ushort)((_registers[variable.Address] & ~mask) |
                    (values[0] != 0 ? mask : 0));
            }
            else values.AsSpan().CopyTo(_registers.AsSpan(variable.Address));
        }
        Changed?.Invoke();
    }

    /// <summary>Vynuluje registry proměnné, případně jen její sdílený BOOL bit.</summary>
    public void ClearVariable(Variable variable)
    {
        lock (_gate)
        {
            if (variable.Bit is int bit)
                _registers[variable.Address] &= (ushort)~(1 << bit);
            else
                _registers.AsSpan(variable.Address, variable.RegisterCount).Clear();
        }
        Changed?.Invoke();
    }
}

/// <summary>Validace a převod mezi typovanou hodnotou a 16bitovými slovy na síti.</summary>
public static class VariableCodec
{
    /// <summary>Ověří hodnotu, rozsah adres a překryv s existujícími proměnnými.</summary>
    public static void Validate(Variable variable, IEnumerable<Variable> existing)
    {
        // Konfigurační JSON může obsahovat null i přes nenulovatelný typ v C#.
        ArgumentNullException.ThrowIfNull(variable);
        if (string.IsNullOrWhiteSpace(variable.Name)) throw new ArgumentException("Zadejte název proměnné.");
        if (variable.Value is null) throw new ArgumentException("Hodnota proměnné nesmí být null.");
        if (variable.Length < 1 || variable.Length > 125) throw new ArgumentException("Délka musí být 1 až 125 registrů.");
        if (variable.Address < 0 || variable.Address + variable.RegisterCount > 65536)
            throw new ArgumentException("Adresa nebo rozsah registrů je mimo 0 až 65535.");
        if (variable.Bit is not null && (variable.Type != VariableType.Bool || variable.Bit is < 0 or > 15))
            throw new ArgumentException("Bit 0 až 15 lze zadat pouze pro BOOL.");
        foreach (var other in existing)
        {
            if (other.Name.Equals(variable.Name, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Název {variable.Name} již existuje.");
            if (variable.Address >= other.Address + other.RegisterCount || other.Address >= variable.Address + variable.RegisterCount)
                continue;
            // Různé BOOL bity mohou sdílet slovo; ostatní překryvy by se přepisovaly.
            if (variable.Type == VariableType.Bool && other.Type == VariableType.Bool &&
                variable.Bit is not null && other.Bit is not null && variable.Bit != other.Bit)
                continue;
            throw new ArgumentException($"Proměnná se překrývá s {other.Name} na registru {other.Address}.");
        }
        _ = Encode(variable);
    }

    /// <summary>Převede uživatelskou hodnotu na registry v pořadí očekávaném Modbus klientem.</summary>
    public static ushort[] Encode(Variable variable)
    {
        // Převod čísel používá invariantní zápis; REAL dovoluje i místní desetinnou čárku.
        var value = variable.Type is VariableType.String or VariableType.RegisterAscii or VariableType.DwordAscii
            ? variable.Value : variable.Value.Trim();
        switch (variable.Type)
        {
            case VariableType.Bool:
                if (value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1") return [1];
                if (value.Equals("false", StringComparison.OrdinalIgnoreCase) || value == "0") return [0];
                throw new ArgumentException("BOOL musí být 0, 1, false nebo true.");
            case VariableType.Int16:
                return [unchecked((ushort)short.Parse(value, CultureInfo.InvariantCulture))];
            case VariableType.UInt16:
                return [ushort.Parse(value, CultureInfo.InvariantCulture)];
            case VariableType.Int32:
            {
                // Víceslovná hodnota ukládá nejvyšší 16 bitů do prvního registru.
                var bits = unchecked((uint)int.Parse(value, CultureInfo.InvariantCulture));
                return [(ushort)(bits >> 16), (ushort)bits];
            }
            case VariableType.UInt32:
            {
                var bits = uint.Parse(value, CultureInfo.InvariantCulture);
                return [(ushort)(bits >> 16), (ushort)bits];
            }
            case VariableType.Float32:
            {
                if (!float.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out var number) &&
                    !float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                    throw new ArgumentException("REAL musí být číslo.");
                var bits = BitConverter.SingleToUInt32Bits(number);
                return [(ushort)(bits >> 16), (ushort)bits];
            }
            case VariableType.String:
            case VariableType.DwordAscii:
            {
                // Běžný ASCII text zabírá dva bajty na registr, vyšší bajt první.
                var bytes = Ascii(value);
                var count = variable.RegisterCount;
                if (bytes.Length > count * 2) throw new ArgumentException($"Text se nevejde do {count} registrů.");
                var words = new ushort[count];
                for (var i = 0; i < bytes.Length; i++)
                    words[i / 2] |= (ushort)(bytes[i] << (i % 2 == 0 ? 8 : 0));
                return words;
            }
            case VariableType.RegisterAscii:
            {
                // RegisterAscii používá pouze vyšší bajt každého registru.
                var bytes = Ascii(value);
                if (bytes.Length > variable.Length) throw new ArgumentException($"Text se nevejde do {variable.Length} registrů.");
                var words = new ushort[variable.Length];
                for (var i = 0; i < bytes.Length; i++) words[i] = (ushort)(bytes[i] << 8);
                return words;
            }
            default: throw new ArgumentOutOfRangeException(nameof(variable.Type));
        }
    }

    /// <summary>Převede snímek registrů zpět na hodnotu zobrazovanou v editoru.</summary>
    public static string Decode(Variable variable, ushort[] words)
    {
        // Stejné pořadí slov jako při Encode; znaménko mění jen výklad bitů Int32.
        var bits = ((uint)words[0] << 16) | (words.Length > 1 ? words[1] : 0u);
        return variable.Type switch
        {
            VariableType.Bool => ((variable.Bit is int bit ? (words[0] >> bit) & 1 : words[0] != 0 ? 1 : 0) != 0) ? "1" : "0",
            VariableType.Int16 => unchecked((short)words[0]).ToString(CultureInfo.InvariantCulture),
            VariableType.UInt16 => words[0].ToString(CultureInfo.InvariantCulture),
            VariableType.Int32 => unchecked((int)bits).ToString(CultureInfo.InvariantCulture),
            VariableType.UInt32 => bits.ToString(CultureInfo.InvariantCulture),
            VariableType.Float32 => BitConverter.UInt32BitsToSingle(bits).ToString("G9", CultureInfo.CurrentCulture),
            VariableType.String or VariableType.DwordAscii => DecodeText(words, false),
            VariableType.RegisterAscii => DecodeText(words, true),
            _ => throw new ArgumentOutOfRangeException(nameof(variable.Type))
        };
    }

    private static byte[] Ascii(string value)
    {
        if (value.Any(c => c > 127))
            throw new ArgumentException("Textové registry podporují pouze ASCII znaky.");
        return Encoding.ASCII.GetBytes(value);
    }

    private static string DecodeText(ushort[] words, bool highByteOnly)
    {
        // Nulový bajt ukončuje běžný text; register_ascii vynechává prázdné registry.
        var bytes = highByteOnly
            ? words.Select(w => (byte)(w >> 8)).ToArray()
            : words.SelectMany(w => new[] { (byte)(w >> 8), (byte)w }).ToArray();
        return highByteOnly
            ? Encoding.ASCII.GetString(bytes.Where(b => b != 0).ToArray())
            : Encoding.ASCII.GetString(bytes.TakeWhile(b => b != 0).ToArray());
    }
}
