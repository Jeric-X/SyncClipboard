using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using SyncClipboard.Core;
using SyncClipboard.Core.Utilities;
using System;
using System.Text.RegularExpressions;

namespace SyncClipboard.WinUI3.Controls;

/// <summary>
/// 增强的文本显示控件，自动检测并渲染超链接
/// 支持文本选择、链接点击
/// </summary>
public sealed partial class HyperlinkTextBlock : UserControl
{
    /// <summary>
    /// 需要解析链接的文本依赖属性
    /// </summary>
    public static readonly DependencyProperty LinkTextProperty =
        DependencyProperty.Register(
            nameof(LinkText),
            typeof(string),
            typeof(HyperlinkTextBlock),
            new PropertyMetadata(string.Empty, OnLinkTextPropertyChanged));

    /// <summary>
    /// 需要解析链接的文本
    /// </summary>
    public string LinkText
    {
        get => (string)GetValue(LinkTextProperty);
        set => SetValue(LinkTextProperty, value);
    }

    /// <summary>
    /// 文本换行依赖属性
    /// </summary>
    public static readonly DependencyProperty TextWrappingProperty =
        DependencyProperty.Register(
            nameof(TextWrapping),
            typeof(TextWrapping),
            typeof(HyperlinkTextBlock),
            new PropertyMetadata(TextWrapping.WrapWholeWords, OnTextWrappingPropertyChanged));

    /// <summary>
    /// 文本换行
    /// </summary>
    public TextWrapping TextWrapping
    {
        get => (TextWrapping)GetValue(TextWrappingProperty);
        set => SetValue(TextWrappingProperty, value);
    }

    public HyperlinkTextBlock()
    {
        this.InitializeComponent();
    }

    private static void OnLinkTextPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HyperlinkTextBlock control)
        {
            control.ParseAndRenderLinks();
        }
    }

    private static void OnTextWrappingPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HyperlinkTextBlock control)
        {
            control._TextBlock.TextWrapping = (TextWrapping)e.NewValue;
        }
    }

    /// <summary>
    /// 解析文本中的 URL 并渲染为带链接样式的 Paragraph
    /// </summary>
    private void ParseAndRenderLinks()
    {
        _TextBlock.Blocks.Clear();

        var text = LinkText;
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var paragraph = new Paragraph();
        int currentIndex = 0;
        var matches = StringHelper.UrlRegex().Matches(text);

        // 如果没有链接，直接添加整个文本
        if (matches.Count == 0)
        {
            paragraph.Inlines.Add(new Run { Text = text });
            _TextBlock.Blocks.Add(paragraph);
            return;
        }

        // 有链接时，构建 Inlines
        foreach (Match match in matches)
        {
            // 添加链接前的普通文本
            if (match.Index > currentIndex)
            {
                var plainText = text[currentIndex..match.Index];
                paragraph.Inlines.Add(new Run { Text = plainText });
                currentIndex = match.Index;
            }

            // 添加链接文本
            var linkText = match.Value;
            paragraph.Inlines.Add(CreateLinkInline(linkText));

            currentIndex += linkText.Length;
        }

        // 添加剩余的普通文本
        if (currentIndex < text.Length)
        {
            var remainingText = text[currentIndex..];
            paragraph.Inlines.Add(new Run { Text = remainingText });
        }

        _TextBlock.Blocks.Add(paragraph);
    }

    private static Inline CreateLinkInline(string linkText)
    {
        try
        {
            var uri = new Uri(linkText, UriKind.Absolute);
            // WinRT 使用 OriginalString，需用已转义的 AbsoluteUri 重新构造 Uri。
            var hyperlink = new Hyperlink { NavigateUri = new Uri(uri.AbsoluteUri, UriKind.Absolute) };
            hyperlink.Inlines.Add(new Run { Text = linkText });
            return hyperlink;
        }
        catch (Exception ex) when (ex is UriFormatException or ArgumentException)
        {
            AppCore.TryGetCurrent()?.Logger.Write(nameof(HyperlinkTextBlock), $"Failed to create hyperlink: {ex.Message}");
            return new Run { Text = linkText };
        }
    }
}
