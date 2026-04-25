using System.Globalization;
using Whisper.net.LibraryLoader;

namespace AudioRecorder.Models;

public class UserSettings
{
    public string? LastMicDeviceName { get; set; }
    public string? LastSysDeviceId { get; set; }
    public string WhisperModel { get; set; } = "ggml-base.bin";
    public string Language { get; set; } = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
    public string TranscriptLanguage { get; set; } = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
    public bool TranscriptEnabled { get; set; } = true;

    public List<RuntimeLibrary> RuntimeLibraryOrder { get; set; } =
    [
        RuntimeLibrary.Cuda, RuntimeLibrary.Cuda12, RuntimeLibrary.Vulkan,
        RuntimeLibrary.VitisAI, RuntimeLibrary.OpenVino,
        RuntimeLibrary.CoreML, RuntimeLibrary.Cpu, RuntimeLibrary.CpuNoAvx
    ];
}