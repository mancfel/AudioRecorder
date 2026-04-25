using System.Text.Json;
using Whisper.net;
using Whisper.net.LibraryLoader;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: AudioRecorder.Transcription <modelPath> <language> [runtimeLibraryOrder]");
    return;
}

var modelPath = args[0];
var language = args[1];
var runtimeLibraryOrder = args.Length > 2 ? args[2] : null;

if (!string.IsNullOrEmpty(runtimeLibraryOrder))
{
    var libraries = runtimeLibraryOrder.Split(',')
        .Select(s => Enum.TryParse<RuntimeLibrary>(s, true, out var lib) ? lib : (RuntimeLibrary?)null)
        .Where(l => l.HasValue)
        .Select(l => l!.Value)
        .ToArray();

    if (libraries.Length > 0) RuntimeOptions.RuntimeLibraryOrder = libraries.ToList();
}

if (!File.Exists(modelPath))
{
    Console.Error.WriteLine($"Model file not found: {modelPath}");
    return;
}

try
{
    using var whisperFactory = WhisperFactory.FromPath(modelPath);
    await using var processor = whisperFactory.CreateBuilder()
        .WithLanguage(language)
        .WithPrintTimestamps()
        .WithNoSpeechThreshold(0.6f)
        .Build();

    // Signal that the processor is ready
    Console.WriteLine(JsonSerializer.Serialize(new { Status = "Ready" }));

    await using var stdin = Console.OpenStandardInput();
    using var reader = new BinaryReader(stdin);

    while (true)
        try
        {
            // Read number of samples
            var sampleCount = reader.ReadInt32();
            if (sampleCount <= 0) break;

            // Read samples
            var samples = new float[sampleCount];
            for (var i = 0; i < sampleCount; i++) samples[i] = reader.ReadSingle();

            // Process audio
            await foreach (var result in processor.ProcessAsync(samples))
            {
                var output = new { result.Text };
                Console.WriteLine(JsonSerializer.Serialize(output));
            }

            Console.WriteLine(JsonSerializer.Serialize(new { Status = "Done" }));
        }
        catch (EndOfStreamException)
        {
            break;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error processing audio: {ex.Message}");
        }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Critical error: {ex.Message}");
}