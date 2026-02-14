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
        
        //using var writer = new WaveFileWriter("output.wav", mixingProvider.WaveFormat);
        
        // var buffer = new byte[mixingProvider.WaveFormat.AverageBytesPerSecond]; // ~1 secondo di audio
        // int bytesRead;
        // while ((bytesRead = mixingProvider.Read(buffer, 0, buffer.Length)) > 0)
        // {
        //     writer.Write(buffer, 0, bytesRead);
        // }
    }
}