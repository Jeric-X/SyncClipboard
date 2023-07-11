using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Utilities.Image;
using System;
using static SyncClipboard.Service.ProfileType;
using System.Net.Http;
using System.Threading.Tasks;
using System.Threading;
using System.Net;
#nullable enable

namespace SyncClipboard.Service;

public abstract class ClipboardFactoryBase : IClipboardFactory
{
    protected abstract ILogger Logger { get; set; }
    protected abstract UserConfig UserConfig { get; set; }
    protected abstract IServiceProvider ServiceProvider { get; set; }
    protected abstract IWebDav WebDav { get; set; }

    public abstract MetaInfomation GetMetaInfomation();

    public Profile CreateProfile(MetaInfomation? metaInfomation = null)
    {
        metaInfomation ??= GetMetaInfomation();

        if (metaInfomation.Files != null)
        {
            var filename = metaInfomation.Files[0];
            if (System.IO.File.Exists(filename))
            {
                if (ImageHelper.FileIsImage(filename))
                {
                    return new ImageProfile(filename, ServiceProvider);
                }
                return new FileProfile(filename, ServiceProvider);
            }
        }

        if (metaInfomation.Text != null)
        {
            return new TextProfile(metaInfomation.Text, ServiceProvider);
        }

        if (metaInfomation.Image != null)
        {
            return ImageProfile.CreateFromImage(metaInfomation.Image, ServiceProvider);
        }

        return new UnkonwnProfile();
    }

    public async Task<Profile> CreateProfileFromRemote(CancellationToken cancelToken)
    {
        JsonProfile? jsonProfile;
        try
        {
            jsonProfile = await WebDav.GetJson<JsonProfile>(SyncService.REMOTE_RECORD_FILE, cancelToken);
        }
        catch (Exception ex)
        {
            if (ex is HttpRequestException { StatusCode: HttpStatusCode.NotFound })
            {
                var blankProfile = new TextProfile("", ServiceProvider);
                await blankProfile.UploadProfileAsync(WebDav, cancelToken);
                return blankProfile;
            }
            Logger.Write("CreateFromRemote failed");
            throw;
        }

        ArgumentNullException.ThrowIfNull(jsonProfile);
        ClipboardType type = StringToClipBoardType(jsonProfile.Type);
        return GetProfileBy(type, jsonProfile);
    }

    private Profile GetProfileBy(ClipboardType type, JsonProfile jsonProfile)
    {
        switch (type)
        {
            case ClipboardType.Text:
                return new TextProfile(jsonProfile.Clipboard, ServiceProvider);
            case ClipboardType.File:
                {
                    if (ImageHelper.FileIsImage(jsonProfile.File))
                    {
                        return new ImageProfile(jsonProfile, WebDav, Logger, UserConfig);
                    }
                    return new FileProfile(jsonProfile, WebDav, Logger, UserConfig);
                }
            case ClipboardType.Image:
                return new ImageProfile(jsonProfile, WebDav, Logger, UserConfig);
        }

        return new UnkonwnProfile();
    }
}
