namespace SyncClipboard.Core.Models.UserConfigs;

public record class ClipboardAcquisitionConfig
{
    public TextImageRule TextImageRule { get; set; } = TextImageRule.Text;
}
