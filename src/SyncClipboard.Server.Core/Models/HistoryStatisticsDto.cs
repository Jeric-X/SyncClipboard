namespace SyncClipboard.Server.Core.Models;

/// <summary>
/// 历史记录统计信息
/// </summary>
public class HistoryStatisticsDto
{
    /// <summary>
    /// 未删除条目数
    /// </summary>
    public int ActiveCount { get; set; }

    /// <summary>
    /// 星标条目数
    /// </summary>
    public int StarredCount { get; set; }

    /// <summary>
    /// 已删除条目数
    /// </summary>
    public int DeletedCount { get; set; }

    /// <summary>
    /// 历史记录总数
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// 总文件占用大小（MB）
    /// </summary>
    public double TotalFileSizeMB { get; set; }
}
