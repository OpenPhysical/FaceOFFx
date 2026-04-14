using System.Text.Json;

namespace FaceOFFx.Cli.Services;

internal static class DatasetResultValueParser
{
    public static double? TryGetDouble(object? value)
    {
        return value switch
        {
            null => null,
            double number => number,
            float number => number,
            decimal number => (double)number,
            int number => number,
            long number => number,
            string text when double.TryParse(text, out var parsed) => parsed,
            JsonElement element => TryGetDouble(element),
            _ => null
        };
    }

    public static string? TryGetString(object? value)
    {
        return value switch
        {
            null => null,
            string text => text,
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString(),
            JsonElement element => element.ToString(),
            _ => value.ToString()
        };
    }

    public static JsonElement? TryGetJsonElement(object? value)
    {
        return value switch
        {
            JsonElement element => element,
            null => null,
            _ => JsonSerializer.SerializeToElement(value)
        };
    }

    private static double? TryGetDouble(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Number when element.TryGetDouble(out var number) => number,
            JsonValueKind.String when double.TryParse(element.GetString(), out var parsed) => parsed,
            _ => null
        };
    }
}
