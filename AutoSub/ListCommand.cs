using FFMpegCore;
using Spectre.Console;
using Spectre.Console.Cli;
using System.Text;

namespace AutoSub;

/// <summary>
/// Lists video files that have neither an external subtitle file nor an embedded subtitle stream
/// (text or image-based, e.g. VobSub). Processing differs here: it transcribes image-only files.
/// Read-only: unlike processing, it never moves subtitles out of a "Subs" directory.
/// </summary>
public class ListCommand : AsyncCommand<ListSettings>
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    public override async Task<int> ExecuteAsync(CommandContext context, ListSettings settings, CancellationToken cancellationToken)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (string.IsNullOrWhiteSpace(settings.Directory))
        {
            AnsiConsole.MarkupLine("[red]Error: Directory is required[/]");
            return 1;
        }

        if (!System.IO.Directory.Exists(settings.Directory))
        {
            AnsiConsole.MarkupLineInterpolated($"[red]Error: Directory does not exist: {settings.Directory}[/]");
            return 1;
        }

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Console.CancelKeyPress += (sender, eventArgs) =>
        {
            cts.Cancel();
            eventArgs.Cancel = true;
        };

        var files = FileHelper.FindVideoFiles(settings.Directory);

        int missingCount = 0;

        foreach (var file in files)
        {
            if (cts.Token.IsCancellationRequested)
                break;

            if (FileHelper.HasSubtitleFile(file, moveFromSubs: false))
                continue;

            try
            {
                var mediaInfo = await FFProbe.AnalyseAsync(file, cancellationToken: cts.Token);

                // Any subtitle stream counts here, including image-based ones such as VobSub.
                if (mediaInfo.SubtitleStreams.Count > 0)
                    continue;
            }
            catch (OperationCanceledException) when (cts.Token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, $"Could not analyse {file}");
                await Console.Error.WriteLineAsync($"Skipped (could not analyse): {file}");
                continue;
            }

            // Plain output so it can be piped/redirected.
            Console.WriteLine(file);
            missingCount++;
        }

        await Console.Error.WriteLineAsync($"{missingCount} of {files.Count} video file(s) without subtitles.");
        return 0;
    }
}
