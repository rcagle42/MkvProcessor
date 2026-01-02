# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

MKV Batch Processor is a Windows desktop application (WPF/.NET 8.0) for batch converting MKV video files to MP4 format. Key features include hardware acceleration (NVIDIA NVENC, Intel QuickSync, AMD AMF), audio normalization with loudness correction, and subtitle extraction.

## Build Commands

```bash
# Build
dotnet build

# Build Release
dotnet build -c Release

# Run
dotnet run

# Publish
dotnet publish -c Release
```

## Architecture

The application follows MVVM pattern with a service-oriented design:

```
Presentation (WPF/XAML)
    ↓
ViewModel (MainViewModel - central orchestrator)
    ↓
Services (ProcessingQueue, FFmpegService, MediaInfoService, etc.)
    ↓
Models (MkvFile, ProcessingSettings, QualityPreset, etc.)
```

### Key Components

**ViewModels/**
- `MainViewModel.cs` - Central view model handling all UI state, commands, and coordination between services

**Services/**
- `ProcessingQueue.cs` - Manages file processing queue with pause/resume/cancel logic
- `FFmpegService.cs` - Core FFmpeg interaction for video encoding, audio normalization, subtitle extraction
- `MediaInfoService.cs` - FFprobe wrapper for media file analysis (JSON output parsing)
- `EncoderDetectionService.cs` - Hardware encoder availability detection
- `SettingsService.cs` - JSON-based settings persistence to `%APPDATA%\MkvProcessor\settings.json`
- `FFmpegLocator.cs` - Discovers FFmpeg/FFprobe in bundled folder, app directory, or system PATH

**Models/**
- `MkvFile.cs` - Represents a file in queue with metadata and processing progress
- `ProcessingSettings.cs` - User configuration (encoder, quality, audio mode, output folder)
- `QualityPreset.cs` - Pre-defined CRF and bitrate settings for movies/TV shows
- Enums: `FileStatus`, `AudioMode`, `EncoderType`, `ContentType`

### Processing Pipeline

1. Files are probed with FFprobe to extract metadata (duration, audio codec, subtitles)
2. Subtitles extracted from MKV (text as SRT, bitmap as SUP/SUB)
3. Audio optionally normalized to stereo with two-pass loudness analysis
4. Video encoded to MP4 with selected codec/quality settings

## Dependencies

- **CommunityToolkit.Mvvm 8.2.2** - MVVM infrastructure (ObservableObject, RelayCommand)
- **Newtonsoft.Json 13.0.3** - Settings serialization
- **Hardcodet.NotifyIcon.Wpf 1.1.0** - System tray integration
- **FFmpeg/FFprobe** - Bundled in `/ffmpeg/` folder or discovered via system PATH

## Code Patterns

- Nullable reference types enabled throughout
- Async/await with CancellationToken support for responsive UI
- Generated regex patterns using `[GeneratedRegex]` attribute
- Real-time FFmpeg output parsing for progress updates
