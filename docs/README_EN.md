# SyncClipboard

[中文](../README.md) | English

<details>
<summary>Contents</summary>

- [SyncClipboard](#syncclipboard)
  - [Features](#features)
  - [Server](#server)
    - [Standalone Server](#standalone-server)
      - [Server Configuration](#server-configuration)
      - [Docker](#docker)
      - [Arch Linux](#arch-linux)
    - [Desktop Client Built-in Server](#desktop-client-built-in-server)
    - [WebDAV Server](#webdav-server)
  - [Client](#client)
    - [Windows](#windows)
      - [Portable Version](#portable-version)
      - [Troubleshooting](#troubleshooting)
    - [macOS](#macos)
      - [Manual Installation](#manual-installation)
      - [Troubleshooting](#troubleshooting-1)
    - [Linux](#linux)
      - [Manual Installation](#manual-installation-1)
      - [Arch Linux](#arch-linux-1)
      - [Troubleshooting](#troubleshooting-2)
    - [Desktop Client Command Line Arguments](#desktop-client-command-line-arguments)
      - [--shutdown-previous](#--shutdown-previous)
      - [--command-{command-name}](#--command-command-name)
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
[SyncClipboard.Server](https://github.com/Jeric-X/SyncClipboard/releases/) is cross-platform, depends on [ASP.NET Core 8.0](https://dotnet.microsoft.com/en-us/download/dotnet/8.0). Run with:
```
dotnet /path/to/SyncClipboard.Server.dll --contentRoot ./
```
Content root folder is `SyncClipboard.Server.dll`'s parent folder. Writing permission is needed. Choosing a different content root folder is possible. Copy a new `appsettings.json` to the folder and run with:
```
dotnet /path/to/SyncClipboard.Server.dll --contentRoot /path/to/contentRoot
```

#### Server Configuration
`appsettings.json` is the config file.
```jsonc
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "Kestrel": {
    "Endpoints": {
      "http": {
        "Url": "http://*:5033"
      },
      //"https": {
      //  "Url": "https://*:5033"
      //}
    },
    //"Certificates": {
    //  "Default": {
    //    "Path": "/path/to/pem",
    //    "KeyPath": "/path/to/pem_key"
    //  }
    //}
  },
  "AppSettings": {
    "UserName": "your_username",
    "Password": "your_password"
  }
}
```
For more information, please refer to the [official Microsoft documentation](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/endpoints?view=aspnetcore-8.0#configure-https-in-appsettingsjson).

Username and password can be set by environment variables. When the environment variables `SYNCCLIPBOARD_USERNAME` and `SYNCCLIPBOARD_PASSWORD` are both set, they will be used as the username and password.  

`ASPNETCORE_hostBuilder__reloadConfigOnChange` is used to configure whether to automatically detect changes in `appsettings.json` and reload the configuration. The default value is false. Changing it to any value other than false will enable this feature.

> [!WARNING]  
> HTTP transmits data in plaintext. When deploying the server on a public network, please enable HTTPS or configure HTTPS using a reverse proxy tool. If obtaining a certificate from a certificate authority is not possible, it is recommended to use the open-source tool [mkcert](https://github.com/FiloSottile/mkcert) or other methods to generate a self-signed certificate.

#### Docker

```shell
# docker
docker run -d \
  --name=syncclipboard-server \
  -p 5033:5033 \
  -e SYNCCLIPBOARD_USERNAME=your_username \
  -e SYNCCLIPBOARD_PASSWORD=your_password \
  --restart unless-stopped \
  jericx/syncclipboard-server:latest

# docker compose
curl -sL https://github.com/Jeric-X/SyncClipboard/raw/master/src/SyncClipboard.Server/docker-compose.yml
docker compose up -d
```
To configure HTTPS, map the `appsettings.json` and certificate files manually. The path for `appsettings.json` inside the container is `/app/appsettings.json`.

#### Arch Linux

You can install it directly from [AUR](https://aur.archlinux.org/packages/syncclipboard-server) (maintained by [@devome](https://github.com/devome)):

```shell
paru -Sy syncclipboard-server
```

The configuration file path is `/etc/syncclipboard/appsettings.json`. After modifying the configuration, you can start the service using `systemctl` command:

```shell
sudo systemctl enable --now syncclipboard.service
```

### Desktop Client Built-in Server
Desktop client (Windows/Linux/macOS) has a built-in server, can be configured with GUI.

### WebDAV Server
  
Tested server：   
- [x] [Nextcloud](https://nextcloud.com/)
- [x] [AList](https://alist.nn.ci/)
- [x] [InfiniCLOUD](https://infini-cloud.net/en/)
- [x] [aliyundrive-webdav](https://github.com/messense/aliyundrive-webdav)

## Client

Clipboard is auto-synced between desktop clients running on Windows/Linux/macOS.

<details>
<summary>Screenshots</summary>

![](image/WinUI_EN.png)

</details>

### Windows
#### Portable Version

Download the zip file starting with `SyncClipboard_win_` from the [Release](https://github.com/Jeric-X/SyncClipboard/releases/latest) page. Extract it and run `SyncClipboard.exe`.

#### Troubleshooting
- The minimum supported OS version is Windows 10 2004.
- If the interface icons are displayed incorrectly on Windows 10, download and install the Microsoft [Segoe Fluent Icons](https://aka.ms/SegoeFluentIcons) font.

### macOS
#### Manual Installation
Download the installation package starting with `SyncClipboard_macos_` from the [Release](https://github.com/Jeric-X/SyncClipboard/releases/latest) page. Double-click it and drag the SyncClipboard icon to the Applications folder.

#### Troubleshooting
- System prompts `“SyncClipboard” cannot be opened because the developer cannot be verified`: Go to `Settings` -> `Privacy & Security` on macOS, and click `Open Anyway`.
- System prompts `"SyncClipboard" is damaged, can't be opened`: Run the following command in the terminal: `sudo xattr -d com.apple.quarantine /Applications/SyncClipboard.app`
- Hotkeys require Accessibility permissions. The software will prompt for authorization when needed.

### Linux
#### Manual Installation
Download the installation package starting with `SyncClipboard_linux_` from the [Release](https://github.com/Jeric-X/SyncClipboard/releases/latest) page.

#### Arch Linux

Arch Linux users can directly install from [AUR](https://aur.archlinux.org/packages/syncclipboard-desktop) (maintained by [@devome](https://github.com/devome)):

```shell
paru -Sy syncclipboard-desktop
```

After installation, you can launch it from the menu. If launching via the command `syncclipboard-desktop` results in an error, set the environment variable `LANG` to `en_US.UTF-8` or just start it using `LANG=en_US.UTF-8 syncclipboard-desktop`.

#### Troubleshooting
- Clipboard sync is delayed, fails, or uploads garbled text: It is recommended to install `xclip` (for X11) or `wl-clipboard` (for Wayland) on your system. SyncClipboard will use these tools to help access the clipboard and improve stability. Use the commands `xclip -version` or `wl-paste -version` to check if they are installed.
- When upgrading using `deb` or `rpm` installation packages, if the upgrade fails, please uninstall the old version before installing the new one.
- When using the `AppImage` package, please ensure that the AppImage file has executable permissions.
- Hotkeys may not work on Wayland.
- The language cannot be auto-detected and defaults to English. You can change the language in SyncClipboard's settings after launching.

> [!NOTE]  
> To completely remove SyncClipboard, manually delete the configuration and temporary file directories:  
> `%AppData%\SyncClipboard\` (Windows), `~/Library/Application Support/SyncClipboard/` (macOS), `~/.config/SyncClipboard/` (Linux)

### Desktop Client Command Line Arguments

#### --shutdown-previous
Closes any running instance of SyncClipboard and starts a new one.

#### --command-{command-name}
Executes the specified command, where `{command-name}` is the name of the command. After setting a shortcut key, you can view the corresponding command name in the configuration file. Even if the shortcut key configuration is cleared, the command line argument remains valid.  
If a SyncClipboard instance is already running, it will not start a new instance but will instruct the running SyncClipboard to execute the command. If no instance is running, the command will be executed immediately after startup.  
Multiple `--command-{command-name}` arguments are supported, multiple commands are executed simultaneously.  

> [!NOTE]  
> When using command line arguments on macOS, please use the full path to the executable: `/Applications/SyncClipboard.app/Contents/MacOS/SyncClipboard.Desktop.MacOS`

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
[NativeNotification](https://github.com/Jeric-X/NativeNotification) 
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
[Quartz.NET](https://github.com/quartznet/quartznet)   