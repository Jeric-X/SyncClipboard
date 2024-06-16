using SyncClipboard.Abstract.Notification;
using System;
using System.Collections.Generic;

namespace SyncClipboard.Desktop.Utilities;

public class ProgressBar : IProgressBar
{
    public string Title { get; set; } = "";
    public string Tag { get; set; } = "";
    public string? ProgressTitle { get; set; }
    public double? ProgressValue { get; set; }
    public bool IsIndeterminate { get; set; }
    public string? ProgressValueTip { get; set; }
    public string ProgressStatus { get; set; } = "";
    public Uri? Image { get; set; }
    public List<Button> Buttons { get; set; } = new List<Button>();

    public void Remove()
    {
    }

    public void ShowSilent()
    {
    }

    public bool Upadate()
    {
        return true;
    }
}
