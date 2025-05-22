using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using ImageMagick;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Desktop.ClipboardAva.ClipboardReader;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.ViewModels;

internal partial class DiagnoseDetailViewModel : ObservableObject
{
    private readonly MultiSourceClipboardReader Clipboard = App.Current.Services.GetRequiredService<MultiSourceClipboardReader>();

    [ObservableProperty]
    private bool isImage;
    [ObservableProperty]
    private bool isText;
    [ObservableProperty]
    private bool isString;

    [ObservableProperty]
    private string? csharpString;
    [ObservableProperty]
    private string? ansi;
    [ObservableProperty]
    private string? utf8;
    [ObservableProperty]
    private string? utf16;
    [ObservableProperty]
    private string? utf32;
    [ObservableProperty]
    private uint? uint_32;
    [ObservableProperty]
    private int? int_32;
    [ObservableProperty]
    private Bitmap? bitmap;
    //[ObservableProperty]
    //private string? unicode;
    //[ObservableProperty]
    //private string? unicode;

    private void Clear()
    {
        IsImage = false;
        IsString = false;
        IsText = false;
        CsharpString = null;
        Ansi = null;
        Utf8 = null;
        Utf16 = null;
        Utf32 = null;
        Bitmap = null;
    }

    public async Task Init(string formatAndCSType)
    {
        try
        {
            Clear();
            var type = formatAndCSType.Split(Environment.NewLine)[0];
            var clipboard = await Clipboard.GetDataAsync(type!, CancellationToken.None);
            if (clipboard is string str)
            {
                IsString = true;
                CsharpString = str;
            }

            await ProcessImageBytes(clipboard);
            if (IsImage is not true)
            {
                ProcessStringBytes(clipboard);
            }
        }
        catch
        {
        }
    }

    private async Task ProcessImageBytes(object? clipboard)
    {
        if (clipboard is byte[] bytes)
        {
            try
            {
                using MagickImage image = new(bytes);
                using MemoryStream ms = new MemoryStream();
                var path = Path.Combine(Core.Commons.Env.TemplateFileFolder, "diagnose.bmp");
                await image.WriteAsync(path, MagickFormat.Bmp);
                Bitmap = new Bitmap(path);
                IsImage = true;
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex.Message);
            }
        }
    }

    private void ProcessStringBytes(object? clipboard)
    {
        if (clipboard is byte[] bytes)
        {
            IsText = true;
            Ansi = AdjustString(Encoding.ASCII.GetString(bytes));
            Utf8 = AdjustString(Encoding.UTF8.GetString(bytes));
            Utf16 = AdjustString(Encoding.Unicode.GetString(bytes));
            Utf32 = AdjustString(Encoding.UTF32.GetString(bytes));
            try
            {
                Int_32 = BitConverter.ToInt32(bytes);
                Uint_32 = BitConverter.ToUInt32(bytes);
            }
            catch { }
        }
    }

    private static string AdjustString(string str)
    {
        int maxLength = 2000;
        return str.Length <= maxLength ? str : str[..maxLength];
    }
}
