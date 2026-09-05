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

    static async Task<int> Main(string[] args)
    {
        var app = new CommandApp<ProcessCommand>();
        
        app.Configure(config =>
        {
            config.SetApplicationName("AutoSub");
            config.SetApplicationVersion("1.0.0");

            config.AddCommand<ListCommand>("list")
                .WithDescription("List video files that have no subtitles (neither embedded nor as separate file)")
                .WithExample("list", @"D:\Movies");
        });

        return await app.RunAsync(args);
    }
}