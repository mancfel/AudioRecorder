using AudioRecorder.Models;
using AudioRecorder.Models.Enums;

namespace AudioRecorder.Services.Interfaces;

public interface IAudioRecorderService : IDisposable
{
    bool IsRecording { get; }
    event EventHandler<string>? StatusChanged;
    event EventHandler<(float MicLevel, float SysLevel)>? LevelsUpdated;
    event EventHandler<(TranscriptionSource Source, string Text)>? TranscriptionReceived;
    Task StartRecordingAsync(int micDeviceNumber, string? systemDeviceId);
    void StopRecording();
    Task SaveRecordingAsync(string filePath);
}