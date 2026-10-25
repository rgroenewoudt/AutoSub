# AutoSub

AutoSub is a command-line tool that creates English `.srt` subtitles for your video files. It uses [Whisper](https://github.com/openai/whisper) speech recognition (through [Whisper.net](https://github.com/sandrohanea/whisper.net)) and runs on the GPU with Vulkan.

Point it at a folder, and it will:

- Find all video files in the folder and its subfolders.
- Skip videos that already have subtitles (a separate subtitle file, or an embedded text subtitle track).
- Extract the English audio track with FFmpeg.
- Transcribe the audio with Whisper and write `{videoname}.en.srt` next to the video.

## Features

- **Recursive scan** of a directory, with support for many video formats (`.mkv`, `.mp4`, `.avi`, `.m2ts`, `.mov` and more).
- **Smart skipping**: videos with existing subtitles are not processed again. Videos with only image-based subtitles (PGS, VobSub) are still transcribed, because these subtitles are not text.
- **Remembers earlier runs** in a small SQLite database (`data.db`), so a second run is fast. Failed files are retried on the next run.
- **Picks the right audio track**: the first English track that is not a commentary track.
- **Language check**: the audio must be detected as English, otherwise no subtitle is created and an error is logged.
- **Readable subtitles**: long sentences are split into at most two lines, at punctuation or at a word boundary.
- **Tidies up `Subs/` folders**: subtitle files from a `Subs/` folder (used by some releases) are moved next to the video.
- **Live progress** in the terminal.
- **`list` command** to see which videos have no subtitles at all.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [FFmpeg](https://ffmpeg.org/download.html) (`ffmpeg` and `ffprobe` must be on your `PATH`)
- A GPU with Vulkan support is recommended. Without it, Whisper runs on the CPU, which is much slower.
- Windows (x64) or Linux (x64)

## Getting started

```bash
git clone https://github.com/rgroenewoudt/AutoSub.git
cd AutoSub
dotnet build
```

## Usage

### Generate subtitles

```bash
dotnet run --project AutoSub -- <directory> [--model <model>]
```

Example:

```bash
dotnet run --project AutoSub -- "D:\Movies" --model medium
```

| Option | Description | Default |
| --- | --- | --- |
| `<directory>` | The folder with video files to process | (required) |
| `-m`, `--model` | Whisper model: `tiny`, `base`, `small`, `medium`, `large-v1`, `large-v2`, `large-v3` | `small` |

The model is downloaded on first use and saved as `ggml-{model}.bin` in the current folder. Bigger models give better results but are slower and need more memory.

### List videos without subtitles

```bash
dotnet run --project AutoSub -- list <directory>
```

This command does not change anything. It prints the paths of all videos without subtitles to stdout and a summary to stderr, so you can save the list to a file:

```bash
dotnet run --project AutoSub -- list "D:\Movies" > missing.txt
```

Note: the `list` command counts **all** embedded subtitles, also image-based ones (PGS, VobSub), as subtitles.

## Files created at runtime

These files are created in the current working folder:

| File | Purpose |
| --- | --- |
| `ggml-{model}.bin` | The downloaded Whisper model |
| `data.db` | SQLite database with videos that are already processed or skipped |
| `log.txt` | Log file (NLog) |

To process all files again, delete `data.db`.

## Development

```bash
dotnet build
dotnet test                                          # run all tests
dotnet test --filter "LongSentence_SplitsOnPeriod"   # run one test
```

Tests use NUnit. The tests in `ManualTests.cs` are marked `[Explicit]` and need real video files on your computer.

Database migrations use EF Core:

```bash
dotnet ef migrations add <Name> --project AutoSub
dotnet ef database update --project AutoSub
```

## Built with

- [Whisper.net](https://github.com/sandrohanea/whisper.net) – speech recognition
- [FFMpegCore](https://github.com/rosenbjerg/FFMpegCore) – audio extraction and media info
- [Spectre.Console](https://spectreconsole.net/) – command line and terminal UI
- [Entity Framework Core](https://learn.microsoft.com/ef/core/) with SQLite – run history
- [NLog](https://nlog-project.org/) – logging
