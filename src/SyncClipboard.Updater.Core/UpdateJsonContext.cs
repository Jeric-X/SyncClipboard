using System.Text.Json.Serialization;

namespace SyncClipboard.Updater.Core;

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(UpdateRequest))]
[JsonSerializable(typeof(UpdateResult))]
public partial class UpdateJsonContext : JsonSerializerContext;
