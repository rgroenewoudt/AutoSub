#nullable enable
using FFMpegCore;
using FFMpegCore.Pipes;
using System.Diagnostics;
using System.Threading;
using Whisper.net;
using Whisper.net.Wave;

namespace AutoSub;

public static class FileHelper
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    public static string[] FileExtensions =
    [
        ".srt",
        ".ass",
        ".ssa",
        ".mks",
        ".sami",
        ".smi",
        ".sub",
        ".sup",
        ".vtt"
    ];

    public static string[] VideoFileExtensions =
    [
        ".001",
        ".3g2",
        ".3gp",
        ".amv",
        ".asf",
        ".asx",
        ".avi",
        ".bin",
        ".bivx",
        ".divx",
        ".dv",
        ".dvr-ms",
        ".f4v",
        ".fli",
        ".flv",
        ".ifo",
        ".img",
        ".iso",
        ".m2t",
        ".m2ts",
        ".m2v",
        ".m4v",
        ".mkv",
        ".mk3d",
        ".mov",
        ".mp4",
        ".mpe",
        ".mpeg",
        ".mpg",
        ".mts",
        ".mxf",
        ".nrg",
        ".nsv",
        ".nuv",
        ".ogg",
        ".ogm",
        ".ogv",
        ".pva",
        ".qt",
        ".rec",
        ".rm",
        ".rmvb",
        ".strm",
        ".svq3",
        ".tp",
        ".ts",
        ".ty",
        ".viv",
        ".vob",
        ".vp3",
        ".webm",
        ".wmv",
        ".wtv",
        ".xvid"
    ];

    // Image-based subtitle codecs that cannot be used as text subtitles.
    // Files with only these codecs should still be transcribed by Whisper.
    private static readonly HashSet<string> ImageBasedSubtitleCodecs = new(StringComparer.OrdinalIgnoreCase)
    {
        "dvd_subtitle",   // DVD VOBsub
        "dvdsub",
        "hdmv_pgs_subtitle", // Blu-ray PGS
        "pgssub",
        "xsub",           // XSUB (DivX AVI)
    };

    public static bool IsImageBasedSubtitleCodec(string? codecName) =>
        codecName != null && ImageBasedSubtitleCodecs.Contains(codecName);

    public static bool HasSubtitleFile(string videoFile)
    {
        var dir = Path.GetDirectoryName(videoFile)!;
        var filenameWithoutExt = Path.GetFileNameWithoutExtension(videoFile);
        var prefix = Path.Combine(dir, filenameWithoutExt);
        foreach (var ext in FileExtensions)
        {
            if (File.Exists(prefix + ext) || File.Exists(prefix + ".en" + ext))
                return true;
        }

        // Special case: srt in Subs directory
        if(Directory.Exists(Path.Combine(dir, "Subs")))
        {
            if (Directory.Exists(Path.Combine(dir, "Subs", filenameWithoutExt)))
            {
                var files = Directory.GetFiles(Path.Combine(dir, "Subs", filenameWithoutExt));

                var srtFile = files
                    .FirstOrDefault(f => f.Contains("English.srt", StringComparison.OrdinalIgnoreCase) || f.Contains("eng.srt", StringComparison.OrdinalIgnoreCase));

                if (srtFile != null)
                {
                    Logger.Info($"Moved .srt from Subs directory");
                    File.Move(srtFile, prefix + ".srt");
                    return true;
                }
            }
            else
            {
                var files = Directory.GetFiles(Path.Combine(dir, "Subs"))
                    .Where(f => f.Contains(filenameWithoutExt))
                    .ToList();

                var idxFile = files.FirstOrDefault(x => x.Contains(filenameWithoutExt + ".idx", StringComparison.OrdinalIgnoreCase));
                var subFile = files.FirstOrDefault(x => x.Contains(filenameWithoutExt + ".sub", StringComparison.OrdinalIgnoreCase));

                if(idxFile != null && subFile != null)
                {
                    Logger.Info($"Moved .idx/sub from Subs directory");
                    File.Move(idxFile, prefix + ".idx");
                    File.Move(subFile, prefix + ".sub");
                    return true;
                }
            }

            var subsDirSrt = Path.Combine(dir, "Subs", filenameWithoutExt, "2_English.srt");
            if (File.Exists(subsDirSrt))
            {
                Logger.Info($"Moved srt from Subs directory: {subsDirSrt}");
                File.Move(subsDirSrt, prefix + ".en.srt");
                return true;
            }
        }

        return false;
    }

    public static async Task GenerateSubtitleAsync(string videoFile, IMediaAnalysis mediaInfo,
        WhisperProcessor processor,
        CancellationToken cancellationToken)
    {
        var start = Stopwatch.GetTimestamp();

        // Detect audio stream
        AudioStream? audioStream;
        if (mediaInfo.AudioStreams.Count == 1)
        {
            audioStream = mediaInfo.AudioStreams[0];
        }
        else
        {
            audioStream = mediaInfo.AudioStreams
                .OrderBy(x => x.Index)
                .Where(x => x.Tags == null || !x.Tags.TryGetValue("title", out var titleVal) || titleVal != "Commentary")
                .FirstOrDefault(x => string.Equals("en", x.Language, StringComparison.OrdinalIgnoreCase) || string.Equals("eng", x.Language, StringComparison.OrdinalIgnoreCase));
        }

        if (audioStream == null)
        {
            var errorMsg = "No English audio stream found. Available audio streams:";
            foreach (var itemAudioStream in mediaInfo.AudioStreams)
            {
                errorMsg += $"- Available audio stream: index: {itemAudioStream.Index} channels:{itemAudioStream.Channels} lang:{itemAudioStream.Language} profile:{itemAudioStream.Profile} tags:{string.Join(' ', itemAudioStream.Tags?.Select(t => $"{t.Key}:{t.Value}") ?? [])}";
            }

            throw new Exception(errorMsg);
        }

        Logger.Info($"Selected audio stream: channels:{audioStream.Channels} lang:{audioStream.Language}");

        // Extract audio stream
        await using var extractedAudioStream = new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None, 32768, FileOptions.DeleteOnClose);
        var ffmpegResult = await FFMpegArguments
        .FromFileInput(videoFile)
        .OutputToPipe(new StreamPipeSink(extractedAudioStream), options => options
                .WithCustomArgument($"-map 0:{audioStream.Index}") // select specific audio track
                .WithAudioCodec("pcm_s16le")                // WAV PCM codec
                .WithCustomArgument("-ac 1")                       // mono
                .ForceFormat("wav")                         // WAV container
                .WithAudioSamplingRate(16000) // Whisper model requires 16kHz audio
                .UsingMultithreading(true)
        )
        .ProcessAsynchronously();
        if (!ffmpegResult)
        {
            throw new Exception("Failed to extract audio stream");
        }

        Logger.Info($"Extracted audio stream: {extractedAudioStream.Length/1024} KB");

        extractedAudioStream.Position = 0;

        var waveParser = new WaveParser(extractedAudioStream);
        var samples = await waveParser.GetAvgSamplesAsync(cancellationToken);

        var (lang, probability) = processor.DetectLanguageWithProbability(samples);

        if (lang != "en" || probability < 0.5)
        {
            throw new Exception($"Audio stream parsing failed due wrong language or low probability. Detected language {lang} and probability {probability}");
        }

        var whisperEnumerable = processor.ProcessAsync(samples, cancellationToken);

        // Create srt file
        var enSrtFile = Path.Combine(Path.GetDirectoryName(videoFile)!, $"{Path.GetFileNameWithoutExtension(videoFile)}.en.srt");

        int subtitleCount = 0;
        var tmpFile = Path.GetTempFileName();
        await using (var srtFileStream =
                     new StreamWriter(new FileStream(tmpFile, FileMode.OpenOrCreate, FileAccess.Write,
                         FileShare.None)))
        {
            await foreach (var result in whisperEnumerable)
            {
                //Console.WriteLine($"{result.Start}->{result.End}: {result.NoSpeechProbability}");

                if (cancellationToken.IsCancellationRequested)
                    return;

                var lines = SubtitleSplitter.SmartSplit(result.Text);
                if (lines.Count == 0)
                    continue;

                await srtFileStream.WriteLineAsync($"{subtitleCount++}");
                await srtFileStream.WriteLineAsync(
                    $"{result.Start:hh\\:mm\\:ss\\,fff} --> {result.End:hh\\:mm\\:ss\\,fff}");

                foreach (var line in lines)
                    await srtFileStream.WriteLineAsync(line);

                await srtFileStream.WriteLineAsync();
            }
        }

        File.Move(tmpFile, enSrtFile);

        var duration = (int)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        Logger.Info($"Processing took {duration}ms, resulting in {subtitleCount} records in .srt file");
    }
}