using AudioRecorder.Models;

namespace AudioRecorder.Services.Interfaces;

public interface IAudioDeviceService
{
    List<AudioDevice> GetInputDevices();
    List<WasapiDevice> GetOutputDevices();
}