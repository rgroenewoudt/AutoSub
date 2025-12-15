using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using PhenX.EntityFrameworkCore.BulkInsert.Sqlite;

namespace AutoSub;

public class DatabaseContext : DbContext
{
    public DbSet<Subtitle> Subtitles { get; set; }
    public DbSet<SkippedVideo> SkippedVideos { get; set; }

    public string DbPath { get; }

    public DatabaseContext()
    {
        DbPath = Path.Combine(Environment.CurrentDirectory, "data.db");
    }

    // The following configures EF to create a Sqlite database file in the
    // special "local" folder for your platform.
    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseSqlite($"Data Source={DbPath}").UseBulkInsertSqlite();
}

public class Subtitle
{
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int SubtitleID { get; set; }
    public string VideoPath { get; set; }

    public string Language { get; set; }
}

public class SkippedVideo
{
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int SkippedVideoID { get; set; }
    public string VideoPath { get; set; }

    public string Language { get; set; }
}