using System.Text.Json;

namespace SpaceSpreadsheetEmulator.StaticData;

internal static class JsonStaticData
{
    public static string ReadText(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value))
        {
            return string.Empty;
        }

        if (value.ValueKind is JsonValueKind.String)
        {
            return value.GetString() ?? string.Empty;
        }

        return value.ValueKind is JsonValueKind.Object
            && value.TryGetProperty("en", out JsonElement english)
            && english.ValueKind is JsonValueKind.String
                ? english.GetString() ?? string.Empty
                : string.Empty;
    }

    public static long? ReadInt64(JsonElement root, string propertyName)
        => root.TryGetProperty(propertyName, out JsonElement value)
            && value.ValueKind is JsonValueKind.Number
                ? value.GetInt64()
                : null;

    public static int? ReadInt32(JsonElement root, string propertyName)
        => root.TryGetProperty(propertyName, out JsonElement value)
            && value.ValueKind is JsonValueKind.Number
                ? value.GetInt32()
                : null;

    public static double? ReadDouble(JsonElement root, string propertyName)
        => root.TryGetProperty(propertyName, out JsonElement value)
            && value.ValueKind is JsonValueKind.Number
                ? value.GetDouble()
                : null;

    public static bool ReadBoolean(JsonElement root, string propertyName)
        => root.TryGetProperty(propertyName, out JsonElement value)
            && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            && value.GetBoolean();
}
