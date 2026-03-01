using System.Globalization;
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
        SetLanguage(userSettings.Language);
        
        Language.ItemsSource = new List<string> { "en", "it" };
        Language.SelectedItem = userSettings.Language;
        Transcript.IsChecked = userSettings.TranscriptEnabled;
        
        LoadAudioDevices();
        LoadWhisperModels();
    }

    private void SetLanguage(string lang)
    {
        var dict = new ResourceDictionary();
        try
        {
            dict.Source = new Uri($"Resources/Strings.{lang}.xaml", UriKind.Relative);

            var oldDict = Application.Current.Resources.MergedDictionaries
                .FirstOrDefault(d => d.Source != null && d.Source.OriginalString.Contains("Strings."));

            if (oldDict != null)
            {
                Application.Current.Resources.MergedDictionaries.Remove(oldDict);
            }

            Application.Current.Resources.MergedDictionaries.Add(dict);
        }
        catch
        {
            // Fallback if resource not found
        }
    }

    private string GetText(string key) => Application.Current.TryFindResource(key) as string ?? key;

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
                StatusLabel.Text = GetText("NoWhisperModelsFound");
            }
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"{GetText("ModelLoadError")}{ex.Message}";
        }
    }

    private void WhisperModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (WhisperModelComboBox.SelectedItem is string selectedModel)
        {
            userSettings.WhisperModel = selectedModel;
            SettingsService.SaveSettings(userSettings);
            
            // Reinitialize the transcription service if necessary
            // In this case, AudioRecorderService creates a new TranscriptionService
            // every time recording starts, reading the current settings.
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
                StatusLabel.Text = GetText("NoMicAvailable");
                StartButton.IsEnabled = false;
            }
            else
            {
                MicDeviceComboBox.ItemsSource = micDevices;
                
                // Try to restore the last selected device
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

                // Try to restore the last selected device
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
            StatusLabel.Text = $"{GetText("ErrorLoadingDevices")}{ex.Message}";
            StartButton.IsEnabled = false;
        }
    }

    private void UpdateStatusLabel()
    {
        var mic = selectedMicDevice?.ProductName ?? GetText("None");
        var sys = selectedSysDevice?.Name ?? GetText("Default");
        StatusLabel.Text = string.Format(GetText("MicSysStatus"), mic, sys);
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
            MessageBox.Show(GetText("SelectMicWarning"), 
                GetText("DeviceNotSelectedTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
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
            Filter = GetText("Mp3Filter"),
            DefaultExt = "mp3",
            FileName = $"{GetText("RecordingFilenamePrefix")}{DateTime.Now:yyyyMMdd_HHmmss}.mp3"
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
                MessageBox.Show($"{GetText("SaveError")}{ex.Message}", GetText("ErrorTitle"), 
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
        string lang = Language.SelectedValue.ToString()!;
        
        userSettings.Language = lang;
        SettingsService.SaveSettings(userSettings);
        SetLanguage(lang);
        
        // Refresh audio devices to update ToString() representation
        LoadAudioDevices();
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
