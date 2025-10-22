using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Metadata;
using SyncClipboard.Core.ViewModels.Sub;
using System;
using System.Collections.Generic;

namespace SyncClipboard.Desktop.Views;

internal class ClipboardViewerTemplateSelector : IDataTemplate
{
    [Content]
    public Dictionary<string, IDataTemplate> AvailableTemplates { get; } = [];

    public Control Build(object? param)
    {
        if (param is not HistoryRecordVM record)
        {
            throw new ArgumentNullException(nameof(param));
        }

        if (record.Type == ProfileType.Image)
        {
            return AvailableTemplates["Image"].Build(param)!;
        }
        return AvailableTemplates["Normal"].Build(param)!;
    }

    public bool Match(object? data)
    {
        if (data is HistoryRecordVM)
        {
            return true;
        }
        return false;
    }
}
