# SyncClipboard
[![build](https://github.com/Jeric-X/SyncClipboard/actions/workflows/build.yml/badge.svg?branch=WinUI3)](https://github.com/Jeric-X/SyncClipboard/actions?query=branch%3AWinUI3)
[![Build status](https://ci.appveyor.com/api/projects/status/4hm1au4xaikj96tr/branch/WinUI3?svg=true)](https://ci.appveyor.com/project/Jeric-X/syncclipboard/branch/WinUI3)

- [SyncClipboard](#syncclipboard)
  - [功能](#功能)
  - [服务器](#服务器)
    - [独立服务器](#独立服务器)
    - [Windows客户端内置服务器](#windows客户端内置服务器)
    - [其他WebDAV服务器](#其他webdav服务器)
  - [客户端](#客户端)
    - [Windows](#windows)
      - [WinUI版](#winui版)
      - [Winform版](#winform版)
    - [IOS](#ios)
      - [使用快捷指令](#使用快捷指令)
      - [使用JSBox](#使用jsbox)
    - [Android](#android)
      - [使用HTTP Request Shortcuts](#使用http-request-shortcuts)
    - [客户端配置](#客户端配置)
  - [项目依赖](#项目依赖)


## 功能

- 剪切板同步，使用WebDAV服务器作为中转站，支持文字、图片和文件  
- 优化图片类型的剪切板，功能有：
  - 从任意位置复制图片时，可以直接向文件系统粘贴图片文件
  - 从文件系统复制图片类的文件时，可以直接向支持图片的文本框粘贴图片
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
- http使用明文传输，在公网部署考虑使用反向代理工具配置SSL
- 内置服务器并不是WebDAV实现

### Windows客户端内置服务器
[Windows客户端](#Windows)自带服务器实现，可以作为本机和其他客户端的服务器。  
在配置文件`SyncClipboard.json`中的`ServerService`部分修改`端口号`，`用户名`，`密码`，服务器注意事项同上

### 其他WebDAV服务器
可以使用支持WebDAV协议的网盘作为服务器  
测试过的服务器：   
- [x] NextCloud  
- [x] 坚果云  
- [x] [AList](https://alist.nn.ci/)

注：
- 坚果云需要开启WebDAV独立密码，并且一定时间内有请求次数限制

## 客户端
### Windows   
分为WinUI版和Winform版，可以自行选择下载  
客户端依赖：   
- [.NET 6.0桌面运行时](https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-desktop-6.0.16-windows-x64-installer)，未安装会弹窗提醒并跳转到微软官方下载页面  
- [ASP.NET Core 6.0运行时](https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-aspnetcore-6.0.16-windows-x64-installer)，未安装会弹窗提醒并跳转到微软官方下载页面  
- Windows10 1809及以上
#### WinUI版

- 下载地址：[Release](https://github.com/Jeric-X/SyncClipboard/releases/)页面中的`SyncClipboard.WinUI3.zip`，解压缩后运行`SyncClipboard.exe`  
- 额外运行依赖：
  - Windows10 2004及以上  
  - 微软[Segoe Fluent Icons](https://learn.microsoft.com/zh-cn/windows/apps/design/style/segoe-fluent-icons-font)图标字体，Windows11自带无需安装，Windows10需要手动下载安装（[官方地址](https://aka.ms/SegoeFluentIcons)），否则界面图标会大范围出错

<details>
<summary>展开/折叠截图</summary>

![](assets/WinUI.png)

</details>

#### Winform版
- 下载地址：[Release](https://github.com/Jeric-X/SyncClipboard/releases/)页面中的`SyncClipboard.exe`，独立文件直接运行
- 界面上的可设置项较少，可以通过手动修改配置文件设置功能

<details>
<summary>展开/折叠截图</summary>

![](assets/Winform.png)

</details>

### IOS 
#### 使用[快捷指令](https://apps.apple.com/cn/app/%E5%BF%AB%E6%8D%B7%E6%8C%87%E4%BB%A4/id1462947752)  

导入这个[快捷指令](https://www.icloud.com/shortcuts/9e2f44bd12a84935b715aac9b488f6ee)。从组件栏和分享菜单中使用

#### 使用[JSBox](https://apps.apple.com/cn/app/jsbox-%E5%AD%A6%E4%B9%A0%E5%86%99%E4%BB%A3%E7%A0%81/id1312014438)
导入这个[js文件](/script/Clipboard.js)，修改`user`，`token`，`path`字段。作为键盘扩展处理文字时使用，不支持文件

### Android
#### 使用[HTTP Request Shortcuts](https://play.google.com/store/apps/details?id=ch.rmy.android.http_shortcuts)
导入这个[配置文件](/script/shortcuts.zip)，修改`变量`中的`UserName`，`UserToken`，`url`。`HTTP Request Shortcuts`支持从下拉菜单、桌面组件、桌面图标、分享菜单中使用

### 客户端配置

全平台依赖三条必要配置（配置的拼写可能会有所不同，含义相同）。windows端可以在配置文件中修改更多配置
- user
- password
- url，格式为http(s)://ip(或者域名):port。使用其他WebDav服务器时，url需要具体到一个已存在的文件夹作为工作目录，不使用windows客户端时需在工作目录中再创建`file`文件夹以同步文件，windows客户端在首次同步文件时会自动创建`file`文件夹

## 项目依赖
[Magick.NET](https://github.com/dlemstra/Magick.NET)  
[Windows Community Toolkit Labs](https://github.com/CommunityToolkit/Labs-Windows)  
[.NET Community Toolkit](https://github.com/CommunityToolkit/dotnet)  
[H.NotifyIcon](https://github.com/HavenDV/H.NotifyIcon)  
[WinUIEx](https://github.com/dotMorten/WinUIEx)  
[moq](https://github.com/moq/moq)