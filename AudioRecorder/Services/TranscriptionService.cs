using System.IO;
using AudioRecorder.Models;
using Whisper.net;
using Whisper.net.Ggml;
using System.Windows;

namespace AudioRecorder.Services;

public class TranscriptionService : IDisposable
{
    private WhisperFactory? whisperFactory;
    private WhisperProcessor? processor;
    private string modelPath;
    private bool isInitialized;
    private readonly UserSettings userSettings;
    private readonly SemaphoreSlim semaphore = new(1, 1);

    public event EventHandler<string>? TranscriptionReceived;

    public TranscriptionService()
    {
        userSettings = SettingsService.Settings;
        modelPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AudioRecorder",
            userSettings.WhisperModel
        );
    }

    public void InitializeAsync()
    {
        // If the model has changed in the settings in the meantime, we need to reinitialize
        string currentModelPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AudioRecorder",
            userSettings.WhisperModel
        );

        switch (isInitialized)
        {
            case true when currentModelPath == modelPath:
                processor!.ChangeLanguage(userSettings.Language);
                return;
            case true:
                DisposeInternal();
                isInitialized = false;
                break;
        }

        EnsureModelExists(currentModelPath);

        whisperFactory = WhisperFactory.FromPath(currentModelPath);
        processor = whisperFactory.CreateBuilder()
            .WithLanguage(userSettings.Language)
            .WithPrintTimestamps()
            .Build();
        
        // Update the path of the currently loaded model
        modelPath = currentModelPath;
        isInitialized = true;
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
        if (!File.Exists(path))
        {
            string? directory = Path.GetDirectoryName(path);
            if (directory != null && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Manual loading removed due to API instability, the user must provide the model
            // or we will implement a more robust downloader later.
            throw new FileNotFoundException(GetText("WhisperModelNotFound"), path);
        }
    }

    public async Task ProcessAudioAsync(float[] samples, Action<string>? onSegmentReceived = null)
    {
        await semaphore.WaitAsync();
        try
        {
            await foreach (var result in processor!.ProcessAsync(samples))
            {
                onSegmentReceived?.Invoke(result.Text);
                TranscriptionReceived?.Invoke(this, result.Text);
            }
        }
        finally
        {
            semaphore.Release();
        }
    }

    public void Dispose()
    {
        semaphore.Dispose();
        DisposeInternal();
    }

    private void DisposeInternal()
    {
        processor?.Dispose();
        whisperFactory?.Dispose();
        processor = null;
        whisperFactory = null;
    }
}
