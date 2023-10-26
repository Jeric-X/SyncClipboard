# SyncClipboard
[![build](https://github.com/Jeric-X/SyncClipboard/actions/workflows/build.yml/badge.svg?branch=master)](https://github.com/Jeric-X/SyncClipboard/actions?query=branch%3Amaster)

中文 | [English](docs/README_EN.md)  

<details>
<summary>目录</summary>

- [SyncClipboard](#syncclipboard)
  - [功能](#功能)
  - [服务器](#服务器)
    - [独立服务器](#独立服务器)
    - [客户端内置服务器](#客户端内置服务器)
    - [WebDAV服务器](#webdav服务器)
  - [客户端](#客户端)
    - [Windows](#windows)
    - [Linux, macOS](#linux-macos)
    - [IOS](#ios)
      - [使用快捷指令](#使用快捷指令)
      - [使用JSBox](#使用jsbox)
    - [Android](#android)
      - [使用HTTP Request Shortcuts](#使用http-request-shortcuts)
    - [客户端配置说明](#客户端配置说明)
  - [API](#api)
    - [获取/上传剪切板（文字）](#获取上传剪切板文字)
    - [获取/上传剪切板（图片/文件）](#获取上传剪切板图片文件)
    - [SyncClipboard.json](#syncclipboardjson)
    - [其他查询/创建/删除](#其他查询创建删除)
  - [项目依赖](#项目依赖)

</details>

## 功能

- 剪切板同步，使用WebDAV服务器（或软件内置服务器）作为中转站，支持文字、图片和文件  
- 优化图片类型的剪切板，功能有：
  - 从任意位置复制图片时，可以直接向文件系统粘贴图片文件，反之亦然
  - 从浏览器复制图片后，后台下载原图到本地，解决无法从浏览器拷贝动态图的问题（大多网站有认证，适用范围有限，支持bilibili动态图片）
  - 从文件系统复制较新格式类型的图片文件时（webp/heic等），在剪切板内储存gif或jpg格式，用于直接向支持图片的文本框粘贴图片

## 服务器
### 独立服务器
[SyncClipboard.Server](https://github.com/Jeric-X/SyncClipboard/releases/)支持跨平台运行，依赖[ASP.NET Core 6.0](https://dotnet.microsoft.com/zh-cn/download/dotnet/6.0)，安装`ASP.NET Core 运行时`后，通过以下命令运行
```
dotnet /path/to/SyncClipboard.Server.dll --contentRoot ./
```
工作目录与dll所在目录一致，会产生临时文件，在`appsettings.json`中可以修改绑定的ip和端口，以及客户端认证需要的用户名和密码  
如需修改工作目录，拷贝一份appsettings.json到新工作目录并修改`--contentRoot`后的路径  
注意：
- 客户端处填写`http://ip:端口号`，`http`不可省略
- http使用明文传输(包括本软件用于认证使用的基于Basic Auth的账号密码)，在公网部署考虑使用反向代理工具配置SSL
- 内置服务器并不是WebDAV实现

### 客户端内置服务器
桌面客户端（Windows/Linux/macOS）内置了服务器，可以使用可视界面配置，注意事项同上

### WebDAV服务器
可以使用支持WebDAV协议的网盘作为服务器  
测试过的服务器：   
- [x] [Nextcloud](https://nextcloud.com/) 
- [x] [坚果云](https://www.jianguoyun.com/)
- [x] [AList](https://alist.nn.ci/)

注意：
- 坚果云有每月流量限制和短时间内请求次数限制，建议自行设置桌面端的`轮询服务器间隔`和`最大上传文件大小`   

## 客户端

桌面客户端（Windows/Linux/macOS）运行在后台时将自动同步剪切板
<details>
<summary>展开/折叠截图</summary>

![](docs/image/WinUI.png)

</details>

### Windows
下载地址：[Release](https://github.com/Jeric-X/SyncClipboard/releases/)页面中的`SyncClipboard.WinUI3.zip`，解压缩后运行`SyncClipboard.exe`  

依赖：   
- [.NET 6.0桌面运行时](https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-desktop-6.0.16-windows-x64-installer)，未安装会弹窗提醒并跳转到微软官方下载页面  
- [ASP.NET Core 6.0运行时](https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-aspnetcore-6.0.16-windows-x64-installer)，未安装会弹窗提醒并跳转到微软官方下载页面  
- Windows10 2004及以上
- 微软[Segoe Fluent Icons](https://learn.microsoft.com/zh-cn/windows/apps/design/style/segoe-fluent-icons-font)图标字体，Windows11自带无需安装，Windows10需要手动下载安装（[官方地址](https://aka.ms/SegoeFluentIcons)），否则界面图标会大范围出错

### Linux, macOS
下载地址：[SyncClipboard.Desktop](https://github.com/Jeric-X/SyncClipboard.Desktop/releases)，根据系统选择你需要的安装包  

注意：
- 名称中带有`no-self-contained`：依赖[.NET 6.0桌面运行时](https://dotnet.microsoft.com/en-us/download/dotnet/6.0)和[ASP.NET Core 6.0运行时](https://dotnet.microsoft.com/en-us/download/dotnet/6.0)
- 名称中带有`self-contained`：通常可以直接运行
- 暂时只支持文字，不支持图片和文件，内置服务器支持其他客户端同步图片和文件
- 删除软件时，配置文件目录不会被删除，配置文件储存在`~/.config/SyncClipboard/`，可以手动删除整个目录
- 每次更新版本需要先删除旧版，再安装新版，不支持直接更新
- 存在大量BUG

已知问题：
- macos：运行在后台时，dock栏图标无法唤起主界面，点击状态栏图标附带的菜单可以唤起

### IOS 
#### 使用[快捷指令](https://apps.apple.com/cn/app/%E5%BF%AB%E6%8D%B7%E6%8C%87%E4%BB%A4/id1462947752)  

导入这个[快捷指令](https://www.icloud.com/shortcuts/9e2f44bd12a84935b715aac9b488f6ee)，跟随引导填写配置信息。从组件栏和分享菜单中使用

#### 使用[JSBox](https://apps.apple.com/cn/app/jsbox-%E5%AD%A6%E4%B9%A0%E5%86%99%E4%BB%A3%E7%A0%81/id1312014438)
导入这个[js文件](/script/Clipboard.js)，修改`user`，`token`，`path`字段。作为键盘扩展处理文字时使用，不支持文件

### Android
#### 使用[HTTP Request Shortcuts](https://play.google.com/store/apps/details?id=ch.rmy.android.http_shortcuts)
导入这个[配置文件](/script/shortcuts.zip)，修改`变量`中的`UserName`，`UserToken`，`url`。`HTTP Request Shortcuts`支持从下拉菜单、桌面组件、桌面图标、分享菜单中使用

<details>
<summary>导入配置文件后修改配置图示</summary>

![](docs/image/android1.jpg)
![](docs/image/android2.jpg)
![](docs/image/android3.jpg)

</details>

### 客户端配置说明

全平台依赖三条必要配置（配置的拼写可能会有所不同，含义相同）。windows端可以自定义修改更多配置
- user
- password
- url，格式为http(s)://ip(或者域名):port。使用WebDav服务器时，url需要具体到一个已存在的文件夹作为工作目录，例如`https://domain.com/dav/folder1/working%20folder`，特殊符号需要使用url转义字符代替，不要使用这个文件夹存储其他文件。不使用windows客户端时需在工作目录中再创建`file`文件夹以同步文件，windows客户端会在设置服务器时自动创建`file`文件夹

## API

以下是SyncClipboard用到的且SyncClipboard.Server实现了的接口

### 获取/上传剪切板（文字）
```
GET /SyncClipboard.json
PUT /SyncClipboard.json
```

### 获取/上传剪切板（图片/文件）
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
    "Type": "Image", // or "File"
    "Clipboard": "md5 hash, optional",
    "File": "filename"
}
```

### 其他查询/创建/删除
```
PROPFIND    /
PROPFIND    /file
MKCOL       /file
DELETE      /file
```

## 项目依赖
[Magick.NET](https://github.com/dlemstra/Magick.NET)  
[Windows Community Toolkit Labs](https://github.com/CommunityToolkit/Labs-Windows)  
[.NET Community Toolkit](https://github.com/CommunityToolkit/dotnet)  
[H.NotifyIcon](https://github.com/HavenDV/H.NotifyIcon)  
[WinUIEx](https://github.com/dotMorten/WinUIEx)  
[moq](https://github.com/moq/moq)  
[Avalonia](https://avaloniaui.net/)  
[FluentAvalonia.BreadcrumbBar](https://github.com/indigo-san/FluentAvalonia.BreadcrumbBar)  
[FluentAvalonia](https://github.com/indigo-san/FluentAvalonia.BreadcrumbBar)  