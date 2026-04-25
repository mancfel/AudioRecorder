namespace AudioRecorder.Services.Interfaces;

public interface ITranscriptionService : IDisposable
{
    Task InitializeAsync();
    void Stop();
    Task ProcessAudioAsync(float[] samples, Action<string>? onSegmentReceived = null);
}