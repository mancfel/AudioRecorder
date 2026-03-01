# Audio Recorder Specifications
- Developed in C# and .NET 10
- Record audio from the microphone
- Record system audio (Loopback)
- Real-time transcription (Microphone and System) using Whisper AI
- Synchronize recordings through silence injection
- Save mixed audio as MP3 and transcription as TXT

## Implementation
- **NAudio**: Library for audio management and device selection
- **WaveInEvent**: Microphone audio capture
- **WasapiLoopbackCapture**: System audio capture with advanced format handling
- **Whisper.net**: AI-powered transcription using Ggml models
- **MediaFoundationResampler**: Audio conversion for transcription (16kHz mono)
- **Synchronization**: Elapsed timer and silence injection to align streams
- **Mixing**: Combined audio channels using MixingWaveProvider32
- **Output**: MP3 (MediaFoundationEncoder) and TXT transcription
- **Settings**: Persistent JSON configuration in %AppData%/AudioRecorder

## Front-End Technologies
- **Windows Presentation Foundation (WPF)**: Modern UI framework for Windows desktop applications
- **XAML**: Markup for defining the user interface and layout
- **Visual Feedback**: Real-time audio level indicators (ProgressBars)
- **Live Transcription**: Dedicated text boxes for real-time transcription display
- **Dispatcher**: WPF threading model for safe UI updates

## Features
- Modern WPF interface with enhanced styling
- **Input device selection** via ComboBox for both mic and system audio
- **Whisper Model selection** for transcription (base, small, etc.)
- **Multi-language support** for transcription (Italian, English, etc.)
- Start/Stop/Save controls with visual feedback
- Real-time status indicator and audio levels (Peak level monitoring)
- Error handling with status updates and MessageBox
- Automatic file saving with timestamp-based naming
- **Gap correction**: Automatic silence injection to handle WASAPI loopback timing issues

## Architecture
- **Pattern**: Code-behind with Service-oriented architecture
- **Services**: 
    - `AudioRecorderService`: Core logic for recording, synchronization, and mixing
    - `AudioDeviceService`: Enumeration and selection of audio devices
    - `TranscriptionService`: AI transcription engine management
    - `SettingsService`: Persistent configuration management
- **Layout**: Responsive grid layout
- **Threading**: Async/Await for non-blocking operations and Dispatcher.Invoke for UI safety

## Applied Fixes
- **WASAPI Synchronization**: Resolved through silence injection based on elapsed time to avoid drift
- **AI Transcription**: Integrated local Whisper models for offline transcription
- **Real-time Monitoring**: Added visual peak level indicators for both audio streams
- **BadDeviceId error resolution** through explicit device selection
- **Silence Filtering**: Energy-based thresholding (0.005f peak) and `WithNoSpeechThreshold(0.6f)` to prevent "Thank you" hallucinations during silent periods.
