using AudioRecorder.Services;
using NAudio.Wave;

namespace AudioRecorder.Tests;

public class AudioRecorderServiceTests
{
    [Fact]
    public void Mix()
    {
        using var reader1 = new WaveFileReader("mic.wav");
        using var reader2 = new WaveFileReader("sys.wav");

        var mixingProvider = new MixingWaveProvider32();
        mixingProvider.AddInputStream(reader1);
        mixingProvider.AddInputStream(reader2);
        
        MediaFoundationEncoder.EncodeToMp3(mixingProvider, "output.mp3");
    }

    [Fact]
    public async Task TranscribeMicWav()
    {
        if (!File.Exists("mic.wav"))
        {
            throw new FileNotFoundException("File mic.wav not found for testing.");
        }

        using var reader = new WaveFileReader("mic.wav");
        var whisperFormat = new WaveFormat(16000, 16, 1);
        
        using var resampler = new MediaFoundationResampler(reader, whisperFormat);
        
        using var transcriptionService = new TranscriptionService();
        var fullText = "";
        
        var samplesList = new List<float>();
        byte[] buffer = new byte[32000];
        int bytesRead;
        
        while ((bytesRead = resampler.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (int i = 0; i < bytesRead; i += 2)
            {
                short sample = BitConverter.ToInt16(buffer, i);
                samplesList.Add(sample / 32768f);
            }
        }

        await transcriptionService.ProcessAudioAsync(samplesList.ToArray(), text => 
        {
            fullText += text + " ";
            Console.WriteLine($"[DEBUG_LOG] Mic Partial: {text}");
        });
        
        Console.WriteLine($"[DEBUG_LOG] Mic Complete Transcription: {fullText}");
        Assert.False(string.IsNullOrWhiteSpace(fullText), "Microphone transcription should not be empty.");
    }

    [Fact]
    public async Task TranscribeSysWav()
    {
        if (!File.Exists("sys.wav"))
        {
            throw new FileNotFoundException("File sys.wav not found for testing.");
        }

        using var reader = new WaveFileReader("sys.wav");
        var whisperFormat = new WaveFormat(16000, 16, 1);
        
        // Use the resampler to bring the audio to the Whisper format
        using var resampler = new MediaFoundationResampler(reader, whisperFormat);
        
        using var transcriptionService = new TranscriptionService();
        var fullText = "";
        transcriptionService.TranscriptionReceived += (s, text) =>
        {
            fullText += text + " ";
            Console.WriteLine($"[DEBUG_LOG] Partial: {text}");
        };

        // Prepare float samples for Whisper
        var samplesList = new List<float>();
        byte[] buffer = new byte[32000]; // ~1 second at 16kHz
        int bytesRead;
        
        while ((bytesRead = resampler.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (int i = 0; i < bytesRead; i += 2)
            {
                short sample = BitConverter.ToInt16(buffer, i);
                samplesList.Add(sample / 32768f);
            }
        }

        await transcriptionService.ProcessAudioAsync(samplesList.ToArray());
        
        Console.WriteLine($"[DEBUG_LOG] Complete Transcription: {fullText}");
        Assert.False(string.IsNullOrWhiteSpace(fullText), "Transcription should not be empty.");
    }
}