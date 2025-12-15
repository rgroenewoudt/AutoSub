using FFMpegCore;
using Microsoft.EntityFrameworkCore;
using PhenX.EntityFrameworkCore.BulkInsert.Extensions;
using Spectre.Console;
using System.Text;
using Whisper.net;
using Whisper.net.Ggml;
using Whisper.net.Logger;

namespace AutoSub;

enum ProcessStatus
{
    Skipped,
    Error,
    Processing,
    Processed
}

internal class Program
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    static async Task Main()
    {
        Console.InputEncoding = Console.OutputEncoding = Encoding.UTF8;

        // Dictionary to hold file statuses
        var fileStatuses = new Dictionary<string, ProcessStatus>();

        var ggmlType = GgmlType.Medium;
        var modelFileName = "ggml-medium.bin";
        var directory = @"G:\TV series\Parks and Recreation Complete 1080p";

        // Download model
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

        // This section creates the processor object which is used to process the audio file, it uses language `auto` to detect the language of the audio file.
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
        //var files = Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories);
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
                    
                    // Build detailed file list
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
                            ProcessStatus.Processing => "[blue]⏳[/]",
                            ProcessStatus.Processed => "[green]✔[/]",
                            ProcessStatus.Skipped => "[grey]⏭[/]",
                            _ => "[red]✖[/]"
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
                            Logger.Info($"Subtitles already embedded in {file}, skipping");

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

                        // Update UI
                        fileStatuses[file] = processStatus;

                        UpdateUI();
                    }
                }

                if(newEntities.Any())
                    await dbContext.ExecuteBulkInsertAsync(newEntities, cancellationToken: CancellationToken.None);
            });

        AnsiConsole.MarkupLine("[bold green]Processing complete![/]");
    }

    private static async Task DownloadModel(string fileName, GgmlType ggmlType)
    {
        await using var modelStream = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(ggmlType);
        await using var fileWriter = File.OpenWrite(fileName);
        await modelStream.CopyToAsync(fileWriter);
    }
}