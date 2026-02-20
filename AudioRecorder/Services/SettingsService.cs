using System.IO;
using System.Text.Json;
using AudioRecorder.Models;

namespace AudioRecorder.Services;

public class SettingsService
{
    public static UserSettings Settings => new Lazy<UserSettings>(LoadSettings()).Value;
    
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AudioRecorder",
        "settings.json"
    );

    private static UserSettings LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                string json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<UserSettings>(json) ?? new UserSettings();
            }
        }
        catch
        {
            // In caso di errore, restituisci impostazioni predefinite
        }
        return new UserSettings();
    }

    public static void SaveSettings(UserSettings settings)
    {
        try
        {
            string? directory = Path.GetDirectoryName(SettingsPath);
            if (directory != null && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Ignora errori di salvataggio
        }
    }
}
