using System.Globalization;

namespace AudioRecorder.Models;

public class UserSettings
{
    public string? LastMicDeviceName { get; set; }
    public string? LastSysDeviceId { get; set; }
    public string WhisperModel { get; set; } = "ggml-base.bin";
    public string Language { get; set; } = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
    public bool TranscriptEnabled { get; set; } = true;
}
