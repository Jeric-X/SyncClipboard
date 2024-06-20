# SyncClipboard

[中文](../README.md) | English

<details>
<summary>Contents</summary>

- [SyncClipboard](#syncclipboard)
  - [Features](#features)
  - [Server](#server)
    - [Standalone Server](#standalone-server)
      - [Docker Container](#docker-container)
        - [Docker](#docker)
        - [Docker Compose](#docker-compose)
    - [Desktop Client Built-in Server](#desktop-client-built-in-server)
    - [WebDAV Server](#webdav-server)
  - [Client](#client)
    - [Windows](#windows)
    - [Linux, macOS](#linux-macos)
    - [IOS](#ios)
      - [Use Shortcuts](#use-shortcuts)
    - [Android](#android)
      - [Use HTTP Request Shortcuts](#use-http-request-shortcuts)
      - [Use Autox.js](#use-autoxjs)
    - [Notes for Clients](#notes-for-clients)
  - [API](#api)
    - [Download/Upload Text](#downloadupload-text)
    - [Download/Upload File/Image](#downloadupload-fileimage)
    - [SyncClipboard.json](#syncclipboardjson)
  - [Open Source Dependencies](#open-source-dependencies)

</details>

## Features

- Clipboard syncing, supporting text/image/file, client-server architecture. You can also use a WebDAV compatible netdisk as server.
- Optimize image type clipboard:
  - Paste image to a textbox directly after copying a image file from file system, and vice versa.
  - Download the original file and copy it after copying a image in web browser. This is helpful for copying an animated image in browser. Web sites always prevent downloads from non-browser, so this feature isn't always usable.
  - Copy the transcoded temporary image file (jpg or gif) after copying a modern image file type (heic, webp, etc.).

## Server
### Standalone Server
[SyncClipboard.Server](https://github.com/Jeric-X/SyncClipboard/releases/) is cross-platform, depends on [ASP.NET Core 6.0](https://dotnet.microsoft.com/en-us/download/dotnet/6.0). Run with:
```
dotnet /path/to/SyncClipboard.Server.dll --contentRoot ./
```
Content root folder is `SyncClipboard.Server.dll`'s parent folder, there will be temporary folders created when running.  
Port, username, password can be changed in `appsettings.json`.  
Choosing a different content root folder is possible. Copy a new `appsettings.json` to the folder and run with:
```
dotnet /path/to/SyncClipboard.Server.dll --contentRoot /path/to/contentRoot
```
Notes：
- Default Username is `admin`, default Password is `admin`, default port is `5033`.
- Address to fill in client is `http://ip(or domain name):port`, nothing can be omitted.
- Http is not encrypted, including username and password. Maybe a https reverse proxy is needed on public network.

#### Docker Container

Copy a server config file [appsettings.json](https://github.com/Jeric-X/SyncClipboard/raw/master/src/SyncClipboard.Server/appsettings.json) first.

```
mkdir syncclipboard-server && cd syncclipboard-server
curl -sL https://github.com/Jeric-X/SyncClipboard/raw/master/src/SyncClipboard.Server/appsettings.json > appsettings.json
```
Change your own username and password in `appsettings.json`.

##### Docker

Run the following command. Remember to change the `/path/to/appsettings.json` to your newly copied `appsettings.json`'s path.

```
docker run -d \
  --name=syncclipboard-server \
  -p 5033:5033 \
  -v /path/to/appsettings.json:/app/appsettings.json \
  --restart unless-stopped \
  jericx/syncclipboard-server:latest
```

##### Docker Compose

Copy a [docker-compose.yml](https://github.com/Jeric-X/SyncClipboard/raw/master/src/SyncClipboard.Server/docker-compose.yml) file.

```
curl -sL https://github.com/Jeric-X/SyncClipboard/raw/master/src/SyncClipboard.Server/docker-compose.yml > docker-compose.yml
```

Change the `/path/to/appsettings.json` in `docker-compose.yml` to your newly copied `appsettings.json`'s path, then run

```
docker compose up -d
```

Notes:
- Server related files are under `src/SyncClipboard.Server` directory. You can download them manually.
- Docker image is hosted on [Docker Hub/jericx/syncclipboard-server](https://hub.docker.com/r/jericx/syncclipboard-server).

### Desktop Client Built-in Server
Desktop client (Windows/Linux/macOS) has a built-in server, basically the same as standalone server but can be configured with GUI.

### WebDAV Server
  
Tested server：   
- [x] [Nextcloud](https://nextcloud.com/)
- [x] [坚果云](https://www.jianguoyun.com/) 
- [x] [AList](https://alist.nn.ci/)
- [x] [InfiniCLOUD](https://infini-cloud.net/en/)

## Client

Clipboard is auto-synced between desktop clients running on Windows/Linux/macOS.

<details>
<summary>Screenshots</summary>

![](image/WinUI_EN.png)

</details>

### Windows   
Download the `SyncClipboard.zip` from [Release](https://github.com/Jeric-X/SyncClipboard/releases/) Page.  
After extracting, run `SyncClipboard.exe`.  
  
Dependencies：   
- [.NET 6.0 Desktop](https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-desktop-6.0.16-windows-x64-installer) runtime
- [ASP.NET Core 6.0](https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-aspnetcore-6.0.16-windows-x64-installer) runtime    
- Windows10 2004 or above
- Microsoft [Segoe Fluent Icons](https://learn.microsoft.com/zh-cn/windows/apps/design/style/segoe-fluent-icons-font). It is included by default on Windows 11. You can download it [here](https://aka.ms/SegoeFluentIcons).
  
Notes: 
- Config files are saved in `%AppData%\SyncClipboard\`. Users can delete them manually.

### Linux, macOS
Downloading page: [SyncClipboard.Desktop](https://github.com/Jeric-X/SyncClipboard.Desktop/releases)  

Notes:
- File name with `no-self-contained`: [.NET 6.0 Desktop](https://dotnet.microsoft.com/en-us/download/dotnet/6.0) runtime and [ASP.NET Core 6.0](https://dotnet.microsoft.com/en-us/download/dotnet/6.0) runtime are required.
- File name with `self-contained`: should run with no dependencies.
- Config files are saved in `~/.config/SyncClipboard/`(Linux), `~/Library/Application Support/SyncClipboard/`(macOS). Uninstaller won't delete them. Users can delete them manually.
- Not suport upgrading directly with `deb` or `rpm` package. Delete old version first, then install the new version.
- Linux: Hotkey is not supported on Wayland.
- macOS: `"SyncClipboard" is damaged, can't be opened`. Use the following command in terminal: `sudo xattr -d com.apple.quarantine /Applications/SyncClipboard.app`
- macOS: Hotkey requires Accessibility permission. After installing a new version, the permission should be re-granted.

### IOS 
#### Use [Shortcuts](https://apps.apple.com/us/app/shortcuts/id1462947752)  

- Sync manually, import this [Shortcut](https://www.icloud.com/shortcuts/1a57093a04ec4f45a5ca7d4de82020e7)
- Sync Automatically, import this [Shortcut](https://www.icloud.com/shortcuts/542ad23b33314b36807c05a5d8aa5c22). This shortcut keeps running in the background forever, you need to stop it manually. You can also change whether to send notifications and querying interval time manullay.

### Android
#### Use [HTTP Request Shortcuts](https://github.com/Waboodoo/HTTP-Shortcuts)
Import this [file](/script/en/shortcuts.zip), Change the `UserName`, `UserToken`, `url` in `Variables` to yours. Make sure no slash(/) at the end of url. `HTTP Request Shortcuts` supports using shortcuts from drop-down menu, home screen widgets, home screen icons and share sheet.

<details>
<summary>Screenshots</summary>

![](image/android1_EN.jpg)
![](image/android2_EN.jpg)
![](image/android3_EN.jpg)

</details>

#### Use [Autox.js](https://github.com/kkevsekk1/AutoX)
Import this [js file](/script/SyncAutoxJs.js). Change the user config. And set a running trigger, for example, running the script when Android system startup.
```
// START  User Config  
const url = 'http://192.168.5.194:5033'
const username = 'admin'
const token = 'admin'
const intervalTime = 3 * 1000                         // 3 seconds
const showToastNotification = true
// END    User Config  
```
Running in background, the script will download the remote text clipbaord automatically and set it to local clipboard. 
If satisfy any of the following conditions, upload is automatic.
- The app is running in forground
- Android 9 Pie or lower Android version
- Use root-based tools like Magisk/Xposed to unlock the limition of clipboard operation in background. There are some references:
  - https://github.com/Kr328/Riru-ClipboardWhitelist
  - https://github.com/GamerGirlandCo/xposed-clipboard-whitelist
  - https://modules.lsposed.org/module/io.github.tehcneko.clipboardwhitelist
  - https://github.com/QueallyTech/DisableLogRequest

### Notes for Clients

There are three necessery config(maybe different words, same uses).
- username
- password
- url, format is `http://ip(or domain name):port`. When using a WebDav server, url needs to be pointed to a specific existing folder as the working folder, like `https://domain.com/dav/folder1/working%20folder`. File name is the best not to contain any special characters or spaces, or you'll have to URL encode it. And do not use this folder to do anything else. If not using a desktop client(Windows/Linux/macOS), create a folder named `file` in the working folder to sync files. Desktop clients create this folder automatically. Make sure no slash(/) at the end of url.

## API

### Download/Upload Text
```
GET /SyncClipboard.json
PUT /SyncClipboard.json
```

### Download/Upload File/Image
```
GET  /SyncClipboard.json
HEAD /file/filename         // optional
GET  /file/filename

PUT /file/filename
PUT /SyncClipboard.json
```

### SyncClipboard.json
```
{
    "Type" : "Text"
    "Clipboard" : "Content",
    "File":""
}

{
    "Type": "Image", // or "File", "Group"
    "Clipboard": "hash, optional",
    "File": "filename"
}
```

## Open Source Dependencies
[Magick.NET](https://github.com/dlemstra/Magick.NET)  
[.NET Community Toolkit](https://github.com/CommunityToolkit/dotnet)  
[H.NotifyIcon](https://github.com/HavenDV/H.NotifyIcon)  
[WinUIEx](https://github.com/dotMorten/WinUIEx)  
[moq](https://github.com/moq/moq)  
[Avalonia](https://avaloniaui.net/)  
[FluentAvalonia.BreadcrumbBar](https://github.com/indigo-san/FluentAvalonia.BreadcrumbBar)  
[FluentAvalonia](https://github.com/amwx/FluentAvalonia)  
[Vanara](https://github.com/dahall/Vanara)  
[Tmds.DBus](https://github.com/tmds/Tmds.DBus)  
[SharpHook](https://github.com/TolikPylypchuk/SharpHook)  
[DotNetZip.Semverd](https://github.com/haf/DotNetZip.Semverd)  