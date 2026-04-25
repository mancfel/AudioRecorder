using System.IO;
using NAudio.Wave;

namespace AudioRecorder.Services;

public static class MixingService
{
    public static void MixAndSaveFiles(string outputPath, string firstFilePath, string secondFilePath)
    {
        try
        {
            // Verify file existence
            var firstFileExists = !string.IsNullOrEmpty(firstFilePath) && File.Exists(firstFilePath);
            var secondFileExists = !string.IsNullOrEmpty(secondFilePath) && File.Exists(secondFilePath);

            if (!firstFileExists) throw new InvalidOperationException($"Audio file not found {firstFilePath}");
            if (!secondFileExists) throw new InvalidOperationException($"Audio file not found {secondFilePath}");

            using var firstFileReader = new WaveFileReader(firstFilePath);
            using var secondFileReader = new WaveFileReader(secondFilePath);

            if (!firstFileReader.WaveFormat.Equals(secondFileReader.WaveFormat))
                throw new InvalidOperationException("Incompatible audio formats");

            var mixingProvider = new MixingWaveProvider32();
            mixingProvider.AddInputStream(firstFileReader);
            mixingProvider.AddInputStream(secondFileReader);

            MediaFoundationEncoder.EncodeToMp3(mixingProvider, outputPath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Error during mixing: {ex.Message}", ex);
        }
    }
}