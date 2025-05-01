﻿using SyncClipboard.Abstract.Notification;

namespace SyncClipboard.Core.Utilities.Fake;

public class FakeProgressBar : IProgressBar
{
    public string Title { get; set; } = "";
    public string Tag { get; set; } = "";
    public string? ProgressTitle { get; set; }
    public double? ProgressValue { get; set; }
    public bool IsIndeterminate { get; set; }
    public string? ProgressValueTip { get; set; }
    public string ProgressStatus { get; set; } = "";
    public Uri? Image { get; set; }
    public List<Button> Buttons { get; set; } = [];

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
