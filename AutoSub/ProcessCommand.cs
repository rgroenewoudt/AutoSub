using FFMpegCore;
using Microsoft.EntityFrameworkCore;
using PhenX.EntityFrameworkCore.BulkInsert.Extensions;
using Spectre.Console;
using Spectre.Console.Cli;
using System.Text;
using Whisper.net;
using Whisper.net.Ggml;
using Whisper.net.Logger;

namespace AutoSub;

public class ProcessCommand : AsyncCommand<ProcessSettings>
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    public override async Task<int> ExecuteAsync(CommandContext context, ProcessSettings settings)
    {
        Console.InputEncoding = Console.OutputEncoding = Encoding.UTF8;

        if (string.IsNullOrWhiteSpace(settings.Directory))
        {
            AnsiConsole.MarkupLine("[red]Error: Directory is required[/]");
            return 1;
        }

        if (!System.IO.Directory.Exists(settings.Directory))
        {
            AnsiConsole.MarkupLine($"[red]Error: Directory does not exist: {settings.Directory}[/]");
            return 1;
        }

        var fileStatuses = new Dictionary<string, ProcessStatus>();

        var ggmlType = settings.Model.ToLowerInvariant() switch
        {
            "tiny" => GgmlType.Tiny,
            "base" => GgmlType.Base,
            "small" => GgmlType.Small,
            "medium" => GgmlType.Medium,
            "large-v1" => GgmlType.LargeV1,
            "large-v2" => GgmlType.LargeV2,
            "large-v3" => GgmlType.LargeV3,
            _ => GgmlType.Medium
        };
        
        var modelFileName = $"ggml-{settings.Model.ToLowerInvariant()}.bin";
        var directory = settings.Directory;

        if (!File.Exists(modelFileName))
        {
            AnsiConsole.MarkupLineInterpolated($"[grey]Downloading model [yellow]{modelFileName}[/][/]");
            await DownloadModel(modelFileName, ggmlType);
        }
        else
        {
            AnsiConsole.MarkupLineInterpolated($"[grey]Using cached model [yellow]{modelFileName}[/][/]");
        }

        using var whisperLogger = LogProvider.AddLogger((level, msg) =>
        {
            NLog.LogLevel nLogLevel;
            switch (level)
            {
                case WhisperLogLevel.Info:
                    nLogLevel = NLog.LogLevel.Info;
                    break;
                case WhisperLogLevel.Cont:
                case WhisperLogLevel.Debug:
                    nLogLevel = NLog.LogLevel.Debug;
                    break;
                case WhisperLogLevel.Error:
                    nLogLevel = NLog.LogLevel.Error;
                    break;
                case WhisperLogLevel.Warning:
                    nLogLevel = NLog.LogLevel.Warn;
                    break;
                default:
                case WhisperLogLevel.None:
                    nLogLevel = NLog.LogLevel.Off;
                    break;
            }
            Logger.Log(nLogLevel, msg);
        });
        using var whisperFactory = WhisperFactory.FromPath(modelFileName);

        await using var processor = whisperFactory.CreateBuilder()
            .WithLanguage("auto")
            .Build();

        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (sender, eventArgs) =>
        {
            AnsiConsole.MarkupLine("[bold red]Cancelling...[/]");
            cts.Cancel();
            eventArgs.Cancel = true;
        };

        await using var dbContext = new DatabaseContext();
        await dbContext.Database.EnsureCreatedAsync(cts.Token);

        AnsiConsole.MarkupLineInterpolated($"[grey]Database path: [yellow]{dbContext.DbPath}[/][/]");

        var existingSubtitles = await dbContext.Subtitles.Select(x => x.VideoPath).ToHashSetAsync(cts.Token);
        List<Subtitle> newEntities = [];

        AnsiConsole.MarkupLineInterpolated($"[grey]Searching files...[/]");
        var files = Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories)
            .Where(f => FileHelper.VideoFileExtensions.Any(ext => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        await AnsiConsole.Live(new Panel("Starting processing..."))
            .AutoClear(false)
            .Overflow(VerticalOverflow.Ellipsis)
            .Cropping(VerticalOverflowCropping.Top)
            .StartAsync(async ctx =>
            {
                int processedCount = 0, skippedCount = 0, errorCount = 0, queueCount = 0;
                void UpdateUI()
                {
                    var fileTable = new Table
                    {
                        Border = TableBorder.Rounded,
                        Expand = true
                    };
                    fileTable.AddColumn("Video");
                    fileTable.AddColumn("Status");

                    fileTable.Columns[0].NoWrap();
                    fileTable.Columns[1].NoWrap();

                    foreach (var kvp in fileStatuses)
                    {
                        string fileStatus = kvp.Value switch
                        {
                            ProcessStatus.Processing => "[blue]?[/]",
                            ProcessStatus.Processed => "[green]?[/]",
                            ProcessStatus.Skipped => "[grey]?[/]",
                            _ => "[red]?[/]"
                        };
                        fileTable.AddRow(Markup.Escape(kvp.Key), fileStatus);
                    }

                    fileTable.AddRow(new BreakdownChart()
                        .Width(60)
                        .AddItem("Queued", queueCount - errorCount - skippedCount - processedCount, Color.White)
                        .AddItem("Error", errorCount, Color.Red)
                        .AddItem("Skipped", skippedCount, Color.Grey)
                        .AddItem("Processed", processedCount, Color.Green)
                    );

                    ctx.UpdateTarget(fileTable);
                }

                queueCount = files.Count;

                foreach (string file in files)
                {
                    if (cts.Token.IsCancellationRequested)
                        break;

                    if (existingSubtitles.Contains(file))
                    {
                        //skippedCount++;
                        //continue;
                    }

                    if (newEntities.Count >= 10)
                    {
                        await dbContext.ExecuteBulkInsertAsync(newEntities, cancellationToken: CancellationToken.None);
                        newEntities.Clear();
                    }

                    var processStatus = ProcessStatus.Processing;
                    fileStatuses[file] = processStatus;
                    UpdateUI();

                    try
                    {
                        if (FileHelper.HasSubtitleFile(file))
                        {
                            Logger.Info($"Subtitles already present in {file}, skipping");

                            newEntities.Add(new Subtitle() { VideoPath = file, Language = "en" });
                            processStatus = ProcessStatus.Skipped;
                            continue;
                        }

                        var mediaInfo = await FFProbe.AnalyseAsync(file, cancellationToken: CancellationToken.None);

                        if (mediaInfo.SubtitleStreams.Any())
                        {
                            var info = string.Join(";", mediaInfo.SubtitleStreams.Select(s => $"subtitle: {s.CodecName}, {s.Language}, {string.Join(",", s.Tags?.Select(t => $"{t.Key}:{t.Value}") ?? Array.Empty<string>())}"));
                            
                            Logger.Info($"Subtitles already embedded in {file}: {info}, skipping");

                            newEntities.Add(new Subtitle() { VideoPath = file, Language = "en" });
                            processStatus = ProcessStatus.Skipped;
                            continue;
                        }

                        Logger.Info($"Processing video {file}...");

                        await FileHelper.GenerateSubtitleAsync(file, mediaInfo, processor, cts.Token);
                        newEntities.Add(new Subtitle() { VideoPath = file, Language = "en" });
                        processStatus = ProcessStatus.Processed;
                    }
                    catch (Exception ex)
                    {
                        processStatus = ProcessStatus.Error;

                        Logger.Warn(ex, $"Exception for video {file}");
                    }
                    finally
                    {
                        switch (processStatus)
                        {
                            case ProcessStatus.Processed:
                                processedCount++;
                                break;
                            case ProcessStatus.Skipped:
                                skippedCount++;
                                break;
                            case ProcessStatus.Error:
                            default:
                                errorCount++;
                                break;
                        }

                        fileStatuses[file] = processStatus;

                        UpdateUI();
                    }
                }

                if (newEntities.Any())
                    await dbContext.ExecuteBulkInsertAsync(newEntities, cancellationToken: CancellationToken.None);
            });

        AnsiConsole.MarkupLine("[bold green]Processing complete![/]");
        return 0;
    }

    private static async Task DownloadModel(string fileName, GgmlType ggmlType)
    {
        await using var modelStream = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(ggmlType);
        await using var fileWriter = File.OpenWrite(fileName);
        await modelStream.CopyToAsync(fileWriter);
    }
}
