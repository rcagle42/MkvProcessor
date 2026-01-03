# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

MKV Batch Processor is a Windows desktop application (WPF/.NET 8.0) for batch converting MKV video files to MP4 format. Key features include hardware acceleration (NVIDIA NVENC, Intel QuickSync, AMD AMF), audio normalization with loudness correction, subtitle extraction, **TV show file renaming with TVDB integration**, and **SUP to SRT subtitle conversion using OCR**.

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
- `MainViewModel.cs` - Central view model for the Processing tab, handling file queue and encoding operations
- `TvRenamerViewModel.cs` - View model for the TV Renaming tab, managing TVDB integration and file renaming
- `SubtitleConverterViewModel.cs` - View model for the Subtitle Converter tab, managing PgsToSrt OCR conversion

**Services/**
- `ProcessingQueue.cs` - Manages file processing queue with pause/resume/cancel logic
- `FFmpegService.cs` - Core FFmpeg interaction for video encoding, audio normalization, subtitle extraction
- `MediaInfoService.cs` - FFprobe wrapper for media file analysis (JSON output parsing)
- `EncoderDetectionService.cs` - Hardware encoder availability detection
- `SettingsService.cs` - JSON-based settings persistence to `%APPDATA%\MkvProcessor\settings.json`
- `FFmpegLocator.cs` - Discovers FFmpeg/FFprobe in bundled folder, app directory, or system PATH
- `TvdbService.cs` - TVDB v4 API client for show search and episode metadata retrieval
- `TvdbCacheService.cs` - JSON file caching for TVDB data in `%APPDATA%\MkvProcessor\TvdbCache\`
- `FileMatchingService.cs` - Regex-based episode detection from filenames (S01E01, 1x01, 101 patterns)
- `RenamingService.cs` - Safe file rename operations with validation
- `PgsToSrtLocator.cs` - Discovers PgsToSrt.dll in user path, bundled folder, app directory, or system PATH
- `PgsToSrtService.cs` - Executes PgsToSrt for OCR-based SUP to SRT conversion

**Models/**
- `MkvFile.cs` - Represents a file in queue with metadata and processing progress
- `ProcessingSettings.cs` - User configuration (encoder, quality, audio mode, output folder, TVDB API key)
- `QualityPreset.cs` - Pre-defined CRF and bitrate settings for movies/TV shows
- `TvShow.cs` - TVDB show data with seasons and episodes
- `Season.cs` - Season container with episode list
- `Episode.cs` - Episode metadata (season/episode number, name, air date)
- `FileMatch.cs` - Links a file to a matched episode with confidence level
- `SubtitleFile.cs` - Represents a subtitle file in the conversion queue with status and progress
- Enums: `FileStatus`, `AudioMode`, `EncoderType`, `ContentType`, `MatchConfidence`, `NamingFormat`, `SubtitleConversionStatus`

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
- CommunityToolkit.Mvvm source generators: `[ObservableProperty]`, `[RelayCommand]`

## TV Renamer Feature

The TV Renaming tab provides Plex-compatible file renaming using TVDB metadata.

### UI Layout (Three-Panel Design)

```
┌─────────────────┬──────────────────────┬──────────────────────────────────┐
│ Episode Browser │ Selected Episodes    │ Selected Files    [Add][Folder] │
│                 │                      │                                  │
│ [Search___][Go] │ 1x 01 - Pilot        │ show.s01e01.mkv          (1x01) │
│                 │ 1x 02 - Second       │ show.s01e02.mkv          (1x02) │
│ Recent:         │ 1x 03 - Third        │ show.s01e03.mkv          (1x03) │
│ - Show Name     │                      │                                  │
│                 │ (alternating rows)   │ (alternating rows)               │
│ Show: Name [X]  │                      │                                  │
│ Season: [1 ▼]   ├──────────────────────┼──────────────────────────────────┤
│ Episodes:       │ [Up][Down][Remove]   │ [Up][Down][Remove][Sort][Clear]  │
│ - 01 Episode    │ [Sort][Clear]        │                                  │
│ - 02 Episode    │                      │                                  │
│ [Add][Season]   │                      │                                  │
├─────────────────┴──────────────────────┴──────────────────────────────────┤
│ [Auto Match] [Clear All]              Status              [   Rename   ] │
└───────────────────────────────────────────────────────────────────────────┘
```

### Naming Formats

- **Standard (01x01)**: `Show Name - 01x01 - Episode Name.mkv` (default)
- **Scene (S01E01)**: `Show Name - S01E01 - Episode Name.mkv`

### Episode Detection Patterns

The `FileMatchingService` uses regex to detect episode info from filenames:

| Pattern | Example | Confidence |
|---------|---------|------------|
| S01E01, s01e01 | `show.S01E05.mkv` | High |
| 1x01, 01x01 | `show.1x05.mkv` | Medium |
| 101, 0101 | `show.105.mkv` | Low |

### TVDB API Integration

- **Authentication**: POST to `https://api4.thetvdb.com/v4/login` with API key (and optional PIN)
- **Bearer token**: Stored in memory, refreshed every 7 days
- **Endpoints used**: `/search`, `/series/{id}/extended`, `/series/{id}/episodes/default`

### Cache Structure

```
%APPDATA%\MkvProcessor\TvdbCache\
├── recent.json              # Recently accessed shows [{id, name, year}]
└── shows/
    ├── 12345.json           # Full show data with seasons/episodes
    └── 67890.json
```

### Positional Matching

The rename operation uses positional matching:
- Episode Queue row 1 → File Queue row 1
- Episode Queue row 2 → File Queue row 2
- etc.

Users can manually reorder both queues using Up/Down buttons or Sort to align files with episodes.

### Auto-Match Feature

The Auto Match button attempts to automatically populate the Episode Queue by:
1. Reading detected season/episode numbers from each file in the File Queue
2. Finding matching episodes from the selected show
3. Adding matched episodes to the Episode Queue in file order

The `TryExtendedMatch` method in `TvRenamerViewModel` is a stub for future fuzzy name matching expansion.

### Key User Interactions

- **Double-click** episode in browser → adds to Selected Episodes queue
- **Drag-drop** files/folders onto the view → adds to Selected Files queue
- **Add Season** → adds all episodes from selected season to queue

## Subtitle Converter Feature

The Subtitle Converter tab converts PGS/SUP bitmap subtitles to SRT text format using PgsToSrt with Tesseract OCR.

### Dependencies

- **PgsToSrt** - .NET-based OCR tool ([GitHub](https://github.com/Tentacule/PgsToSrt))
- **Tesseract traineddata files** - Language models for OCR (e.g., `eng.traineddata`)

### PgsToSrt Discovery

`PgsToSrtLocator` searches in this order:
1. User-configured path from settings
2. Bundled `/pgstosrt/` folder
3. Application directory
4. System PATH (looks for `PgsToSrt.dll`)

### Command Execution

```bash
dotnet PgsToSrt.dll --input "file.sup" --output "file.srt" --tesseractlanguage eng --tesseractdata "path/to/tessdata"
```

### Supported Languages

Common Tesseract codes: `eng`, `spa`, `fra`, `deu`, `ita`, `por`, `nld`, `pol`, `rus`, `jpn`, `kor`, `chi_sim`, `chi_tra`, `ara`

### Output Behavior

- Output `.srt` file created in same directory as input `.sup` file
- Filename matches input: `movie.en.sup` → `movie.en.srt`
- Always overwrites existing output files
