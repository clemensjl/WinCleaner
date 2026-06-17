using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinCleaner.Util;

/// <summary>
/// Zentrale, einheitliche JSON-Ausgabe nach stdout. Lesbare Pfade
/// (UnsafeRelaxedJsonEscaping) und Enums als Namen statt Zahlen.
/// </summary>
public static class JsonOut
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    public static void Write(object data) =>
        Console.WriteLine(JsonSerializer.Serialize(data, Options));
}
