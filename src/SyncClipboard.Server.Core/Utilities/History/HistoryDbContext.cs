
using Microsoft.EntityFrameworkCore;
using SyncClipboard.Server.Core.Models;

namespace SyncClipboard.Server.Core.Utilities.History;

public class HistoryDbContext : DbContext
{
    private readonly string _dbPath;

    public HistoryDbContext(DbContextOptions<HistoryDbContext> options, IWebHostEnvironment env) : base(options)
    {
        var webRoot = string.IsNullOrEmpty(env.WebRootPath) ? env.ContentRootPath : env.WebRootPath;
        var dataFolder = Path.Combine(webRoot, "data");
        if (!Directory.Exists(dataFolder)) Directory.CreateDirectory(dataFolder);
        _dbPath = Path.Combine(dataFolder, "history.db");
    }

    public DbSet<HistoryRecordEntity> HistoryRecords { get; set; } = null!;

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        if (!options.IsConfigured)
        {
            options.UseSqlite($"Data Source={_dbPath}");
        }
    }
}
