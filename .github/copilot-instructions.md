# AutoSub – Copilot Instructions

## What this project is

AutoSub is a .NET 10 CLI tool that auto-generates `.srt` subtitle files for video files using OpenAI's Whisper speech-recognition model (via `Whisper.net`). It scans a directory recursively, skips files that already have subtitles, extracts audio with FFmpeg, runs Whisper, and writes `{filename}.en.srt` alongside the video.

## Build, test, and run

```bash
# Build
dotnet build

# Run all tests
dotnet test

# Run a single test by name
dotnet test --filter "LongSentence_SplitsOnPeriod"

# Run the CLI
dotnet run --project AutoSub -- <directory> [--model medium]
```

Available `--model` values: `tiny`, `base`, `small`, `medium` (default), `large-v1`, `large-v2`, `large-v3`. Whisper model files are downloaded on first use and cached as `ggml-{model}.bin` in the working directory.

### EF Core migrations

```bash
dotnet ef migrations add <Name> --project AutoSub
dotnet ef database update --project AutoSub
```

## Architecture

The entry point (`Program.cs`) wires up a single `CommandApp<ProcessCommand>` via Spectre.Console.Cli. All business logic lives in `ProcessCommand.ExecuteAsync`, which orchestrates this pipeline for each video file:

1. **Skip check** – `FileHelper.HasSubtitleFile` looks for existing subtitle files next to the video (`.srt`, `.ass`, `.vtt`, etc. including `.en.*` variants) and also handles the `Subs/` subdirectory convention used by some Blu-ray rips (moves the file into place and returns `true`).
2. **Embedded subtitle check** – FFProbe analyses the container; files with embedded subtitle streams are skipped.
3. **Audio extraction** – FFMpeg extracts the selected audio track as 16 kHz mono PCM WAV into a temp `FileStream` with `FileOptions.DeleteOnClose`.
4. **Language gate** – `WhisperProcessor.DetectLanguageWithProbability` is called first; processing is aborted if the detected language is not `"en"` or probability < 0.5.
5. **Transcription** – `processor.ProcessAsync` streams `SegmentData` results; each segment is passed through `SubtitleSplitter.SmartSplit` and written to a temp file, then `File.Move`d to the final `.en.srt` path.
6. **Persistence** – Processed paths are bulk-inserted (batch size 10) into the `Subtitles` SQLite table via `PhenX.EntityFrameworkCore.BulkInsert` to avoid reprocessing on subsequent runs.

The live terminal UI is a Spectre.Console `Table` + `BreakdownChart` updated after each file via `AnsiConsole.Live`.

## Key conventions

- **Output naming**: subtitle files are always named `{videoname}.en.srt` (the `.en.` language tag is intentional).
- **Audio selection**: when a file has multiple audio streams, the first English (`en`/`eng`) track is selected, and tracks tagged `title=Commentary` are excluded.
- **`SubtitleSplitter.SmartSplit`**: splits a Whisper segment text into at most two lines at the last punctuation delimiter (`. ? ! , ; : —`) before `maxLineLength` (default 40). Falls back to word boundary, then returns the whole string unsplit.
- **SQLite DB**: `data.db` is created in `Environment.CurrentDirectory` at runtime (not in the project directory). The `SkippedVideos` table exists in the model but the skip-on-rerun logic in `ProcessCommand` is currently commented out.
- **Logging**: NLog writes to `log.txt` (and the VS Debugger target) at Trace level. Whisper's own log callbacks are bridged to NLog inside `ProcessCommand`. `NLog.config` ships with the output via `CopyToOutputDirectory=PreserveNewest`.
- **Manual / integration tests**: `ManualTests.cs` is decorated with `[Explicit]` – these require real video files on a specific local path and must be run manually; they are excluded from `dotnet test` runs by default.
- **Test framework**: NUnit 4 with `[TestFixture]` / `[Test]`. `NUnit.Framework` is globally imported in the test project (`<Using Include="NUnit.Framework" />`), so no `using` statement is needed in test files.
