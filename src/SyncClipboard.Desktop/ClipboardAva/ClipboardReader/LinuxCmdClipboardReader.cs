using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Utilities;

namespace SyncClipboard.Desktop.ClipboardAva.ClipboardReader;

[SupportedOSPlatform("linux")]
public class LinuxCmdClipboardReader : IClipboardReader
{
    public string SourceName => _name;
    bool _inited = false;
    private readonly ILogger _logger;
    private readonly string _cmd;
    private readonly string _paraPrefix;
    private readonly string _name;

    public LinuxCmdClipboardReader(ILogger logger, string name, string cmd, string paraPrefix)
    {
        _logger = logger;
        _cmd = cmd;
        _paraPrefix = paraPrefix;
        _name = name;
        _ = Init();
    }

    private async Task Init()
    {
        try
        {
            var bytes = await GetDataByParasAsync("-version", CancellationToken.None) as byte[];
            var str = Encoding.UTF8.GetString(bytes!);
            _logger.Write(_name, $"{_name} initialized");
            _inited = true;
        }
        catch (Exception ex)
        {
            _logger.Write(_name, $"Init error: {ex.Message}");
        }
    }

    public virtual async Task<string[]?> GetFormatsAsync(CancellationToken token)
    {
        if (await GetDataByFormatAsync(Format.Targets, token) is not byte[] textBytes)
            return null;
        var formatsStr = Encoding.UTF8.GetString(textBytes);
        return formatsStr.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
    }

    public virtual async Task<string?> GetTextAsync(CancellationToken token)
    {
        return await GetDataAsync(Format.TEXT, token) as string;
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

        return await GetDataByFormatAsync(format, token);
    }

    public async Task<int?> GetTimeStamp(CancellationToken token)
    {
        if (await GetDataByFormatAsync(Format.TimeStamp, token) is not byte[] textBytes)
            return null;
        var timeStampStr = Encoding.UTF8.GetString(textBytes);
        return int.TryParse(timeStampStr, out var timeStamp) ? timeStamp : BitConverter.ToInt32(textBytes);
    }


    public async Task<object?> CheckAndGetDataByParasAsync(string parameters, CancellationToken token)
    {
        if (!_inited)
        {
            return null;
        }
        return await GetDataByParasAsync(parameters, token);
    }

    private async Task<object?> GetDataByParasAsync(string parameters, CancellationToken token)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _cmd,
            Arguments = parameters,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        using MemoryStream memoryStream = new();
        var stdoutTask = process.StandardOutput.BaseStream.CopyToAsync(memoryStream, token);
        var stdErrTask = process.StandardError.ReadToEndAsync(token);
        await process.WaitForExitAsync(token);

        if (process.ExitCode != 0)
        {
            string error = await stdErrTask;
            throw new Exception($"error: {error}");
        }

        await stdoutTask;
        return memoryStream.ToArray();
    }

    public Task<object?> GetDataByFormatAsync(string format, CancellationToken token)
    {
        return CheckAndGetDataByParasAsync($"{_paraPrefix} {format}", token);
    }
}
