using System.Text.Json;

namespace SignalRMVC.FreezeMonitor.Helpers;

public static class JsonFileHelper
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    public static async Task WriteAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, value, SerializerOptions, cancellationToken);
    }
}
