using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using AudioRecorder.Models;
using System.Collections.Concurrent;
using System.IO;

namespace AudioRecorder.Services;

public class AudioRecorderService : IDisposable
{
    private WaveInEvent? microphoneCapture;
    private WasapiLoopbackCapture? systemCapture;
    private WaveFileWriter? microphoneWriter;
    private WaveFileWriter? systemWriter;
    private readonly object lockObject = new();
    private bool isRecording;
    private string? microphoneFilePath;
    private string? systemFilePath;

    public bool IsRecording 
    { 
        get 
        { 
            lock (lockObject) 
            { 
                return isRecording; 
            } 
        } 
    }

    public event EventHandler<string>? StatusChanged;

    public void StartRecording(int deviceNumber = 0)
    {
        lock (lockObject)
        {
            if (isRecording) return;

            try
            {
                // Crea file temporanei separati per microfono e sistema
                microphoneFilePath = "mic.wav";
                systemFilePath = "sys.wav";
                
                var waveFormat = new WaveFormat(44100, 16, 2);
                microphoneWriter = new WaveFileWriter(microphoneFilePath, waveFormat);
                systemWriter = new WaveFileWriter(systemFilePath, waveFormat);

                SetupMicrophoneCapture(deviceNumber);
                SetupSystemCapture();
                
                microphoneCapture?.StartRecording();
                systemCapture?.StartRecording();
                
                isRecording = true;
                StatusChanged?.Invoke(this, $"Registrazione in corso... (dispositivo #{deviceNumber} + sistema)");
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
        lock (lockObject)
        {
            if (!isRecording) return;

            microphoneCapture?.StopRecording();
            systemCapture?.StopRecording();
            
            microphoneWriter?.Dispose();
            systemWriter?.Dispose();
            microphoneWriter = null;
            systemWriter = null;
            
            isRecording = false;
            StatusChanged?.Invoke(this, "Registrazione fermata - Pronto per salvare");
        }
    }

    public async Task SaveRecordingAsync(string filePath)
    {
        if (isRecording)
            throw new InvalidOperationException("Fermare la registrazione prima di salvare");

        if (string.IsNullOrEmpty(microphoneFilePath) || !File.Exists(microphoneFilePath))
            throw new InvalidOperationException("Nessuna registrazione del microfono disponibile");

        await Task.Run(() => MixAndSaveFiles(filePath));

        StatusChanged?.Invoke(this, $"File salvato: {Path.GetFileName(filePath)}");
        
        // Pulisci file temporanei
        //CleanupTempFiles();
    }

    private void SetupMicrophoneCapture(int deviceNumber)
    {
        microphoneCapture = new WaveInEvent
        {
            DeviceNumber = deviceNumber,
            WaveFormat = new WaveFormat(44100, 16, 2),
            BufferMilliseconds = 50
        };

        microphoneCapture.DataAvailable += OnMicrophoneDataAvailable;
    }

    private void SetupSystemCapture()
    {
        systemCapture = new WasapiLoopbackCapture();
        
        // Configurazione esplicita per evitare problemi di formato
        systemCapture.ShareMode = NAudio.CoreAudioApi.AudioClientShareMode.Shared;
        
        systemCapture.DataAvailable += OnSystemDataAvailable;
        systemCapture.RecordingStopped += (s, e) =>
        {
            if (e.Exception != null)
                StatusChanged?.Invoke(this, $"Errore registrazione sistema: {e.Exception.Message}");
        };
    }

    private void OnMicrophoneDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (microphoneWriter != null && isRecording)
        {
            lock (microphoneWriter)
            {
                microphoneWriter.Write(e.Buffer, 0, e.BytesRecorded);
            }
        }
    }

    private void OnSystemDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (systemWriter != null && isRecording && systemCapture != null)
        {
            try
            {
                // Converte direttamente senza modifiche se il formato è compatibile
                if (IsFormatCompatible(systemCapture.WaveFormat))
                {
                    lock (systemWriter)
                    {
                        systemWriter.Write(e.Buffer, 0, e.BytesRecorded);
                    }
                }
                else
                {
                    // Conversione con resampling appropriato
                    var convertedData = ConvertSystemAudio(e.Buffer, e.BytesRecorded, systemCapture.WaveFormat);
                    if (convertedData != null && convertedData.Length > 0)
                    {
                        lock (systemWriter)
                        {
                            systemWriter.Write(convertedData, 0, convertedData.Length);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Log errore senza interrompere la registrazione
                Console.WriteLine($"Errore conversione audio sistema: {ex.Message}");
            }
        }
    }

    private bool IsFormatCompatible(WaveFormat sourceFormat)
    {
        return sourceFormat.SampleRate == 44100 && 
               sourceFormat.BitsPerSample == 16 && 
               sourceFormat.Channels == 2 &&
               sourceFormat.Encoding == WaveFormatEncoding.Pcm;
    }

    private byte[]? ConvertSystemAudio(byte[] buffer, int bytesRecorded, WaveFormat sourceFormat)
    {
        try
        {
            // Crea un provider dal buffer audio
            var audioBytes = new byte[bytesRecorded];
            Array.Copy(buffer, audioBytes, bytesRecorded);
            
            using var memoryStream = new MemoryStream(audioBytes);
            using var rawSource = new RawSourceWaveStream(memoryStream, sourceFormat);
            
            // Converti il formato se necessario
            ISampleProvider sampleProvider = rawSource.ToSampleProvider();
            
            // Resampling se necessario
            if (sourceFormat.SampleRate != 44100)
            {
                sampleProvider = new WdlResamplingSampleProvider(sampleProvider, 44100);
            }
            
            // Converti a stereo se necessario
            if (sourceFormat.Channels == 1)
            {
                sampleProvider = new MonoToStereoSampleProvider(sampleProvider);
            }
            else if (sourceFormat.Channels > 2)
            {
                sampleProvider = new MultiplexingSampleProvider(new[] { sampleProvider }, 2);
            }
            
            // Converti i sample a 16-bit PCM
            var samples = new float[sampleProvider.WaveFormat.SampleRate * sampleProvider.WaveFormat.Channels];
            var samplesRead = sampleProvider.Read(samples, 0, samples.Length);
            
            if (samplesRead == 0) return null;
            
            var result = new byte[samplesRead * 2]; // 16-bit = 2 bytes per sample
            for (int i = 0; i < samplesRead; i++)
            {
                var sample = Math.Max(-1f, Math.Min(1f, samples[i])); // Clamp
                var intSample = (short)(sample * short.MaxValue);
                var bytes = BitConverter.GetBytes(intSample);
                result[i * 2] = bytes[0];
                result[i * 2 + 1] = bytes[1];
            }
            
            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Errore nella conversione audio: {ex.Message}");
            return null;
        }
    }

    private void MixAndSaveFiles(string outputPath)
    {
        var waveFormat = new WaveFormat(44100, 16, 2);
        
        try
        {
            using var outputWriter = new WaveFileWriter(outputPath, waveFormat);
            
            // Verifica esistenza file
            var micExists = !string.IsNullOrEmpty(microphoneFilePath) && File.Exists(microphoneFilePath);
            var sysExists = !string.IsNullOrEmpty(systemFilePath) && File.Exists(systemFilePath);
            
            if (!micExists && !sysExists)
            {
                throw new InvalidOperationException("Nessun file audio disponibile per il mixing");
            }
            
            using var micReader = micExists ? new WaveFileReader(microphoneFilePath!) : null;
            using var sysReader = sysExists ? new WaveFileReader(systemFilePath!) : null;

            var maxSamples = Math.Max(
                micReader?.SampleCount ?? 0,
                sysReader?.SampleCount ?? 0
            );

            var buffer = new byte[4096];
            long totalSamplesProcessed = 0;

            while (totalSamplesProcessed < maxSamples)
            {
                var micBytes = micReader?.Read(buffer, 0, buffer.Length) ?? 0;
                var sysBytes = sysReader?.Read(buffer, 0, buffer.Length) ?? 0;

                if (micBytes == 0 && sysBytes == 0) break;

                var maxBytes = Math.Max(micBytes, sysBytes);
                var micData = new byte[maxBytes];
                var sysData = new byte[maxBytes];
                
                if (micBytes > 0) Array.Copy(buffer, micData, micBytes);
                if (sysBytes > 0) Array.Copy(buffer, sysData, sysBytes);

                var mixedData = MixAudioData(micData, sysData);
                outputWriter.Write(mixedData, 0, mixedData.Length);
                
                totalSamplesProcessed += maxBytes / 4; // 16-bit stereo = 4 bytes per sample
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Errore durante il mixing: {ex.Message}", ex);
        }
    }

    private byte[] MixAudioData(byte[] micData, byte[] sysData)
    {
        var maxLength = Math.Max(micData.Length, sysData.Length);
        var result = new byte[maxLength];

        for (int i = 0; i < maxLength; i += 2)
        {
            short micSample = i < micData.Length ? BitConverter.ToInt16(micData, i) : (short)0;
            short sysSample = i < sysData.Length ? BitConverter.ToInt16(sysData, i) : (short)0;

            int mixed = (micSample + sysSample) / 2;
            mixed = Math.Max(short.MinValue, Math.Min(short.MaxValue, mixed));

            var mixedBytes = BitConverter.GetBytes((short)mixed);
            result[i] = mixedBytes[0];
            if (i + 1 < maxLength) result[i + 1] = mixedBytes[1];
        }

        return result;
    }

    private void CleanupTempFiles()
    {
        try
        {
            if (!string.IsNullOrEmpty(microphoneFilePath) && File.Exists(microphoneFilePath))
                File.Delete(microphoneFilePath);
            if (!string.IsNullOrEmpty(systemFilePath) && File.Exists(systemFilePath))
                File.Delete(systemFilePath);
        }
        catch { }

        microphoneFilePath = null;
        systemFilePath = null;
    }

    private void CleanupRecording()
    {
        microphoneCapture?.Dispose();
        systemCapture?.Dispose();
        microphoneWriter?.Dispose();
        systemWriter?.Dispose();
        
        CleanupTempFiles();
            
        microphoneCapture = null;
        systemCapture = null;
        microphoneWriter = null;
        systemWriter = null;
    }

    public void Dispose()
    {
        StopRecording();
        CleanupRecording();
    }
}

