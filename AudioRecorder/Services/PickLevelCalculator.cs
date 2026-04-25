using NAudio.Wave;

namespace AudioRecorder.Services;

public static class PickLevelCalculator
{
    public static float CalculatePeakLevel(byte[] buffer, int bytesRecorded, WaveFormat? format)
    {
        if (format == null || bytesRecorded <= 0) return 0;

        float max = 0;
        try
        {
            if (format.BitsPerSample == 16)
                for (var i = 0; i < bytesRecorded; i += 2)
                {
                    if (i + 1 >= bytesRecorded) break;
                    var sample = BitConverter.ToInt16(buffer, i);
                    var sample32 = Math.Abs(sample / 32768f);
                    if (sample32 > max) max = sample32;
                }
            else if (format.BitsPerSample == 32)
                for (var i = 0; i < bytesRecorded; i += 4)
                {
                    if (i + 3 >= bytesRecorded) break;
                    var sample = BitConverter.ToSingle(buffer, i);
                    var sample32 = Math.Abs(sample);
                    if (sample32 > max) max = sample32;
                }
        }
        catch
        {
            // In case of parsing errors, return the maximum found so far
        }

        return Math.Min(max, 1.0f);
    }
}