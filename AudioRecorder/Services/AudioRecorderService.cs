using System.Diagnostics;
using System.IO;
using AudioRecorder.Models.Enums;
using AudioRecorder.Services.Interfaces;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace AudioRecorder.Services;

public class AudioRecorderService(ISettingsService settingsService, ITranscriptionService transcriptionService)
    : IAudioRecorderService
{
    private static readonly string DocumentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    private static readonly string BasePath = Path.Combine(DocumentsPath, "AudioRecorder");
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly Stopwatch _timer = new();
    private string? _currentFilePath;

    private float _currentMicLevel;
    private float _currentSysLevel;
    private bool _isMicTranscribing;
    private bool _isSysTranscribing;
    private MediaFoundationResampler? _micResampler;
    private WaveInEvent? _microphoneCapture;
    private string? _microphoneFilePath;
    private WaveFileWriter? _microphoneWriter;

    private BufferedWaveProvider? _micWhisperBuffer;
    private MediaFoundationResampler? _sysResampler;
    private WasapiLoopbackCapture? _systemCapture;
    private string? _systemFilePath;
    private WaveFileWriter? _systemWriter;

    private BufferedWaveProvider? _sysWhisperBuffer;
    private string? _transcriptionFilePath;
    private StreamWriter? _transcriptionWriter;

    private WaveFormat? _whisperFormat;

    public bool IsRecording { get; private set; }

    public event EventHandler<string>? StatusChanged;
    public event EventHandler<(float MicLevel, float SysLevel)>? LevelsUpdated;
    public event EventHandler<(TranscriptionSource Source, string Text)>? TranscriptionReceived;

    public async Task StartRecordingAsync(int micDeviceNumber, string? systemDeviceId)
    {
        await _semaphore.WaitAsync();
        try
        {
            if (IsRecording) return;

            try
            {
                _currentFilePath = Path.Combine(BasePath, $"recording_{DateTime.Now:yyyyMMddHHmmss}");

                Directory.CreateDirectory(_currentFilePath);

                SetupSystemCapture(systemDeviceId);
                SetupMicrophoneCapture(micDeviceNumber, _systemCapture!.WaveFormat);

                _microphoneFilePath = Path.Combine(_currentFilePath, "mic.wav");
                _systemFilePath = Path.Combine(_currentFilePath, "sys.wav");
                _transcriptionFilePath = Path.Combine(_currentFilePath, "transcript.txt");

                _microphoneWriter = new WaveFileWriter(_microphoneFilePath, _systemCapture.WaveFormat);
                _systemWriter = new WaveFileWriter(_systemFilePath, _systemCapture.WaveFormat);
                _transcriptionWriter = new StreamWriter(_transcriptionFilePath, false) { AutoFlush = true };

                if (settingsService.Settings.TranscriptEnabled)
                    await transcriptionService.InitializeAsync();

                _microphoneCapture?.StartRecording();
                _systemCapture?.StartRecording();
                _timer.Restart();

                _whisperFormat = new WaveFormat(16000, 16, 1);

                _micWhisperBuffer = new BufferedWaveProvider(_microphoneCapture?.WaveFormat)
                {
                    DiscardOnBufferOverflow = true,
                    BufferDuration = TimeSpan.FromSeconds(10)
                };
                _micResampler = new MediaFoundationResampler(_micWhisperBuffer, _whisperFormat);

                _sysWhisperBuffer = new BufferedWaveProvider(_systemCapture?.WaveFormat)
                {
                    DiscardOnBufferOverflow = true,
                    BufferDuration = TimeSpan.FromSeconds(10)
                };
                _sysResampler = new MediaFoundationResampler(_sysWhisperBuffer, _whisperFormat);

                IsRecording = true;
                StatusChanged?.Invoke(this, string.Format(ResourceManager.GetText("RecordingInProgress"), micDeviceNumber));
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, string.Format(ResourceManager.GetText("StartError"), ex.Message));
                CleanupRecording();
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void StopRecording()
    {
        _semaphore.Wait();
        try
        {
            if (!IsRecording) return;

            _microphoneCapture?.StopRecording();
            _systemCapture?.StopRecording();
            _timer.Stop();

            _transcriptionWriter?.Flush();
            _transcriptionWriter?.Dispose();
            _transcriptionWriter = null;

            if (settingsService.Settings.TranscriptEnabled)
                transcriptionService.Stop();

            IsRecording = false;
            StatusChanged?.Invoke(this, ResourceManager.GetText("RecordingStoppedReadyToSave"));
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task SaveRecordingAsync(string filePath)
    {
        if (IsRecording)
            throw new InvalidOperationException(ResourceManager.GetText("StopBeforeSaveError"));

        if (string.IsNullOrEmpty(_microphoneFilePath) || !File.Exists(_microphoneFilePath))
            throw new InvalidOperationException(ResourceManager.GetText("NoMicRecordingAvailable"));

        if (string.IsNullOrEmpty(_systemFilePath) || !File.Exists(_systemFilePath))
            throw new InvalidOperationException(ResourceManager.GetText("NoSysRecordingAvailable"));

        await Task.Run(() => MixingService.MixAndSaveFiles(filePath, _microphoneFilePath, _systemFilePath));

        // Also save the transcription if present
        if (!string.IsNullOrEmpty(_transcriptionFilePath) && File.Exists(_transcriptionFilePath))
            try
            {
                var transcriptDest = Path.ChangeExtension(filePath, ".txt");
                File.Copy(_transcriptionFilePath, transcriptDest, true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving transcription: {ex.Message}");
            }

        StatusChanged?.Invoke(this, string.Format(ResourceManager.GetText("FileSaved"), Path.GetFileName(filePath)));
    }

    public void Dispose()
    {
        _semaphore.Dispose();
        _micResampler?.Dispose();
        _microphoneCapture?.Dispose();
        _microphoneWriter?.Dispose();
        _sysResampler?.Dispose();
        _systemCapture?.Dispose();
        _systemWriter?.Dispose();
        _transcriptionWriter?.Dispose();
        transcriptionService.Dispose();
        StopRecording();
        CleanupRecording();
        GC.SuppressFinalize(this);
    }

    private void SetupMicrophoneCapture(int deviceNumber, WaveFormat waveFormat)
    {
        _microphoneCapture = new WaveInEvent
        {
            DeviceNumber = deviceNumber,
            WaveFormat = waveFormat,
            BufferMilliseconds = 50
        };

        _microphoneCapture.DataAvailable += OnMicrophoneDataAvailable;
        _microphoneCapture.RecordingStopped += (_, e) =>
        {
            _microphoneWriter?.Dispose();
            _microphoneWriter = null;
            _microphoneCapture?.Dispose();
            if (e.Exception != null)
                StatusChanged?.Invoke(this, $"Microphone recording error: {e.Exception.Message}");
        };
    }

    private void SetupSystemCapture(string? systemDeviceId)
    {
        if (string.IsNullOrEmpty(systemDeviceId))
        {
            _systemCapture = new WasapiLoopbackCapture();
        }
        else
        {
            using var enumerator = new MMDeviceEnumerator();
            var device = enumerator.GetDevice(systemDeviceId);
            _systemCapture = new WasapiLoopbackCapture(device);
        }

        // Explicit configuration to avoid format issues
        _systemCapture.ShareMode = AudioClientShareMode.Shared;

        _systemCapture.DataAvailable += OnSystemDataAvailable;
        _systemCapture.RecordingStopped += (_, e) =>
        {
            _systemWriter?.Dispose();
            _systemWriter = null;
            _systemCapture?.Dispose();
            if (e.Exception != null)
                StatusChanged?.Invoke(this, $"System recording error: {e.Exception.Message}");
        };
    }

    private void OnMicrophoneDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_microphoneWriter != null && IsRecording)
        {
            _currentMicLevel = PickLevelCalculator.CalculatePeakLevel(e.Buffer, e.BytesRecorded, _microphoneCapture?.WaveFormat);
            LevelsUpdated?.Invoke(this, (_currentMicLevel, _currentSysLevel));

            lock (_microphoneWriter)
            {
                _microphoneWriter.Write(e.Buffer, 0, e.BytesRecorded);
            }

            // Microphone Transcription
            ProcessTranscription(_micWhisperBuffer, _micResampler, TranscriptionSource.Microphone,
                ref _isMicTranscribing, e.Buffer, e.BytesRecorded);
        }
    }

    private void OnSystemDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (!IsRecording || _systemWriter == null || _systemCapture == null)
            return;

        _currentSysLevel = PickLevelCalculator.CalculatePeakLevel(e.Buffer, e.BytesRecorded, _systemCapture.WaveFormat);
        LevelsUpdated?.Invoke(this, (_currentMicLevel, _currentSysLevel));

        // System Transcription
        ProcessTranscription(_sysWhisperBuffer, _sysResampler, TranscriptionSource.System, ref _isSysTranscribing,
            e.Buffer, e.BytesRecorded);

        lock (_systemWriter)
        {
            // 1) How many bytes "should" be in the file according to elapsed time
            var waveFormat = _systemCapture.WaveFormat;
            var expectedBytes = (long)(_timer.Elapsed.TotalSeconds * waveFormat.AverageBytesPerSecond);

            // align to BlockAlign (whole frame), avoid cuts in the middle of the sample
            expectedBytes -= expectedBytes % waveFormat.BlockAlign;

            // 2) How many bytes are actually in the file at the moment
            var actualBytes = _systemWriter.Length;

            // 3) If we are behind, write silence (byte=0) to fill the gap
            var gapBytes = expectedBytes - actualBytes;
            if (gapBytes > 0)
            {
                var silenceBuffer = new byte[8192]; // zero-initialized => silence
                while (gapBytes > 0)
                {
                    var toWrite = (int)Math.Min(silenceBuffer.Length, gapBytes);
                    _systemWriter.Write(silenceBuffer, 0, toWrite);
                    gapBytes -= toWrite;
                }
            }

            // 4) Write the real audio just arrived
            _systemWriter.Write(e.Buffer, 0, e.BytesRecorded);
        }
    }

    private void ProcessTranscription(BufferedWaveProvider? bufferProvider, MediaFoundationResampler? resampler,
        TranscriptionSource source, ref bool isTranscribingFlag, byte[] buffer, int bytesRecorded)
    {
        if (bufferProvider != null && resampler != null && _whisperFormat != null)
        {
            bufferProvider.AddSamples(buffer, 0, bytesRecorded);

            if (!isTranscribingFlag && bufferProvider.BufferedDuration.TotalSeconds >= 3)
            {
                isTranscribingFlag = true;
                // Capture the flag in a local variable to reset it in the task
                // Note: in C# 'ref' parameters cannot be used in async lambdas.
                // We will use a different approach to manage the state.
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var resampledBuffer = new byte[16000 * 2 * 3];
                        var totalBytesRead = 0;
                        int bytesRead;

                        while ((bytesRead = resampler.Read(resampledBuffer, totalBytesRead,
                                   resampledBuffer.Length - totalBytesRead)) > 0)
                        {
                            totalBytesRead += bytesRead;
                            if (totalBytesRead >= resampledBuffer.Length) break;
                        }

                        if (totalBytesRead > 0)
                        {
                            var samples = new float[totalBytesRead / 2];
                            for (int i = 0, j = 0; i < totalBytesRead - 1; i += 2, j++)
                            {
                                var sample = BitConverter.ToInt16(resampledBuffer, i);
                                samples[j] = sample / 32768f;
                            }

                            if (settingsService.Settings.TranscriptEnabled)
                                await transcriptionService.ProcessAudioAsync(samples, text =>
                                {
                                    var writer = _transcriptionWriter;
                                    if (writer != null)
                                    {
                                        var tag = source == TranscriptionSource.Microphone ? "Me" : "Others";
                                        lock (writer)
                                        {
                                            writer.WriteLine($"[{DateTime.Now:HH:mm:ss}] {tag}: {text}");
                                        }
                                    }

                                    TranscriptionReceived?.Invoke(this, (source, text));
                                });
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Transcription error {source}: {ex.Message}");
                    }
                    finally
                    {
                        // Reset the correct flag based on the source
                        if (source == TranscriptionSource.Microphone) _isMicTranscribing = false;
                        else _isSysTranscribing = false;
                    }
                });
            }
        }
    }

    private void CleanupRecording()
    {
        _currentMicLevel = 0;
        _currentSysLevel = 0;
        LevelsUpdated?.Invoke(this, (0, 0));

        _microphoneCapture?.Dispose();
        _systemCapture?.Dispose();
        _microphoneWriter?.Dispose();
        _systemWriter?.Dispose();
        _micResampler?.Dispose();
        _sysResampler?.Dispose();

        _microphoneCapture = null;
        _systemCapture = null;
        _microphoneWriter = null;
        _systemWriter = null;
        _transcriptionWriter?.Dispose();
        _transcriptionWriter = null;
        _micResampler = null;
        _sysResampler = null;
        _isMicTranscribing = false;
        _isSysTranscribing = false;
    }
}