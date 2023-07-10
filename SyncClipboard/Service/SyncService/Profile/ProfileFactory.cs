
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Utilities.Image;
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using static SyncClipboard.Service.ProfileType;
#nullable enable

namespace SyncClipboard.Service
{
    public static class ProfileFactory
    {
        public static async Task<Profile> CreateFromRemote(IWebDav webDav, CancellationToken cancelToken)
        {
            JsonProfile? jsonProfile;
            try
            {
                jsonProfile = await webDav.GetJson<JsonProfile>(SyncService.REMOTE_RECORD_FILE, cancelToken);
            }
            catch (Exception ex)
            {
                if (ex is HttpRequestException { StatusCode: HttpStatusCode.NotFound })
                {
                    var blankProfile = new TextProfile("");
                    await blankProfile.UploadProfileAsync(webDav, cancelToken);
                    return blankProfile;
                }
                Global.Logger.Write("CreateFromRemote failed");
                throw;
            }

            ArgumentNullException.ThrowIfNull(jsonProfile);
            ClipboardType type = StringToClipBoardType(jsonProfile.Type);
            return GetProfileBy(type, jsonProfile, webDav);
        }

        private static Profile GetProfileBy(ClipboardType type, JsonProfile jsonProfile, IWebDav webDav)
        {
            switch (type)
            {
                case ClipboardType.Text:
                    return new TextProfile(jsonProfile.Clipboard);
                case ClipboardType.File:
                    {
                        if (ImageHelper.FileIsImage(jsonProfile.File))
                        {
                            return new ImageProfile(jsonProfile, webDav, Global.Logger, Global.UserConfig);
                        }
                        return new FileProfile(jsonProfile, webDav, Global.Logger, Global.UserConfig);
                    }
                case ClipboardType.Image:
                    return new ImageProfile(jsonProfile, webDav, Global.Logger, Global.UserConfig);
            }

            return new UnkonwnProfile();
        }
    }
}
