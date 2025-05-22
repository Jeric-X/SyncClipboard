using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Utilities;

namespace SyncClipboard.Desktop.ClipboardAva.ClipboardReader;

[SupportedOSPlatform("linux")]
public class XClipReader : IClipboardReader
{
    bool _inited = false;

    public XClipReader(ILogger logger)
    {
        Task.Run(new Action(() =>
        {
            var psi = new ProcessStartInfo
            {
                FileName = "xclip",
                Arguments = "-version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            process.Start();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                string error = process.StandardError.ReadToEnd();
                logger.Write("XClipReader", $"xclip error {process.ExitCode}: {error}");
            }
            else
            {
                _inited = true;
                logger.Write("XClipReader", "xclip initialized");
            }
        }).NoExcept());
    }

    public async Task<string[]?> GetFormatsAsync(CancellationToken token)
    {
        if (await GetDataAsyncUsingXclip(Format.Targets, token) is not byte[] textBytes)
            return null;
        var formatsStr = Encoding.UTF8.GetString(textBytes);
        return formatsStr.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
    }

    public async Task<string?> GetTextAsync(CancellationToken token)
    {
        return await GetDataAsync(Format.Text, token) as string;
    }

    public async Task<object?> GetDataAsync(string format, CancellationToken token)
    {
        if (format == Format.Targets)
        {
            return await GetFormatsAsync(token);
        }
        else if (format == Format.TimeStamp)
        {
            return await GetTimeStamp(token);
        }

        return await GetDataAsyncUsingXclip(format, token);
    }

    public async Task<int?> GetTimeStamp(CancellationToken token)
    {
        if (await GetDataAsyncUsingXclip(Format.TimeStamp, token) is not byte[] textBytes)
            return null;
        var timeStampStr = Encoding.UTF8.GetString(textBytes);
        return int.TryParse(timeStampStr, out var timeStamp) ? timeStamp : BitConverter.ToInt32(textBytes);
    }

    public async Task<object?> GetDataAsyncUsingXclip(string format, CancellationToken token)
    {
        if (!_inited)
        {
            return null;
        }

        var psi = new ProcessStartInfo
        {
            FileName = "xclip",
            Arguments = $"-selection clipboard -o -t {format}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        using MemoryStream memoryStream = new();
        var copyTask = process.StandardOutput.BaseStream.CopyToAsync(memoryStream, token);
        await process.WaitForExitAsync(token);
        await copyTask;

        if (process.ExitCode != 0)
        {
            string error = await process.StandardError.ReadToEndAsync(token);
            throw new Exception($"xclip error: {error}");
        }

        return memoryStream.ToArray();
    }
}
