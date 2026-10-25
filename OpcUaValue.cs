using System.Globalization;
using Opc.Ua;

namespace ModbusServer;

/// <summary>Podporované skalární datové typy OPC UA.</summary>
internal enum OpcUaValueType { Boolean, Int16, UInt16, Int32, UInt32, Int64, UInt64, Float, Double, String }

/// <summary>Převody hodnot z editoru a validace NodeId odděleného od namespace URI.</summary>
internal static class OpcUaValue
{
    /// <summary>Vrací invariantní text; Boolean musí zůstat v zápisu, který přijímá editor.</summary>
    public static string Format(object value) => value switch
    {
        bool boolean => boolean ? "true" : "false",
        IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
        string text => text,
        _ => throw new ArgumentException("Nepodporovaný skalární typ OPC UA.")
    };

    /// <summary>Odmítne hodnoty mimo rozsah, NaN, nekonečno a příliš dlouhý text.</summary>
    public static object Parse(OpcUaValueType type, string text)
    {
        var culture = CultureInfo.InvariantCulture;
        var value = text.Trim();
        return type switch
        {
            OpcUaValueType.Boolean when value is "true" or "1" => true,
            OpcUaValueType.Boolean when value is "false" or "0" => false,
            OpcUaValueType.Int16 => short.Parse(value, NumberStyles.Integer, culture),
            OpcUaValueType.UInt16 => ushort.Parse(value, NumberStyles.Integer, culture),
            OpcUaValueType.Int32 => int.Parse(value, NumberStyles.Integer, culture),
            OpcUaValueType.UInt32 => uint.Parse(value, NumberStyles.Integer, culture),
            OpcUaValueType.Int64 => long.Parse(value, NumberStyles.Integer, culture),
            OpcUaValueType.UInt64 => ulong.Parse(value, NumberStyles.Integer, culture),
            OpcUaValueType.Float => Finite(float.Parse(value, NumberStyles.Float, culture)),
            OpcUaValueType.Double => Finite(double.Parse(value, NumberStyles.Float, culture)),
            OpcUaValueType.String when text.EnumerateRunes().Count() <= 255 => text,
            OpcUaValueType.String => throw new ArgumentException("Text smí mít nejvýše 255 znaků."),
            _ => throw new ArgumentException("Neplatná hodnota typu Boolean. Použijte true/false nebo 1/0.")
        };
    }

    private static float Finite(float value) => float.IsFinite(value) ? value : throw new ArgumentException("Hodnota musí být konečné číslo.");
    private static double Finite(double value) => double.IsFinite(value) ? value : throw new ArgumentException("Hodnota musí být konečné číslo.");

    public static NodeId DataType(OpcUaValueType type) => type switch
    {
        OpcUaValueType.Boolean => DataTypeIds.Boolean,
        OpcUaValueType.Int16 => DataTypeIds.Int16,
        OpcUaValueType.UInt16 => DataTypeIds.UInt16,
        OpcUaValueType.Int32 => DataTypeIds.Int32,
        OpcUaValueType.UInt32 => DataTypeIds.UInt32,
        OpcUaValueType.Int64 => DataTypeIds.Int64,
        OpcUaValueType.UInt64 => DataTypeIds.UInt64,
        OpcUaValueType.Float => DataTypeIds.Float,
        OpcUaValueType.Double => DataTypeIds.Double,
        _ => DataTypeIds.String
    };

    /// <summary>Přijímá jen identifikátor i/s/g/b; namespace index dodá aktuální session.</summary>
    public static NodeId Node(string identifier, ushort namespaceIndex)
    {
        if (identifier.StartsWith("ns=", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Zadejte NodeId bez ns=; namespace se zadává jako URI.");
        if (identifier.StartsWith("i=", StringComparison.Ordinal) &&
            identifier.AsSpan(2).IndexOfAnyExceptInRange('0', '9') < 0 &&
            uint.TryParse(identifier[2..], NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0)
            return new NodeId(number, namespaceIndex);
        if (identifier.StartsWith("s=", StringComparison.Ordinal) && identifier.Length > 2)
            return new NodeId(identifier[2..], namespaceIndex);
        if (identifier.StartsWith("g=", StringComparison.Ordinal) && Guid.TryParse(identifier[2..], out var guid))
            return new NodeId(guid, namespaceIndex);
        if (identifier.StartsWith("b=", StringComparison.Ordinal) && identifier.Length > 2)
        {
            try { return new NodeId(Convert.FromBase64String(identifier[2..]), namespaceIndex); }
            catch (FormatException) { }
        }
        throw new ArgumentException("NodeId musí být i=kladné číslo, s=text, g=UUID nebo b=base64.");
    }
}
