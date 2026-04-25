using AudioRecorder.Models;

namespace AudioRecorder.Services.Interfaces;

public interface ISettingsService
{
    UserSettings Settings { get; }
    void SaveSettings(UserSettings settings);
}