# AGENTS.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this project is

AutoSub is a .NET 10 CLI tool that creates `.srt` subtitle files for video files with Whisper speech recognition (via `Whisper.net`, with the Vulkan runtime). It scans a directory recursively, skips videos that already have subtitles, extracts the audio with FFmpeg, runs Whisper and writes `{videoname}.en.srt` next to the video.

FFmpeg/FFprobe must be on `PATH` (FFMpegCore is used without a configured binary folder).

## Commands

```bash
dotnet build
dotnet test                                          # all tests ([Explicit] tests are skipped)
dotnet test --filter "LongSentence_SplitsOnPeriod"   # single test by name

# Generate subtitles (default command)
dotnet run --project AutoSub -- <directory> [--model small]

# List videos without any subtitles (read-only; paths to stdout, summary to stderr)
dotnet run --project AutoSub -- list <directory>

# EF Core migrations
dotnet ef migrations add <Name> --project AutoSub
dotnet ef database update --project AutoSub
```

`--model` values: `tiny`, `base`, `small` (default), `medium`, `large-v1`, `large-v2`, `large-v3`. The model is downloaded on first use and cached as `ggml-{model}.bin` in the current working directory.

## Architecture

`Program.cs` sets up a Spectre.Console.Cli `CommandApp<ProcessCommand>` (default command) plus a `list` subcommand (`ListCommand`). Each command has its own `*Settings` class.

**`ProcessCommand`** loads the Whisper model, opens the SQLite DB and runs a loop over all video files inside an `AnsiConsole.Live` table + `BreakdownChart`. For each file:

1. Skip if the path is already in the `Subtitles` table (DB of earlier runs).
2. Skip if `FileHelper.HasSubtitleFile` finds an external subtitle file. This also handles the `Subs/` folder layout of some rips and **moves** those files next to the video (unless `moveFromSubs: false`).
3. FFProbe the file. Skip if there is an embedded **text** subtitle stream. Files with only image-based subtitles (PGS, VobSub, etc., see `FileHelper.ImageBasedSubtitleCodecs`) are still transcribed.
4. `FileHelper.GenerateSubtitleAsync` does the real work: pick the audio stream (first `en`/`eng` track, not titled `Commentary`), extract it as 16 kHz mono PCM WAV to a delete-on-close temp file, check language with `DetectLanguageWithProbability` (must be `en` with probability >= 0.5, otherwise it throws), then stream segments through `SubtitleSplitter.SmartSplit` into a temp file that is moved to `.en.srt`.
5. Skipped and processed paths are added to the `Subtitles` table with bulk insert (`PhenX.EntityFrameworkCore.BulkInsert`) in batches of 10. Errors are logged and not stored, so they are retried on the next run.

**`ListCommand`** uses the same checks but read-only, and it treats **any** embedded subtitle stream (also image-based) as "has subtitles". This difference with `ProcessCommand` is intentional.

## Conventions and gotchas

- Output files are always named `{videoname}.en.srt`.
- `SubtitleSplitter.SmartSplit` makes at most two lines: split at the last punctuation (`. ? ! , ; : —`) before `maxLineLength` (default 40), else at a word boundary, else no split.
- `data.db` is created in `Environment.CurrentDirectory` at runtime (via `EnsureCreatedAsync`), not in the project folder. The `SkippedVideos` table is in the model but not used.
- Logging: NLog to `log.txt` and the debugger at Trace level (`NLog.config`, copied to output). Whisper log callbacks are bridged to NLog in `ProcessCommand`.
- Tests: NUnit 4. `NUnit.Framework` is a global using in the test project. `ManualTests.cs` is `[Explicit]` and needs real video files on a local path.
