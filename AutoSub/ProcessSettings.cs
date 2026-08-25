using Spectre.Console.Cli;
using System.ComponentModel;

namespace AutoSub;

public class ProcessSettings : CommandSettings
{
    [CommandArgument(0, "<directory>")]
    [Description("The directory containing video files to process")]
    public string Directory { get; set; } = string.Empty;

    [CommandOption("-m|--model")]
    [Description("The Whisper model to use (tiny, base, small, medium, large-v1, large-v2, large-v3)")]
    [DefaultValue("small")]
    public string Model { get; set; } = "small";
}
