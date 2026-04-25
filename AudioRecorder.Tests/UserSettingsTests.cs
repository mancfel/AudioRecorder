using System.Text.Json;
using System.Text.Json.Serialization;
using AudioRecorder.Models;
using Whisper.net.LibraryLoader;

namespace AudioRecorder.Tests;

public class UserSettingsTests
{
    private readonly JsonSerializerOptions _options = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public void RuntimeLibraryOrder_ShouldSerializeAsStrings()
    {
        var settings = new UserSettings
        {
            RuntimeLibraryOrder = [RuntimeLibrary.Cuda, RuntimeLibrary.Cpu]
        };

        var json = JsonSerializer.Serialize(settings, _options);

        // Verify that the JSON contains the string names instead of integers
        Assert.Contains("\"Cuda\"", json);
        Assert.Contains("\"Cpu\"", json);
        Assert.DoesNotContain(": 0", json);
    }

    [Fact]
    public void RuntimeLibraryOrder_ShouldDeserializeFromStrings()
    {
        var json = """
                   {
                       "RuntimeLibraryOrder": ["Vulkan", "OpenVino"]
                   }
                   """;

        var settings = JsonSerializer.Deserialize<UserSettings>(json, _options);

        Assert.NotNull(settings);
        Assert.Equal(2, settings.RuntimeLibraryOrder.Count);
        Assert.Equal(RuntimeLibrary.Vulkan, settings.RuntimeLibraryOrder[0]);
        Assert.Equal(RuntimeLibrary.OpenVino, settings.RuntimeLibraryOrder[1]);
    }
}