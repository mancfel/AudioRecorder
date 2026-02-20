using System.Diagnostics;
using NAudio.Wave;
using NAudio.CoreAudioApi;
using System.IO;

namespace AudioRecorder.Services;

public class AudioRecorderService : IDisposable
{
    private WaveInEvent? _microphoneCapture;
    private WasapiLoopbackCapture? _systemCapture;
    private WaveFileWriter? _microphoneWriter;
    private WaveFileWriter? _systemWriter;
    private readonly Lock _lockObject = new();
    private bool _isRecording;
    private readonly TranscriptionService _transcriptionService = new();
    
    private BufferedWaveProvider? _micWhisperBuffer;
    private MediaFoundationResampler? _micResampler;
    private bool _isMicTranscribing;

    private BufferedWaveProvider? _sysWhisperBuffer;
    private MediaFoundationResampler? _sysResampler;
    private bool _isSysTranscribing;

    private WaveFormat? _whisperFormat;
    private static readonly string DocumentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    private static readonly string BasePath = Path.Combine(DocumentsPath, "AudioRecorder");
    private string? _currentFilePath;
    private string? _microphoneFilePath;
    private string? _systemFilePath;
    private string? _transcriptionFilePath;
    private StreamWriter? _transcriptionWriter;
    private readonly Stopwatch _timer = new();

    public bool IsRecording 
    { 
        get 
        { 
            lock (_lockObject) 
            { 
                return _isRecording; 
            } 
        } 
    }

    public event EventHandler<string>? StatusChanged;
    public enum TranscriptionSource { Microphone, System }
    public event EventHandler<(float MicLevel, float SysLevel)>? LevelsUpdated;
    public event EventHandler<(TranscriptionSource Source, string Text)>? TranscriptionReceived;

    private float _currentMicLevel;
    private float _currentSysLevel;

    public void StartRecording(int micDeviceNumber, string? systemDeviceId, string? id)
    {
        lock (_lockObject)
        {
            if (_isRecording) return;

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
                _transcriptionWriter = new StreamWriter(_transcriptionFilePath, append: false) { AutoFlush = true };
                
                _microphoneCapture?.StartRecording();
                _systemCapture?.StartRecording();
                _timer.Restart();
                _transcriptionService.InitializeAsync();
                
                _whisperFormat = new WaveFormat(16000, 16, 1);
                
                _micWhisperBuffer = new BufferedWaveProvider(_microphoneCapture.WaveFormat)
                {
                    DiscardOnBufferOverflow = true,
                    BufferDuration = TimeSpan.FromSeconds(10)
                };
                _micResampler = new MediaFoundationResampler(_micWhisperBuffer, _whisperFormat);

                _sysWhisperBuffer = new BufferedWaveProvider(_systemCapture.WaveFormat)
                {
                    DiscardOnBufferOverflow = true,
                    BufferDuration = TimeSpan.FromSeconds(10)
                };
                _sysResampler = new MediaFoundationResampler(_sysWhisperBuffer, _whisperFormat);
                
                _isRecording = true;
                StatusChanged?.Invoke(this, $"Registrazione in corso... (mic #{micDeviceNumber} + sistema)");
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Errore nell'avvio: {ex.Message}");
                CleanupRecording();
            }
        }
    }

    public void StopRecording()
    {
        lock (_lockObject)
        {
            if (!_isRecording) return;

            _microphoneCapture?.StopRecording();
            _systemCapture?.StopRecording();
            _timer.Stop();
            
            _transcriptionWriter?.Flush();
            _transcriptionWriter?.Dispose();
            _transcriptionWriter = null;
            
            _isRecording = false;
            StatusChanged?.Invoke(this, "Registrazione fermata - Pronto per salvare");
        }
    }

    public async Task SaveRecordingAsync(string filePath)
    {
        if (_isRecording)
            throw new InvalidOperationException("Fermare la registrazione prima di salvare");

        if (string.IsNullOrEmpty(_microphoneFilePath) || !File.Exists(_microphoneFilePath))
            throw new InvalidOperationException("Nessuna registrazione del microfono disponibile");

        await Task.Run(() => MixAndSaveFiles(filePath, _microphoneFilePath, _systemFilePath));

        // Salva anche la trascrizione se presente
        if (!string.IsNullOrEmpty(_transcriptionFilePath) && File.Exists(_transcriptionFilePath))
        {
            try
            {
                string transcriptDest = Path.ChangeExtension(filePath, ".txt");
                File.Copy(_transcriptionFilePath, transcriptDest, true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Errore nel salvataggio della trascrizione: {ex.Message}");
            }
        }

        StatusChanged?.Invoke(this, $"File salvato: {Path.GetFileName(filePath)}");
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
        _microphoneCapture.RecordingStopped += (s, e) =>
        {
            _microphoneWriter?.Dispose();
            _microphoneWriter = null;
            _microphoneCapture.Dispose();
            if (e.Exception != null)
                StatusChanged?.Invoke(this, $"Errore registrazione microfono: {e.Exception.Message}");
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
        
        // Configurazione esplicita per evitare problemi di formato
        _systemCapture.ShareMode = AudioClientShareMode.Shared;
        
        _systemCapture.DataAvailable += OnSystemDataAvailable;
        _systemCapture.RecordingStopped += (s, e) =>
        {
            _systemWriter.Dispose();
            _systemWriter = null;
            _systemCapture.Dispose();
            if (e.Exception != null)
                StatusChanged?.Invoke(this, $"Errore registrazione sistema: {e.Exception.Message}");
        };
    }

    private void OnMicrophoneDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_microphoneWriter != null && _isRecording)
        {
            _currentMicLevel = CalculatePeakLevel(e.Buffer, e.BytesRecorded, _microphoneCapture?.WaveFormat);
            LevelsUpdated?.Invoke(this, (_currentMicLevel, _currentSysLevel));

            lock (_microphoneWriter)
            {
                _microphoneWriter.Write(e.Buffer, 0, e.BytesRecorded);
            }

            // Trascrizione Microfono
            ProcessTranscription(_micWhisperBuffer, _micResampler, TranscriptionSource.Microphone, ref _isMicTranscribing, e.Buffer, e.BytesRecorded);
        }
    }

    private void OnSystemDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (!_isRecording || _systemWriter == null || _systemCapture == null)
            return;

        _currentSysLevel = CalculatePeakLevel(e.Buffer, e.BytesRecorded, _systemCapture.WaveFormat);
        LevelsUpdated?.Invoke(this, (_currentMicLevel, _currentSysLevel));

        // Trascrizione Sistema
        ProcessTranscription(_sysWhisperBuffer, _sysResampler, TranscriptionSource.System, ref _isSysTranscribing, e.Buffer, e.BytesRecorded);

        lock (_systemWriter)
        {
            // 1) Quanti byte "dovrebbero" esserci nel file secondo il tempo trascorso
            var waveFormat = _systemCapture.WaveFormat;
            long expectedBytes = (long)(_timer.Elapsed.TotalSeconds * waveFormat.AverageBytesPerSecond);

            // allinea a BlockAlign (frame intero), evita tagli nel mezzo del campione
            expectedBytes -= expectedBytes % waveFormat.BlockAlign;

            // 2) Quanti byte ci sono davvero nel file al momento
            long actualBytes = _systemWriter.Length;

            // 3) Se siamo indietro, scrivi silenzio (byte=0) per colmare il gap
            long gapBytes = expectedBytes - actualBytes;
            if (gapBytes > 0)
            {
                var silenceBuffer = new byte[8192]; // zero-initialized => silenzio
                while (gapBytes > 0)
                {
                    int toWrite = (int)Math.Min(silenceBuffer.Length, gapBytes);
                    _systemWriter.Write(silenceBuffer, 0, toWrite);
                    gapBytes -= toWrite;
                }
            }

            // 4) Scrivi l'audio reale appena arrivato
            _systemWriter.Write(e.Buffer, 0, e.BytesRecorded);
        }
    }
    
    private void ProcessTranscription(BufferedWaveProvider? bufferProvider, MediaFoundationResampler? resampler, TranscriptionSource source, ref bool isTranscribingFlag, byte[] buffer, int bytesRecorded)
    {
        if (bufferProvider != null && resampler != null && _whisperFormat != null)
        {
            bufferProvider.AddSamples(buffer, 0, bytesRecorded);

            if (!isTranscribingFlag && bufferProvider.BufferedDuration.TotalSeconds >= 3)
            {
                isTranscribingFlag = true;
                // Catturiamo il flag in una variabile locale per poterlo resettare nel task
                // Nota: in C# i parametri 'ref' non possono essere usati nei lambda async.
                // Useremo un approccio diverso per gestire lo stato.
                _ = Task.Run(async () =>
                {
                    try
                    {
                        byte[] resampledBuffer = new byte[16000 * 2 * 3];
                        int totalBytesRead = 0;
                        int bytesRead;

                        while ((bytesRead = resampler.Read(resampledBuffer, totalBytesRead, resampledBuffer.Length - totalBytesRead)) > 0)
                        {
                            totalBytesRead += bytesRead;
                            if (totalBytesRead >= resampledBuffer.Length) break;
                        }

                        if (totalBytesRead > 0)
                        {
                            float[] samples = new float[totalBytesRead / 2];
                            for (int i = 0, j = 0; i < totalBytesRead - 1; i += 2, j++)
                            {
                                short sample = BitConverter.ToInt16(resampledBuffer, i);
                                samples[j] = sample / 32768f;
                            }

                            if(SettingsService.Settings.TranscriptEnabled)
                            {
                                await _transcriptionService.ProcessAudioAsync(samples, text =>
                                {
                                    var writer = _transcriptionWriter;
                                    if (writer != null)
                                    {
                                        string tag = source == TranscriptionSource.Microphone ? "Io" : "Altri";
                                        lock (writer)
                                        {
                                            writer.WriteLine($"[{DateTime.Now:HH:mm:ss}] {tag}: {text}");
                                        }
                                    }

                                    TranscriptionReceived?.Invoke(this, (source, text));
                                });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Errore trascrizione {source}: {ex.Message}");
                    }
                    finally
                    {
                        // Resettiamo il flag corretto basandoci sulla sorgente
                        if (source == TranscriptionSource.Microphone) _isMicTranscribing = false;
                        else _isSysTranscribing = false;
                    }
                });
            }
        }
    }

    public void MixAndSaveFiles(string outputPath, string firstFilePath, string secondFilePath)
    {
        try
        {
            // Verifica esistenza file
            var firstFileExists = !string.IsNullOrEmpty(firstFilePath) && File.Exists(firstFilePath);
            var secondFileExists = !string.IsNullOrEmpty(secondFilePath) && File.Exists(secondFilePath);
            
            if(!firstFileExists) throw new InvalidOperationException($"File audio non trovato {firstFilePath}");
            if(!secondFileExists) throw new InvalidOperationException($"File audio non trovato {secondFilePath}");
            
            using var firstFileReader = new WaveFileReader(firstFilePath);
            using var secondFileReader = new WaveFileReader(secondFilePath);
            
            if(!firstFileReader.WaveFormat.Equals(secondFileReader.WaveFormat))
                throw new InvalidOperationException($"Formati audio non compatibili");
            
            var mixingProvider = new MixingWaveProvider32();
            mixingProvider.AddInputStream(firstFileReader);
            mixingProvider.AddInputStream(secondFileReader);
            
            MediaFoundationEncoder.EncodeToMp3(mixingProvider, outputPath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Errore durante il mixing: {ex.Message}", ex);
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

    private float CalculatePeakLevel(byte[] buffer, int bytesRecorded, WaveFormat? format)
    {
        if (format == null || bytesRecorded <= 0) return 0;

        float max = 0;
        try
        {
            if (format.BitsPerSample == 16)
            {
                for (int i = 0; i < bytesRecorded; i += 2)
                {
                    if (i + 1 >= bytesRecorded) break;
                    short sample = BitConverter.ToInt16(buffer, i);
                    float sample32 = Math.Abs(sample / 32768f);
                    if (sample32 > max) max = sample32;
                }
            }
            else if (format.BitsPerSample == 32)
            {
                for (int i = 0; i < bytesRecorded; i += 4)
                {
                    if (i + 3 >= bytesRecorded) break;
                    float sample = BitConverter.ToSingle(buffer, i);
                    float sample32 = Math.Abs(sample);
                    if (sample32 > max) max = sample32;
                }
            }
        }
        catch
        {
            // In caso di errori di parsing, ritorna il massimo trovato finora
        }

        return Math.Min(max, 1.0f);
    }

    public void Dispose()
    {
        StopRecording();
        CleanupRecording();
        _transcriptionService.Dispose();
    }
}

