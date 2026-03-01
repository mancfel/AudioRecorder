using System.IO;
using System.Windows;
using AudioRecorder.Models;
using Whisper.net;

namespace AudioRecorder.Services;

public class TranscriptionService : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly UserSettings _userSettings;
    private bool _isInitialized;
    private string _modelPath;
    private WhisperProcessor? _processor;
    private WhisperFactory? _whisperFactory;

    public TranscriptionService()
    {
        _userSettings = SettingsService.Settings;
        _modelPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AudioRecorder",
            _userSettings.WhisperModel
        );
    }

    public void Dispose()
    {
        _semaphore.Dispose();
        DisposeInternal();
    }

    public event EventHandler<string>? TranscriptionReceived;

    public void InitializeAsync()
    {
        // If the model has changed in the settings in the meantime, we need to reinitialize
        var currentModelPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AudioRecorder",
            _userSettings.WhisperModel
        );

        switch (_isInitialized)
        {
            case true when currentModelPath == _modelPath:
                _processor!.ChangeLanguage(_userSettings.Language);
                return;
            case true:
                DisposeInternal();
                _isInitialized = false;
                break;
        }

        EnsureModelExists(currentModelPath);

        _whisperFactory = WhisperFactory.FromPath(currentModelPath);
        _processor = _whisperFactory.CreateBuilder()
            .WithLanguage(_userSettings.Language)
            .WithPrintTimestamps()
            .WithNoSpeechThreshold(0.6f)
            .Build();

        // Update the path of the currently loaded model
        _modelPath = currentModelPath;
        _isInitialized = true;
    }

    private string GetText(string key)
    {
        if (Application.Current == null) return key;
        return Application.Current.Dispatcher.CheckAccess()
            ? Application.Current.TryFindResource(key) as string ?? key
            : Application.Current.Dispatcher.Invoke(() => Application.Current.TryFindResource(key) as string ?? key);
    }

    private void EnsureModelExists(string path)
    {
        if (File.Exists(path)) return;
        
        var directory = Path.GetDirectoryName(path);
        if (directory != null && !Directory.Exists(directory)) Directory.CreateDirectory(directory);

        // Manual loading removed due to API instability, the user must provide the model
        // or we will implement a more robust downloader later.
        throw new FileNotFoundException(GetText("WhisperModelNotFound"), path);
    }

    public async Task ProcessAudioAsync(float[] samples, Action<string>? onSegmentReceived = null)
    {
        // Simple peak check to skip silent segments and avoid hallucinations
        var maxPeak = samples.Select(Math.Abs).Prepend(0f).Max();

        // Threshold for silence detection (can be adjusted)
        if (maxPeak < 0.005f) return;

        await _semaphore.WaitAsync();
        try
        {
            await foreach (var result in _processor!.ProcessAsync(samples))
            {
                onSegmentReceived?.Invoke(result.Text);
                TranscriptionReceived?.Invoke(this, result.Text);
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private void DisposeInternal()
    {
        _processor?.Dispose();
        _whisperFactory?.Dispose();
        _processor = null;
        _whisperFactory = null;
    }
}