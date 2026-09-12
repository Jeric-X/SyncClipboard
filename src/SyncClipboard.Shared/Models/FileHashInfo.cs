namespace SyncClipboard.Shared.Models;

/// <summary>
/// 文件路径及其内容的 SHA-256；仅保存数据，不执行验证。
/// </summary>
public sealed record FileHashInfo(string Path, string Hash);
