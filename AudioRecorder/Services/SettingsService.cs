using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AudioRecorder.Models;
using AudioRecorder.Services.Interfaces;

namespace AudioRecorder.Services;

public class SettingsService : ISettingsService
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AudioRecorder",
        "settings.json"
    );

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private UserSettings? _settings;

    public UserSettings Settings => _settings ??= LoadSettings();

    public void SaveSettings(UserSettings settings)
    {
        _settings = settings;
        try
        {
            var directory = Path.GetDirectoryName(SettingsPath);
            if (directory != null && !Directory.Exists(directory)) Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(settings, SerializerOptions);
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Ignore saving errors
        }
    }

    private UserSettings LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<UserSettings>(json, SerializerOptions) ?? new UserSettings();
            }
        }
        catch
        {
            // In case of error, return default settings
            return new UserSettings();
        }

        return new UserSettings();
    }
}