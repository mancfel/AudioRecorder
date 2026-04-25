using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using AudioRecorder.Services.Interfaces;

namespace AudioRecorder.Services;

public sealed class TranscriptionService(ISettingsService settingsService) : ITranscriptionService
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly StringBuilder _startupErrorBuffer = new();
    private CancellationTokenSource? _cts;
    private TaskCompletionSource<bool>? _initTcs;
    private string? _language;
    private string? _modelPath;
    private Process? _process;
    private TaskCompletionSource<bool>? _processingTcs;
    private BinaryWriter? _stdin;

    public event EventHandler<string>? TranscriptionReceived;

    public async Task InitializeAsync()
    {
        var userSettings = settingsService.Settings;
        var currentModelPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AudioRecorder",
            userSettings.WhisperModel
        );

        if (!File.Exists(currentModelPath))
            throw new FileNotFoundException(ResourceManager.GetText("WhisperModelNotFound"), currentModelPath);

        if (_process is { HasExited: false })
        {
            if (currentModelPath == _modelPath && userSettings.TranscriptLanguage == _language)
                return;
            Stop();
        }

        _modelPath = currentModelPath;
        _language = userSettings.TranscriptLanguage;
        var libraries = string.Join(",", userSettings.RuntimeLibraryOrder);

        // Path to the external transcription executable
        var exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AudioRecorder.Transcription.exe");

        // In development, it might be in a different folder. 
        if (!File.Exists(exePath))
            throw new FileNotFoundException(ResourceManager.GetText("TranscriptionExeNotFound"), exePath);

        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = $"\"{_modelPath}\" {_language} {libraries}",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        _initTcs = new TaskCompletionSource<bool>();
        _process = new Process { StartInfo = startInfo };

        try
        {
            if (!_process.Start()) throw new Exception("Failed to start transcription process.");
        }
        catch (Exception ex)
        {
            throw new Exception($"Error starting transcription process: {ex.Message}", ex);
        }

        _stdin = new BinaryWriter(_process.StandardInput.BaseStream);
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => ReadOutputAsync(_cts.Token));

        lock (_startupErrorBuffer)
        {
            _startupErrorBuffer.Clear();
        }
        // Error logging
        var errorLogTask = Task.Run(async () =>
        {
            if (_process == null) return;
            using var errorReader = _process.StandardError;
            while (await errorReader.ReadLineAsync() is { } line)
            {
                Debug.WriteLine($"[Transcription] {line}");
                if (_initTcs.Task.IsCompleted) continue;
                lock (_startupErrorBuffer)
                {
                    _startupErrorBuffer.AppendLine(line);
                }
            }
        });

        // Wait for the "Ready" signal or process exit
        var processExitTask = _process.WaitForExitAsync();
        var completedTask = await Task.WhenAny(_initTcs.Task, processExitTask);

        if (completedTask == processExitTask || _process.HasExited)
        {
            _initTcs.TrySetCanceled();
            await errorLogTask; // Wait for error reader to finish reading remaining errors
            string detailedError;
            lock (_startupErrorBuffer)
            {
                detailedError = _startupErrorBuffer.ToString();
            }

            throw new Exception($"Transcription process exited before initialization was complete. {detailedError}");
        }

        await _initTcs.Task;
    }

    public void Stop()
    {
        if (_process == null || _process.HasExited) return;

        try
        {
            _stdin?.Dispose();
            _stdin = null;

            if (!_process.WaitForExit(3000)) _process.Kill();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error stopping transcription process: {ex.Message}");
        }
        finally
        {
            _cts?.Cancel();
            _process?.Dispose();
            _process = null;
        }
    }

    public async Task ProcessAudioAsync(float[] samples, Action<string>? onSegmentReceived = null)
    {
        // Simple peak check to skip silent segments and avoid hallucinations
        var maxPeak = samples.Select(Math.Abs).Prepend(0f).Max();
        if (maxPeak < 0.005f) return;

        if (_process == null || _process.HasExited || _stdin == null) return;

        await _semaphore.WaitAsync();
        try
        {
            _processingTcs = new TaskCompletionSource<bool>();

            // Register callback for segments
            void Handler(object? s, string text)
            {
                onSegmentReceived?.Invoke(text);
            }

            TranscriptionReceived += Handler;

            try
            {
                _stdin.Write(samples.Length);
                foreach (var sample in samples) _stdin.Write(sample);
                _stdin.Flush();

                // Wait for the "Done" signal from the process
                await _processingTcs.Task;
            }
            finally
            {
                TranscriptionReceived -= Handler;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error during audio processing: {ex.Message}");
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void Dispose()
    {
        _semaphore.Dispose();
        _cts?.Dispose();
        _process?.Dispose();
        _stdin?.Dispose();
        Stop();
    }

    private async Task ReadOutputAsync(CancellationToken ct)
    {
        if (_process == null) return;
        using var reader = _process.StandardOutput;

        while (!ct.IsCancellationRequested && await reader.ReadLineAsync(ct) is { } line)
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                if (doc.RootElement.TryGetProperty("Text", out var textProp))
                {
                    TranscriptionReceived?.Invoke(this, textProp.GetString() ?? "");
                }
                else if (doc.RootElement.TryGetProperty("Status", out var statusProp))
                {
                    var status = statusProp.GetString();
                    switch (status)
                    {
                        case "Ready":
                            _initTcs?.TrySetResult(true);
                            break;
                        case "Done":
                            _processingTcs?.TrySetResult(true);
                            break;
                    }
                }
            }
            catch (JsonException)
            {
                // Ignore invalid JSON
            }
        }
    }
}