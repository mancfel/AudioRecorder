using System.Windows;

namespace AudioRecorder.Services;

public static class ResourceManager
{
    public static string GetText(string key)
    {
        if (Application.Current == null) return key;
        return Application.Current.Dispatcher.CheckAccess()
            ? Application.Current.TryFindResource(key) as string ?? key
            : Application.Current.Dispatcher.Invoke(() => Application.Current.TryFindResource(key) as string ?? key);
    }
}