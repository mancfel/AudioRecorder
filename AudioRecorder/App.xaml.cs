using System.Windows;
using AudioRecorder.Services;
using AudioRecorder.Services.Interfaces;
using AudioRecorder.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AudioRecorder;

public partial class App
{
    private readonly IHost _host;

    public App()
    {
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                services.AddSingleton<ISettingsService, SettingsService>();
                services.AddSingleton<IAudioDeviceService, AudioDeviceService>();
                services.AddTransient<ITranscriptionService, TranscriptionService>();
                services.AddTransient<IAudioRecorderService, AudioRecorderService>();
                services.AddSingleton<MainWindow>();
            })
            .Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        await _host.StartAsync();

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();

        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        using (_host)
        {
            await _host.StopAsync();
        }

        base.OnExit(e);
    }
}