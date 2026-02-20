using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using AudioRecorder.Services;
using AudioRecorder.Models;
using System.IO;

namespace AudioRecorder.Views;

public partial class MainWindow
{
    private readonly AudioRecorderService audioService;
    private AudioDeviceService.AudioDevice? selectedMicDevice;
    private AudioDeviceService.WasapiDevice? selectedSysDevice;
    private readonly UserSettings userSettings;

    public MainWindow()
    {
        InitializeComponent();
        audioService = new AudioRecorderService();
        audioService.StatusChanged += OnStatusChanged;
        audioService.LevelsUpdated += OnLevelsUpdated;
        audioService.TranscriptionReceived += OnTranscriptionReceived;
        
        userSettings = SettingsService.Settings;
        Language.ItemsSource = new List<string> { "en", "it" };
        Language.SelectedItem = userSettings.Language;
        Transcript.IsChecked = userSettings.TranscriptEnabled;
        
        LoadAudioDevices();
        LoadWhisperModels();
    }

    private void LoadWhisperModels()
    {
        try
        {
            string appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AudioRecorder"
            );

            if (!Directory.Exists(appDataPath))
            {
                Directory.CreateDirectory(appDataPath);
            }

            var modelFiles = Directory.GetFiles(appDataPath, "*.bin")
                .Select(Path.GetFileName)
                .ToList();

            WhisperModelComboBox.ItemsSource = modelFiles;

            if (modelFiles.Count > 0)
            {
                if (modelFiles.Contains(userSettings.WhisperModel))
                {
                    WhisperModelComboBox.SelectedItem = userSettings.WhisperModel;
                }
                else
                {
                    WhisperModelComboBox.SelectedIndex = 0;
                    userSettings.WhisperModel = modelFiles[0]!;
                    SettingsService.SaveSettings(userSettings);
                }
            }
            else
            {
                StatusLabel.Text = "Nessun modello Whisper (.bin) trovato in AppData/AudioRecorder";
            }
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Errore nel caricamento modelli: {ex.Message}";
        }
    }

    private void WhisperModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (WhisperModelComboBox.SelectedItem is string selectedModel)
        {
            userSettings.WhisperModel = selectedModel;
            SettingsService.SaveSettings(userSettings);
            
            // Reinizializziamo il servizio di trascrizione se necessario
            // In questo caso, AudioRecorderService crea un nuovo TranscriptionService
            // ogni volta che avvia la registrazione, leggendo le impostazioni correnti.
        }
    }

    private void LoadAudioDevices()
    {
        try
        {
            var micDevices = AudioDeviceService.GetInputDevices();
            var sysDevices = AudioDeviceService.GetOutputDevices();
            
            if (!micDevices.Any())
            {
                StatusLabel.Text = "Nessun dispositivo di input (mic) disponibile";
                StartButton.IsEnabled = false;
            }
            else
            {
                MicDeviceComboBox.ItemsSource = micDevices;
                
                // Cerca di ripristinare l'ultimo dispositivo selezionato
                var savedMic = micDevices.FirstOrDefault(d => d.ProductName == userSettings.LastMicDeviceName);
                if (savedMic != null)
                {
                    MicDeviceComboBox.SelectedItem = savedMic;
                    selectedMicDevice = savedMic;
                }
                else
                {
                    MicDeviceComboBox.SelectedIndex = 0;
                    selectedMicDevice = micDevices.First();
                }
            }

            if (sysDevices.Any())
            {
                SysDeviceComboBox.ItemsSource = sysDevices;

                // Cerca di ripristinare l'ultimo dispositivo selezionato
                var savedSys = sysDevices.FirstOrDefault(d => d.Id == userSettings.LastSysDeviceId);
                if (savedSys != null)
                {
                    SysDeviceComboBox.SelectedItem = savedSys;
                    selectedSysDevice = savedSys;
                }
                else
                {
                    SysDeviceComboBox.SelectedIndex = 0;
                    selectedSysDevice = sysDevices.First();
                }
            }
            
            UpdateStatusLabel();
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Errore nel caricamento dispositivi: {ex.Message}";
            StartButton.IsEnabled = false;
        }
    }

    private void UpdateStatusLabel()
    {
        var mic = selectedMicDevice?.ProductName ?? "Nessuno";
        var sys = selectedSysDevice?.Name ?? "Predefinito";
        StatusLabel.Text = $"Mic: {mic} | Sys: {sys}";
        StartButton.IsEnabled = selectedMicDevice != null && !audioService.IsRecording;
    }

    private void MicDeviceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        selectedMicDevice = MicDeviceComboBox.SelectedItem as AudioDeviceService.AudioDevice;
        if (selectedMicDevice != null)
        {
            userSettings.LastMicDeviceName = selectedMicDevice.ProductName;
            SettingsService.SaveSettings(userSettings);
        }
        UpdateStatusLabel();
    }

    private void SysDeviceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        selectedSysDevice = SysDeviceComboBox.SelectedItem as AudioDeviceService.WasapiDevice;
        if (selectedSysDevice != null)
        {
            userSettings.LastSysDeviceId = selectedSysDevice.Id;
            SettingsService.SaveSettings(userSettings);
        }
        UpdateStatusLabel();
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (selectedMicDevice == null)
        {
            MessageBox.Show("Seleziona un microfono prima di iniziare la registrazione.", 
                "Dispositivo non selezionato", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        MicTranscriptionTextBox.Clear();
        SysTranscriptionTextBox.Clear();
        audioService.StartRecording(selectedMicDevice.DeviceNumber, selectedSysDevice?.Id, userSettings.Language);
        StartButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        SaveButton.IsEnabled = false;
        MicDeviceComboBox.IsEnabled = false;
        SysDeviceComboBox.IsEnabled = false;
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        audioService.StopRecording();
        StartButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        SaveButton.IsEnabled = true;
        MicDeviceComboBox.IsEnabled = true;
        SysDeviceComboBox.IsEnabled = true;
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
        Dispatcher.BeginInvoke(() =>
        {
            StatusLabel.Text = status;
        });
    }

    private void OnLevelsUpdated(object? sender, (float MicLevel, float SysLevel) levels)
    {
        Dispatcher.BeginInvoke(() =>
        {
            MicLevelBar.Value = levels.MicLevel;
            SysLevelBar.Value = levels.SysLevel;
        });
    }

    private void OnTranscriptionReceived(object? sender, (AudioRecorderService.TranscriptionSource Source, string Text) data)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var textBox = data.Source == AudioRecorderService.TranscriptionSource.Microphone 
                ? MicTranscriptionTextBox 
                : SysTranscriptionTextBox;
                
            textBox.AppendText(data.Text + " ");
            textBox.ScrollToEnd();
        });
    }

    protected override void OnClosed(EventArgs e)
    {
        audioService?.Dispose();
        base.OnClosed(e);
    }

    private void Language_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if(Language.SelectedValue is null) return;
        userSettings.Language = Language.SelectedValue.ToString();
        SettingsService.SaveSettings(userSettings);
    }

    private void Transcript_OnChecked(object sender, RoutedEventArgs e)
    {
        userSettings.TranscriptEnabled = true;
        SettingsService.SaveSettings(userSettings);
    }

    private void Transcript_OnUnchecked(object sender, RoutedEventArgs e)
    {
        userSettings.TranscriptEnabled = false;
        SettingsService.SaveSettings(userSettings);
    }
}
