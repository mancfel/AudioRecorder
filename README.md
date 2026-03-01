# Audio Recorder POC

## Overview
This application is a Proof of Concept (POC) designed for recording audio from both the microphone and system outputs, transcribing both in real-time using Whisper in .NET.

> **Note:** This is not a production-ready application. It may contain bugs, and code quality or best practices were not the primary focus during development.

## Architecture
- **Framework:** .NET 10 Windows Desktop
- **UI:** Windows Presentation Foundation (WPF)
- **Audio Library:** [NAudio](https://github.com/naudio/NAudio)
- **Transcription Engine:** [Whisper.net](https://github.com/sandrohanea/whisper.net)

## Features
- Record audio from microphone and system sources simultaneously.
- Save combined audio as MP3.
- Real-time transcription using local Whisper models.
- Transcription export to text files.
- Select Whisper models and target languages for transcription.
- **Energy-based silence filtering** to prevent transcription hallucinations (e.g., "Thank you").
- **Hardware Acceleration support**:
  - NVIDIA CUDA (tested on GeForce RTX 4060 Ti).
  - Intel CPUs (tested on Intel Ultra 7 268V).
- The trascriptin file and the recorded audio are saved in the `Documents\AudioRecorder` folder.

## Whisper Models
The transcription feature requires a Whisper model.
1. Download compatible models from [Hugging Face](https://huggingface.co/ggerganov/whisper.cpp/tree/main).
2. Place the model files in the `%AppData%\AudioRecorder` folder.

## Specifications
Additional implementation details can be found in [Specifications.md](AudioRecorder/Specifications.md).