using ImageMagick;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.ClipboardAva;

internal sealed class WlClipboardWriter
{
    private readonly string _executable;

    public WlClipboardWriter() : this("wl-copy") { }

    internal WlClipboardWriter(string executable) => _executable = executable;

    public async Task WriteTextAsync(string text, CancellationToken token)
    {
        using var data = new MemoryStream(Encoding.UTF8.GetBytes(text));
        await CopyAsync(data, "text/plain;charset=utf-8", token);
    }

    public async Task WriteFilesAsync(string[] files, CancellationToken token)
    {
        if (files.Length == 0) throw new ArgumentException("No files to copy.", nameof(files));
        var uris = files.Select(file => new Uri(Path.GetFullPath(file)).AbsoluteUri);
        using var data = new MemoryStream(Encoding.UTF8.GetBytes(string.Join("\r\n", uris) + "\r\n"));
        await CopyAsync(data, "text/uri-list", token);
    }

    public async Task WriteImageAsync(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var image = new MagickImage(path);
        using var data = new MemoryStream();
        await image.WriteAsync(data, MagickFormat.Png, token);
        data.Position = 0;
        await CopyAsync(data, "image/png", token);
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
