using System.Text.Json;

namespace MicToggle;

internal sealed class OutputVolumeStore
{
    private const int DefaultVolumePercent = 100;
    private readonly string _settingsPath;

    public OutputVolumeStore(string settingsPath)
    {
        _settingsPath = settingsPath;
    }

    public int Load()
    {
        if (!File.Exists(_settingsPath))
        {
            return DefaultVolumePercent;
        }

        try
        {
            using var stream = File.OpenRead(_settingsPath);
            var settings = JsonSerializer.Deserialize<Settings>(stream);
            return Math.Clamp(
                settings?.OutputVolumePercent ?? DefaultVolumePercent,
                0,
                100);
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or JsonException)
        {
            return DefaultVolumePercent;
        }
    }

    public void Save(int volumePercent)
    {
        var directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var settings = new Settings(Math.Clamp(volumePercent, 0, 100));
        var temporaryPath = $"{_settingsPath}.tmp";
        File.WriteAllText(
            temporaryPath,
            JsonSerializer.Serialize(settings));
        File.Move(temporaryPath, _settingsPath, overwrite: true);
    }

    private sealed record Settings(int OutputVolumePercent);
}
