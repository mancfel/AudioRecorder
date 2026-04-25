# Audio Recorder POC

## Overview

This application is a Proof of Concept (POC) designed for recording audio from both the microphone and system outputs,
transcribing both in real-time using Whisper in .NET.

> **Note:** This is not a production-ready application. It may contain bugs, and code quality or best practices were not
> the primary focus during development.

## Architecture

- **Framework:** .NET 10 Windows Desktop
- **UI:** Windows Presentation Foundation (WPF)
- **Audio Library:** [NAudio](https://github.com/naudio/NAudio)
- **Transcription Engine:** [Whisper.net](https://github.com/sandrohanea/whisper.net)
    - Built a custom version of the library to support Vitis
      AI. [Whisper.net VitisAI](https://github.com/mancfel/whisper.net)
    - Built a new Runtime Whisper.net.Runtime.VitisAI.Windows from AMD fork
      of [Whisper.cpp](https://github.com/amd/whisper.cpp)

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
    - Ryzen AI CPUs (tested on Ryzen AI 7 350).
- The transcription file and the recorded audio are saved in subfolders of the `Documents\AudioRecorder` folder.

## Whisper Models

The transcription feature requires a Whisper model.

1. Download compatible models from [Hugging Face](https://huggingface.co/ggerganov/whisper.cpp/tree/main).
2. Place the model files in the `%AppData%\AudioRecorder` folder.
3. To use Ryzen AI CPUs

- Download the .rai vitis encoder for the models to use
  from [Hugging Face](https://huggingface.co/collections/amd/ryzen-ai-17-whisper-npu-optimized-onnx-models).
- Place the model file in the `%AppData%\AudioRecorder` folder.

## Specifications

Additional implementation details can be found in [Specifications.md](AudioRecorder/Specifications.md).