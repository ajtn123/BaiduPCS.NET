using System.Globalization;
using System.Text.Json;

namespace BaiduPCS.Netdisk;

internal static class ResponseJson
{
    internal static JsonElement Field(this JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out JsonElement field) ? field : default;

    internal static string Text(this JsonElement root, string name)
    {
        JsonElement field = root.Field(name);
        return field.ValueKind switch
        {
            JsonValueKind.String => field.GetString() ?? "",
            JsonValueKind.Number => field.GetRawText(),
            _ => "",
        };
    }

    internal static long Number(this JsonElement root, string name, long fallback = 0) =>
        long.TryParse(root.Text(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out long number) ? number : fallback;

    internal static IEnumerable<JsonElement> Items(this JsonElement root, string name)
    {
        JsonElement field = root.Field(name);
        return field.ValueKind == JsonValueKind.Array ? field.EnumerateArray() : [];
    }
}
