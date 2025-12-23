using NAudio.Wave;

namespace AudioRecorder.Services;

public class AudioDeviceService
{
    public class AudioDevice
    {
        public int DeviceNumber { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public int Channels { get; set; }
        
        public override string ToString() => $"{ProductName} ({Channels} canali)";
    }

    public static List<AudioDevice> GetInputDevices()
    {
        var devices = new List<AudioDevice>();
        
        for (int deviceId = 0; deviceId < WaveIn.DeviceCount; deviceId++)
        {
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
                // Ignora dispositivi non accessibili
            }
        }

        return devices;
    }

    public static AudioDevice? GetDefaultInputDevice()
    {
        var devices = GetInputDevices();
        return devices.FirstOrDefault();
    }
}

