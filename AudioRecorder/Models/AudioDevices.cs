using System.Windows;

namespace AudioRecorder.Models;

public class AudioDevice
{
    public int DeviceNumber { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public int Channels { get; init; }

    public override string ToString()
    {
        const string key = "ChannelsFormat";
        var format = "{0} ({1} channels)";
        if (Application.Current == null) return string.Format(format, ProductName, Channels);
        var format1 = format;
        format = Application.Current.Dispatcher.CheckAccess()
            ? Application.Current.TryFindResource(key) as string ?? format
            : Application.Current.Dispatcher.Invoke(() =>
                Application.Current.TryFindResource(key) as string ?? format1);

        return string.Format(format, ProductName, Channels);
    }
}