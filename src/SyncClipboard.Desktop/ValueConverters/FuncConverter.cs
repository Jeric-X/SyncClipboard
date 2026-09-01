using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using FluentAvalonia.UI.Controls;
using SyncClipboard.Core.ViewModels.EmbeddedIcons;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.ViewModels;
using SyncClipboard.Core.ViewModels.Sub;
using System;
using System.Collections.Generic;

namespace SyncClipboard.Desktop.ValueConverters;

public static class FuncConverter
{
    public static FuncValueConverter<bool, bool> Not { get; } =
        new FuncValueConverter<bool, bool>(value => !value);

    public static FuncValueConverter<object, bool> NotNullToVisible { get; } =
        new FuncValueConverter<object, bool>(value => value != null);

    public static FuncValueConverter<object, bool> NullToVisible { get; } =
        new FuncValueConverter<object, bool>(value => value == null);

    public static FuncValueConverter<HistoryRecordVM, bool> ShowImagePreview { get; } =
        new FuncValueConverter<HistoryRecordVM, bool>(record =>
            record != null && record.Type == ProfileType.Image && record.PreviewImage != null);

    public static FuncValueConverter<HistoryRecordVM, bool> ShowTextPreview { get; } =
        new FuncValueConverter<HistoryRecordVM, bool>(record =>
            record != null && (record.Type != ProfileType.Image || record.PreviewImage == null));

    /// <summary>
    /// 多值转换器：根据 record 和 previewImage 判断是否显示图片预览
    /// </summary>
    public static IMultiValueConverter ShowImagePreviewMulti { get; } =
        new FuncMultiValueConverter<object?, bool>(values =>
        {
            var valuesList = new List<object?>();
            foreach (var v in values)
            {
                valuesList.Add(v);
            }

            if (valuesList.Count >= 2 &&
                valuesList[0] is HistoryRecordVM record)
            {
                // PreviewImage 可能是 string 或 null
                var previewImage = valuesList[1];
                var hasPreviewImage = previewImage is string s && s != null;
                return record.Type == ProfileType.Image && hasPreviewImage;
            }
            return false;
        });

    /// <summary>
    /// 多值转换器：根据 record 和 previewImage 判断是否显示文字预览
    /// </summary>
    public static IMultiValueConverter ShowTextPreviewMulti { get; } =
        new FuncMultiValueConverter<object?, bool>(values =>
        {
            var valuesList = new List<object?>();
            foreach (var v in values)
            {
                valuesList.Add(v);
            }

            if (valuesList.Count >= 2 &&
                valuesList[0] is HistoryRecordVM record)
            {
                // PreviewImage 可能是 string 或 null
                var previewImage = valuesList[1];
                var hasPreviewImage = previewImage is string s && s != null;
                return record.Type != ProfileType.Image || !hasPreviewImage;
            }
            return false;
        });

    public static FuncValueConverter<ProfileType?, string> ProfileTypeToString { get; } =
        new FuncValueConverter<ProfileType?, string>(type =>
        {
            if (type == null)
                return string.Empty;
            return Converter.ProfileTypeToLocalizedString(type.Value);
        });

    public static FuncValueConverter<HistoryRecordVM, string> GetRecordSize { get; } =
        new FuncValueConverter<HistoryRecordVM, string>(Converter.GetRecordSize);

    public static FuncValueConverter<Severity?, InfoBarSeverity> ConvertSeverity { get; } =
        new FuncValueConverter<Severity?, InfoBarSeverity>(severity => severity switch
        {
            Severity.Info => InfoBarSeverity.Informational,
            Severity.Success => InfoBarSeverity.Success,
            Severity.Warning => InfoBarSeverity.Warning,
            Severity.Error => InfoBarSeverity.Error,
            _ => InfoBarSeverity.Informational,
        });

    public static IMultiValueConverter LimitHistoryListText { get; } =
        new FuncMultiValueConverter<object?, string>(values =>
        {
            var valuesList = new List<object?>();
            foreach (var v in values)
            {
                valuesList.Add(v);
            }

            var text = valuesList.Count > 0 ? valuesList[0] as string : null;
            var isCompactListMode = valuesList.Count > 1 && valuesList[1] is bool b && b;
            return Converter.LimitHistoryListText(text, isCompactListMode);
        });

    public static IMultiValueConverter HistoryRecordToHugeiconsGeometry { get; } =
        new FuncMultiValueConverter<object?, Geometry>(values =>
        {
            var valuesList = new List<object?>();
            foreach (var value in values)
            {
                valuesList.Add(value);
            }

            var type = valuesList.Count > 0 && valuesList[0] is ProfileType profileType
                ? profileType
                : ProfileType.Text;
            var filePaths = valuesList.Count > 1 ? valuesList[1] as string[] : null;
            return Geometry.Parse(HugeiconsEmbeddedIconProvider.ResolvePathData(type, filePaths));
        });

    public static FuncValueConverter<string, Bitmap?> ToBitImage { get; } =
        new FuncValueConverter<string, Bitmap?>(input =>
        {
            try
            {
                return new Bitmap(input!);
            }
            catch { }
            return null;
        });

    public static FuncValueConverter<bool, string> ToStarIcon { get; } =
        new FuncValueConverter<bool, string>(input =>
        {
            if (input)
            {
                return "\uE1CF";
            }
            return "\uE1CE";
        });

    public static FuncValueConverter<bool, string> ToPinIcon { get; } =
        new FuncValueConverter<bool, string>(input =>
        {
            if (input)
            {
                return char.ConvertFromUtf32((int)Convert.ToUInt32("F809B", 16)).ToString();
            }
            return "\uE141";
        });

    public static FuncValueConverter<double, Thickness> CalculateInfoBarMargin { get; } =
        new FuncValueConverter<double, Thickness>(listBoxHeight =>
        {
            // 计算距离底部20%的位置作为Margin的Bottom值
            var bottomMargin = listBoxHeight * 0.2;
            return new Thickness(0, 0, 0, bottomMargin);
        });

    public static FuncValueConverter<SyncStatus, IBrush> SyncStateToBrush { get; } =
        new FuncValueConverter<SyncStatus, IBrush>(state =>
        {
            return state switch
            {
                SyncStatus.Disconnected => Brushes.Orange,
                SyncStatus.SyncError => Brushes.IndianRed,
                SyncStatus.Synced => Brushes.ForestGreen,
                _ => Brushes.Transparent,
            };
        });

    public static FuncValueConverter<SyncStatus, string> SyncStateToText { get; } =
        new FuncValueConverter<SyncStatus, string>(I18nHelper.GetString);

    public static FuncValueConverter<bool, double> LocalFileReadyToOpacity { get; } =
        new FuncValueConverter<bool, double>(isLocalFileReady =>
        {
            return isLocalFileReady ? 1.0 : 0.5;
        });

    /// <summary>
    /// 多值转换器：当 showSyncState 为 true 且 showPreviewPanel 为 false 时返回 true
    /// 用于同步状态颜色条的可见性控制
    /// </summary>
    public static IMultiValueConverter ShowSyncStatusIndicator { get; } =
        new FuncMultiValueConverter<bool, bool>(values =>
        {
            var valuesList = new List<bool>();
            foreach (var v in values)
            {
                if (v is bool b)
                    valuesList.Add(b);
                else
                    valuesList.Add(false);
            }

            if (valuesList.Count >= 2)
            {
                var showSyncState = valuesList[0];
                var showPreviewPanel = valuesList[1];
                return showSyncState && !showPreviewPanel;
            }
            return false;
        });
}
