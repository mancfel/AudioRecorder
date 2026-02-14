using System.Windows;
using Microsoft.Win32;
using AudioRecorder.Services;

namespace AudioRecorder.Views;

public partial class MainWindow
{
    private readonly AudioRecorderService audioService;
    private AudioDeviceService.AudioDevice? selectedDevice;

    public MainWindow()
    {
        InitializeComponent();
        audioService = new AudioRecorderService();
        audioService.StatusChanged += OnStatusChanged;
        
        LoadAudioDevices();
    }

    private void LoadAudioDevices()
    {
        try
        {
            var devices = AudioDeviceService.GetInputDevices();
            
            if (!devices.Any())
            {
                StatusLabel.Text = "Nessun dispositivo di input disponibile";
                StartButton.IsEnabled = false;
                return;
            }

            DeviceComboBox.ItemsSource = devices;
            DeviceComboBox.SelectedIndex = 0;
            selectedDevice = devices.First();
            
            StatusLabel.Text = $"Dispositivo selezionato: {selectedDevice.ProductName}";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Errore nel caricamento dispositivi: {ex.Message}";
            StartButton.IsEnabled = false;
        }
    }

    private void DeviceComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        selectedDevice = DeviceComboBox.SelectedItem as AudioDeviceService.AudioDevice;
        if (selectedDevice != null)
        {
            StatusLabel.Text = $"Dispositivo selezionato: {selectedDevice.ProductName}";
            StartButton.IsEnabled = !audioService.IsRecording;
        }
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (selectedDevice == null)
        {
            MessageBox.Show("Seleziona un dispositivo di input prima di iniziare la registrazione.", 
                "Dispositivo non selezionato", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        audioService.StartRecording(selectedDevice.DeviceNumber);
        StartButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        SaveButton.IsEnabled = false;
        DeviceComboBox.IsEnabled = false;
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        audioService.StopRecording();
        StartButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        SaveButton.IsEnabled = true;
        DeviceComboBox.IsEnabled = true;
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var saveDialog = new SaveFileDialog
        {
            Filter = "File Mp3|*.mp3",
            DefaultExt = "mp3",
            FileName = $"Registrazione_{DateTime.Now:yyyyMMdd_HHmmss}.mp3"
        };

        if (saveDialog.ShowDialog() == true)
        {
            try
            {
                SaveButton.IsEnabled = false;
                await audioService.SaveRecordingAsync(saveDialog.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Errore nel salvataggio: {ex.Message}", "Errore", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SaveButton.IsEnabled = true;
            }
        }
    }

    private void OnStatusChanged(object? sender, string status)
    {
        Dispatcher.Invoke(() =>
        {
            StatusLabel.Text = status;
        });
    }

    protected override void OnClosed(EventArgs e)
    {
        audioService?.Dispose();
        base.OnClosed(e);
    }
}
