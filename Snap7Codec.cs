using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ModbusServer;

public enum Snap7ValueType { Bool, Int16, Dword, DwordAscii }

public readonly record struct Snap7Address(int Db, int Byte, int Bit);

/// <summary>Adresování DB ve tvaru DBX/DBW/DBD nebo samotným byte offsetem.</summary>
public static class Snap7Codec
{
    private static readonly Regex AddressPattern = new(
        @"^(?:DB(?<db>\d+)\.)?(?<area>DBX|DBW|DBD)?(?<byte>\d+)(?:\.(?<bit>[0-7]))?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static int Size(Snap7ValueType type) => type switch
    {
        Snap7ValueType.Bool => 1,
        Snap7ValueType.Int16 => 2,
        Snap7ValueType.Dword or Snap7ValueType.DwordAscii => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    public static Snap7Address Parse(string text, int defaultDb, Snap7ValueType type)
    {
        var match = AddressPattern.Match(text.Trim());
        if (!match.Success || !int.TryParse(match.Groups["byte"].Value, out var offset))
            throw new ArgumentException("Adresa musí být například 10.2, DB1.DBW12 nebo DB1.DBD20.");
        var db = defaultDb;
        if (match.Groups["db"].Success && !int.TryParse(match.Groups["db"].Value, out db))
            throw new ArgumentException("Neplatné číslo DB.");
        if (db < 1 || db > 65535 || offset < 0 || offset > 65536 - Size(type))
            throw new ArgumentException("DB nebo byte offset je mimo povolený rozsah.");

        var area = match.Groups["area"].Value.ToUpperInvariant();
        var bitSpecified = match.Groups["bit"].Success;
        if (type == Snap7ValueType.Bool)
        {
            if (area is not ("" or "DBX")) throw new ArgumentException("BOOL používá DBX a bit 0–7.");
        }
        else if (bitSpecified || area == "DBX" ||
                 (type == Snap7ValueType.Int16 && area == "DBD") ||
                 (type is Snap7ValueType.Dword or Snap7ValueType.DwordAscii && area == "DBW"))
            throw new ArgumentException("Tvar adresy neodpovídá vybranému typu.");

        return new Snap7Address(db, offset, bitSpecified ? int.Parse(match.Groups["bit"].Value, CultureInfo.InvariantCulture) : 0);
    }

    public static string Decode(ReadOnlySpan<byte> data, Snap7ValueType type, int bit) => type switch
    {
        Snap7ValueType.Bool => ((data[0] >> bit) & 1) == 0 ? "0" : "1",
        Snap7ValueType.Int16 => BinaryPrimitives.ReadInt16BigEndian(data).ToString(CultureInfo.InvariantCulture),
        Snap7ValueType.Dword => BinaryPrimitives.ReadUInt32BigEndian(data).ToString(CultureInfo.InvariantCulture),
        Snap7ValueType.DwordAscii => Encoding.ASCII.GetString(data).TrimEnd('\0', ' '),
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    public static byte[] Encode(string value, Snap7ValueType type)
    {
        var result = new byte[Size(type)];
        switch (type)
        {
            case Snap7ValueType.Bool:
                if (value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase) || value.Trim() == "1") result[0] = 1;
                else if (value.Trim().Equals("false", StringComparison.OrdinalIgnoreCase) || value.Trim() == "0") result[0] = 0;
                else throw new ArgumentException("BOOL musí být 0, 1, false nebo true.");
                break;
            case Snap7ValueType.Int16:
                BinaryPrimitives.WriteInt16BigEndian(result, short.Parse(value, CultureInfo.InvariantCulture));
                break;
            case Snap7ValueType.Dword:
                BinaryPrimitives.WriteUInt32BigEndian(result, uint.Parse(value, CultureInfo.InvariantCulture));
                break;
            case Snap7ValueType.DwordAscii:
                if (value.Length > 4 || value.Any(c => c > 127))
                    throw new ArgumentException("DWORD_ASCII přijímá nejvýše čtyři ASCII znaky.");
                Encoding.ASCII.GetBytes(value).CopyTo(result, 0);
                for (var i = value.Length; i < 4; i++) result[i] = (byte)' ';
                break;
            default: throw new ArgumentOutOfRangeException(nameof(type));
        }
        return result;
    }
}
