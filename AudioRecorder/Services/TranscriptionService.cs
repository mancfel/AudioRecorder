using System.IO;
using AudioRecorder.Models;
using Whisper.net;
using Whisper.net.Ggml;

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
        // Se il modello nel frattempo è cambiato nelle impostazioni, dobbiamo reinizializzare
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
        
        // Aggiorniamo il path del modello attualmente caricato
        modelPath = currentModelPath;
        isInitialized = true;
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

            // Caricamento manuale rimosso per instabilità API, l'utente deve fornire il modello
            // o implementeremo un downloader più robusto in seguito.
            throw new FileNotFoundException("Modello Whisper non trovato. Scaricare il file .bin in AppData/AudioRecorder", path);
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
