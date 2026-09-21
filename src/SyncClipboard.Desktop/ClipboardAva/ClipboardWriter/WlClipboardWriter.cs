using Avalonia.Input;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.ClipboardAva.ClipboardWriter;

internal sealed class WlClipboardWriter : IClipboardWriter
{
    private readonly string _executable;

    public WlClipboardWriter() : this("wl-copy") { }

    internal WlClipboardWriter(string executable) => _executable = executable;

    public string SourceName => "wl-clipboard";

    public async Task SetTextAsync(string text, CancellationToken token)
    {
        using var data = new MemoryStream(Encoding.UTF8.GetBytes(text));
        await CopyAsync(data, "text/plain;charset=utf-8", token);
    }

    public async Task SetDataAsync(DataTransfer transfer, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        // Image packages also contain file/URI representations. Prefer the actual image.
        var pngFormat = DataFormat.CreateBytesPlatformFormat("image/png");
        var png = transfer.Items.Select(item => item.TryGetRaw(pngFormat)).OfType<byte[]>().FirstOrDefault();
        if (png is not null)
        {
            using var data = new MemoryStream(png);
            await CopyAsync(data, "image/png", token);
            return;
        }

        var uriFormat = DataFormat.CreateBytesPlatformFormat("text/uri-list");
        var uris = transfer.Items.Select(item => item.TryGetRaw(uriFormat)).OfType<byte[]>().FirstOrDefault();
        if (uris is not null)
        {
            var lines = Encoding.UTF8.GetString(uris).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0) throw new ArgumentException("No files to copy.", nameof(transfer));
            using var data = new MemoryStream(Encoding.UTF8.GetBytes(string.Join("\r\n", lines) + "\r\n"));
            await CopyAsync(data, "text/uri-list", token);
            return;
        }

        var text = transfer.Items.Select(item => item.TryGetRaw(DataFormat.Text)).OfType<string>().FirstOrDefault();
        if (text is not null)
        {
            await SetTextAsync(text, token);
            return;
        }

        throw new NotSupportedException("The clipboard package contains no supported text, PNG image, or file list.");
    }

    private async Task CopyAsync(Stream data, string mimeType, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var ct = timeout.Token;
        ct.ThrowIfCancellationRequested();

        var startInfo = new ProcessStartInfo(_executable)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("--type");
        startInfo.ArgumentList.Add(mimeType);

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        using var errorCancellation = new CancellationTokenSource();
        var errorTask = process.StandardError.ReadToEndAsync(errorCancellation.Token);
        try
        {
            IOException? writeError = null;
            try
            {
                await data.CopyToAsync(process.StandardInput.BaseStream, ct);
                await process.StandardInput.BaseStream.FlushAsync(ct);
            }
            catch (IOException ex)
            {
                // A failed Wayland connection can close stdin before the payload is written.
                writeError = ex;
            }
            finally
            {
                try
                {
                    process.StandardInput.Close();
                }
                catch (IOException ex)
                {
                    writeError ??= ex;
                }
            }

            // The parent exits once the selection is installed; its child keeps serving paste requests.
            await process.WaitForExitAsync(ct);
            if (process.ExitCode != 0)
            {
                var error = await errorTask.WaitAsync(ct);
                throw new InvalidOperationException($"wl-copy failed ({process.ExitCode}): {error.Trim()}");
            }
            if (writeError is not null) throw writeError;
        }
        catch (OperationCanceledException ex) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException("wl-copy did not finish setting the clipboard within 10 seconds.", ex);
        }
        finally
        {
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None);
                }
                catch (InvalidOperationException) { }
            }

            // wl-copy's background child inherits stderr. Do not wait for clipboard ownership to end.
            await errorCancellation.CancelAsync();
            try
            {
                await errorTask;
            }
            catch (OperationCanceledException) { }
        }
    }
}
