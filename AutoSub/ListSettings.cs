using Spectre.Console.Cli;
using System.ComponentModel;

namespace AutoSub;

public class ListSettings : CommandSettings
{
    [CommandArgument(0, "<directory>")]
    [Description("The directory to search for video files without subtitles")]
    public string Directory { get; set; } = string.Empty;
}
