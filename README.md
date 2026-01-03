# MKV Batch Processor

A Windows desktop application for batch converting MKV video files to MP4 format with hardware acceleration support, renaming TV show files using TVDB metadata, and converting SUP bitmap subtitles to SRT text format.

## Features

### Video Processing
- **Batch conversion** of MKV files to MP4 (H.264/H.265)
- **Hardware acceleration** support:
  - NVIDIA NVENC
  - Intel QuickSync
  - AMD AMF
  - CPU fallback (libx264/libx265)
- **Audio normalization** with loudness correction (EBU R128)
- **Subtitle extraction** (SRT for text, SUP/SUB for bitmap)
- **Quality presets** optimized for Movies and TV Shows
- **System tray** integration with completion notifications

### TV Show Renaming
- **TVDB integration** for show search and episode metadata
- **Plex-compatible naming**: `Show Name - 01x01 - Episode Name.mkv`
- **Three-panel interface** for easy episode-to-file matching
- **Auto-match** files to episodes by detected season/episode numbers
- **Offline caching** of show data for repeated use
- **Multiple naming formats**: Standard (01x01) or Scene (S01E01)

### Subtitle Conversion
- **SUP to SRT conversion** using PgsToSrt with Tesseract OCR
- **Batch processing** with queue management
- **21 language support** for OCR accuracy
- **Pause/Resume/Cancel** during conversion
- **Drag-drop** file and folder support

## Screenshots

*Coming soon*

## Requirements

- Windows 10/11
- .NET 8.0 Runtime
- FFmpeg (bundled or system PATH)

## Installation

1. Download the latest release
2. Extract to a folder of your choice
3. Run `MkvProcessor.exe`

FFmpeg is included in the release package. Alternatively, ensure FFmpeg and FFprobe are available in your system PATH.

## Usage

### Video Processing Tab

1. Drag and drop MKV files or folders onto the application
2. Select your preferred encoder (auto-detects available hardware)
3. Choose content type (Movie/TV Show) and quality preset
4. Click **Start Processing**

### TV Renaming Tab

1. Enter your TVDB API key in the settings panel ([Get API key](https://thetvdb.com/api-information))
2. Search for your TV show
3. Select the season and add episodes to the queue
4. Add your video files (drag-drop or browse)
5. Use **Auto Match** or manually align episodes with files
6. Click **Rename** to rename files in place

### Subtitle Converter Tab

1. Configure PgsToSrt path in settings (expand settings panel)
2. Optionally set Tessdata path for language files
3. Select OCR language from dropdown
4. Add .sup files (drag-drop, browse, or add folder)
5. Click **Convert** to start batch conversion

## Building from Source

```bash
# Clone the repository
git clone https://github.com/yourusername/MkvProcessor.git

# Build
cd MkvProcessor
dotnet build

# Run
dotnet run

# Publish release
dotnet publish -c Release
```

## Configuration

Settings are stored in `%APPDATA%\MkvProcessor\settings.json`

TVDB cache is stored in `%APPDATA%\MkvProcessor\TvdbCache\`

## Attribution

### TVDB

<a href="https://thetvdb.com/">
  <img src="https://thetvdb.com/images/attribution/logo1.png" alt="TheTVDB" width="200"/>
</a>

Metadata provided by [TheTVDB](https://thetvdb.com/). Please consider [adding missing information](https://thetvdb.com/) or [subscribing](https://thetvdb.com/subscribe).

This application uses the TVDB API but is not endorsed or certified by TheTVDB.

### FFmpeg

This application uses [FFmpeg](https://ffmpeg.org/) for video processing, licensed under the LGPL/GPL.

### PgsToSrt

Subtitle conversion uses [PgsToSrt](https://github.com/Tentacule/PgsToSrt) with [Tesseract OCR](https://github.com/tesseract-ocr/tesseract). PgsToSrt must be downloaded separately.

## License

MIT License - See [LICENSE](LICENSE) file for details.

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## Acknowledgments

- [TheTVDB](https://thetvdb.com/) for providing TV show metadata
- [FFmpeg](https://ffmpeg.org/) for video processing capabilities
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) for MVVM infrastructure
