using AudioRecorder.Models;
using AudioRecorder.Services.Interfaces;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace AudioRecorder.Services;

public class AudioDeviceService : IAudioDeviceService
{
    public List<AudioDevice> GetInputDevices()
    {
        var devices = new List<AudioDevice>();

        for (var deviceId = 0; deviceId < WaveIn.DeviceCount; deviceId++)
            try
            {
                var deviceInfo = WaveIn.GetCapabilities(deviceId);
                devices.Add(new AudioDevice
                {
                    DeviceNumber = deviceId,
                    ProductName = deviceInfo.ProductName,
                    Channels = deviceInfo.Channels
                });
            }
            catch
            {
                // Ignore inaccessible devices
            }

        return devices;
    }

    public List<WasapiDevice> GetOutputDevices()
    {
        var devices = new List<WasapiDevice>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            foreach (var endpoint in endpoints)
                devices.Add(new WasapiDevice
                {
                    Id = endpoint.ID,
                    Name = endpoint.FriendlyName
                });
        }
        catch
        {
            // Ignore errors
        }

        return devices;
    }

    public AudioDevice? GetDefaultInputDevice()
    {
        var devices = GetInputDevices();
        return devices.FirstOrDefault();
    }
}