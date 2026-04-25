using AudioRecorder.Models;
using AudioRecorder.Services;
using AudioRecorder.Services.Interfaces;
using NSubstitute;
using Whisper.net.LibraryLoader;

namespace AudioRecorder.Tests;

public class TranscriptionServiceTests
{
    private readonly UserSettings _settings = new()
    {
        WhisperModel = "ggml-tiny.bin",
        RuntimeLibraryOrder = [RuntimeLibrary.Cpu],
        TranscriptLanguage = "it"
    };

    private readonly ISettingsService _settingsService = Substitute.For<ISettingsService>();

    public TranscriptionServiceTests()
    {
        _settingsService.Settings.Returns(_settings);
    }

    [Fact]
    public async Task TranscriptionService_Initialize_Cpu_ShouldLoadModel()
    {
        _settings.RuntimeLibraryOrder = [RuntimeLibrary.Cpu];

        using var transcriptionService = new TranscriptionService(_settingsService);
        await transcriptionService.InitializeAsync();
        transcriptionService.Stop();

        _settings.RuntimeLibraryOrder = [RuntimeLibrary.CpuNoAvx];
        using var transcriptionService2 = new TranscriptionService(_settingsService);
        await transcriptionService2.InitializeAsync();
        transcriptionService2.Stop();
    }
}