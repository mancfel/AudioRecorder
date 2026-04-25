using System.IO;
using System.Windows;
using System.Windows.Controls;
using AudioRecorder.Models;
using AudioRecorder.Models.Enums;
using AudioRecorder.Services.Interfaces;
using Microsoft.Win32;
using Whisper.net.Logger;

namespace AudioRecorder.Views;

public partial class MainWindow
{
    private readonly IAudioRecorderService audioService;
    private readonly IAudioDeviceService deviceService;
    private readonly ISettingsService settingsService;
    private readonly UserSettings userSettings;
    private AudioDevice? selectedMicDevice;
    private WasapiDevice? selectedSysDevice;

    public MainWindow(
        IAudioRecorderService audioService,
        IAudioDeviceService deviceService,
        ISettingsService settingsService)
    {
        this.audioService = audioService;
        this.deviceService = deviceService;
        this.settingsService = settingsService;
        userSettings = settingsService.Settings;

        InitializeComponent();

        this.audioService.StatusChanged += OnStatusChanged;
        this.audioService.LevelsUpdated += OnLevelsUpdated;
        this.audioService.TranscriptionReceived += OnTranscriptionReceived;

        SetLanguage(userSettings.Language);

        UiLanguageComboBox.ItemsSource = new List<string> { "en", "it" };
        UiLanguageComboBox.SelectedItem = userSettings.Language;

        TranscriptLanguageComboBox.ItemsSource = new List<string> { "en", "it" };
        TranscriptLanguageComboBox.SelectedItem = userSettings.TranscriptLanguage;

        Transcript.IsChecked = userSettings.TranscriptEnabled;

        RuntimeLibrariesListBox.ItemsSource = userSettings.RuntimeLibraryOrder;
        UpdateTranscriptionSettingsState();

        LoadAudioDevices();
        LoadWhisperModels();
        LogProvider.AddLogger((level, s) => File.AppendAllLines("Log.log", [$"{level}: {s}"]));
    }

    private void SetLanguage(string lang)
    {
        var dict = new ResourceDictionary();
        try
        {
            dict.Source = new Uri($"Resources/Strings.{lang}.xaml", UriKind.Relative);

            var oldDict = Application.Current.Resources.MergedDictionaries
                .FirstOrDefault(d => d.Source != null && d.Source.OriginalString.Contains("Strings."));

            if (oldDict != null) Application.Current.Resources.MergedDictionaries.Remove(oldDict);

            Application.Current.Resources.MergedDictionaries.Add(dict);
        }
        catch
        {
            // Fallback if resource not found
        }
    }

    private string GetText(string key)
    {
        return Application.Current.TryFindResource(key) as string ?? key;
    }

    private void LoadWhisperModels()
    {
        try
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AudioRecorder"
            );

            if (!Directory.Exists(appDataPath)) Directory.CreateDirectory(appDataPath);

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
                    settingsService.SaveSettings(userSettings);
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
            settingsService.SaveSettings(userSettings);

            // Reinitialize the transcription service if necessary
            // In this case, AudioRecorderService creates a new TranscriptionService
            // every time recording starts, reading the current settings.
        }
    }

    private void LoadAudioDevices()
    {
        try
        {
            var micDevices = deviceService.GetInputDevices();
            var sysDevices = deviceService.GetOutputDevices();

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
        selectedMicDevice = MicDeviceComboBox.SelectedItem as AudioDevice;
        if (selectedMicDevice != null)
        {
            userSettings.LastMicDeviceName = selectedMicDevice.ProductName;
            settingsService.SaveSettings(userSettings);
        }

        UpdateStatusLabel();
    }

    private void SysDeviceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        selectedSysDevice = SysDeviceComboBox.SelectedItem as WasapiDevice;
        if (selectedSysDevice != null)
        {
            userSettings.LastSysDeviceId = selectedSysDevice.Id;
            settingsService.SaveSettings(userSettings);
        }

        UpdateStatusLabel();
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (selectedMicDevice == null)
        {
            MessageBox.Show(GetText("SelectMicWarning"),
                GetText("DeviceNotSelectedTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        MicTranscriptionTextBox.Clear();
        SysTranscriptionTextBox.Clear();

        StartButton.IsEnabled = false;

        await audioService.StartRecordingAsync(selectedMicDevice.DeviceNumber, selectedSysDevice?.Id);

        if (audioService.IsRecording)
        {
            StopButton.IsEnabled = true;
            SaveButton.IsEnabled = false;
            MicDeviceComboBox.IsEnabled = false;
            SysDeviceComboBox.IsEnabled = false;
        }
        else
        {
            StartButton.IsEnabled = true;
        }
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

    private void OnStatusChanged(object? sender, string status)
    {
        Dispatcher.BeginInvoke(() => { StatusLabel.Text = status; });
    }

    private void OnLevelsUpdated(object? sender, (float MicLevel, float SysLevel) levels)
    {
        Dispatcher.BeginInvoke(() =>
        {
            MicLevelBar.Value = levels.MicLevel;
            SysLevelBar.Value = levels.SysLevel;
        });
    }

    private void OnTranscriptionReceived(object? sender, (TranscriptionSource Source, string Text) data)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var textBox = data.Source == TranscriptionSource.Microphone
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

    private void UILanguage_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (UiLanguageComboBox.SelectedValue is null) return;
        var lang = UiLanguageComboBox.SelectedValue.ToString()!;

        userSettings.Language = lang;
        settingsService.SaveSettings(userSettings);
        SetLanguage(lang);

        // Refresh audio devices to update ToString() representation
        LoadAudioDevices();
    }

    private void TranscriptLanguage_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TranscriptLanguageComboBox.SelectedValue is null) return;
        var lang = TranscriptLanguageComboBox.SelectedValue.ToString()!;

        userSettings.TranscriptLanguage = lang;
        settingsService.SaveSettings(userSettings);
    }

    private void Transcript_OnChecked(object sender, RoutedEventArgs e)
    {
        userSettings.TranscriptEnabled = true;
        UpdateTranscriptionSettingsState();
        settingsService.SaveSettings(userSettings);
    }

    private void Transcript_OnUnchecked(object sender, RoutedEventArgs e)
    {
        userSettings.TranscriptEnabled = false;
        UpdateTranscriptionSettingsState();
        settingsService.SaveSettings(userSettings);
    }

    private void UpdateTranscriptionSettingsState()
    {
        var enabled = userSettings.TranscriptEnabled;
        WhisperModelComboBox.IsEnabled = enabled;
        TranscriptLanguageComboBox.IsEnabled = enabled;
        RuntimeLibrariesListBox.IsEnabled = enabled;
        MoveUpButton.IsEnabled = enabled;
        MoveDownButton.IsEnabled = enabled;
    }

    private void MoveUpButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedIndex = RuntimeLibrariesListBox.SelectedIndex;
        if (selectedIndex > 0)
        {
            var item = userSettings.RuntimeLibraryOrder[selectedIndex];
            userSettings.RuntimeLibraryOrder.RemoveAt(selectedIndex);
            userSettings.RuntimeLibraryOrder.Insert(selectedIndex - 1, item);
            RuntimeLibrariesListBox.Items.Refresh();
            RuntimeLibrariesListBox.SelectedIndex = selectedIndex - 1;
            settingsService.SaveSettings(userSettings);
        }
    }

    private void MoveDownButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedIndex = RuntimeLibrariesListBox.SelectedIndex;
        if (selectedIndex >= 0 && selectedIndex < userSettings.RuntimeLibraryOrder.Count - 1)
        {
            var item = userSettings.RuntimeLibraryOrder[selectedIndex];
            userSettings.RuntimeLibraryOrder.RemoveAt(selectedIndex);
            userSettings.RuntimeLibraryOrder.Insert(selectedIndex + 1, item);
            RuntimeLibrariesListBox.Items.Refresh();
            RuntimeLibrariesListBox.SelectedIndex = selectedIndex + 1;
            settingsService.SaveSettings(userSettings);
        }
    }
}