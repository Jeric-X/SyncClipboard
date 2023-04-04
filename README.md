# SyncClipboard
[![build](https://github.com/Jeric-X/SyncClipboard/actions/workflows/build.yml/badge.svg)](https://github.com/Jeric-X/SyncClipboard/actions/workflows/build.yml)
[![Build status](https://ci.appveyor.com/api/projects/status/4hm1au4xaikj96tr?svg=true)](https://ci.appveyor.com/project/Jeric-X/syncclipboard)

## 功能

- 剪切板同步，使用WebDAV服务器作为中转站，支持文字、图片和文件  
- 执行远程命令，使用WebDAV服务器作为命令中转站，只是个雏形，目前只能远程关机，使用这个[快捷指令](https://www.icloud.com/shortcuts/d3d807fb00774fe8ac10cd4a82260456)触发
- 优化图片类型的剪切板，功能大概为：
  - 从任意位置复制图片时，可以直接向文件系统复粘贴图片文件
  - 从文件系统复制图片类的文件时，可以直接向支持图片的文本框粘贴图片
  - 从浏览器复制图片后，后台下载原图到本地，解决直接粘贴时的动态图不动的问题（大多网站有认证，适用范围有限，支持bilibili动态图片）
  - 转换webp/heic等较新类型的图片到gif/jpg

## Server

理论支持任何支持WebDAV协议的网盘、web服务器  
测试过的服务器：   
- [x] NextCloud  
- [x] 坚果云  

注：
- 坚果云需要开启WebDAV独立密码，并且一定时间内有请求次数限制

## Client-Windows  
下载最新的[Release](https://github.com/Jeric-X/SyncClipboard/releases/)，依赖.NET6，Windows 10 17763。后台运行，自动同步

## Client-IOS 
### 使用[快捷指令](https://apps.apple.com/cn/app/%E5%BF%AB%E6%8D%B7%E6%8C%87%E4%BB%A4/id1462947752)  

导入这个[快捷指令](https://www.icloud.com/shortcuts/9e2f44bd12a84935b715aac9b488f6ee)。从组件栏和分享菜单中使用

### 使用[JSBox](https://apps.apple.com/cn/app/jsbox-%E5%AD%A6%E4%B9%A0%E5%86%99%E4%BB%A3%E7%A0%81/id1312014438)
导入这个[js文件](/script/Clipboard.js)，修改`user`，`token`，`path`字段。作为键盘扩展处理文字时使用，不支持文件

## Client-Android
### 使用[HTTP Request Shortcuts](https://play.google.com/store/apps/details?id=ch.rmy.android.http_shortcuts)
导入这个[配置文件](/script/shortcuts.zip)，修改`变量`中的`UserName`，`UserToken`，`url`。`HTTP Request Shortcuts`支持从下拉菜单、桌面组件、桌面图标、分享菜单中使用

## 配置

全平台依赖三条必要配置，windows端可以在配置文件中修改更多配置
- user
- token
- url，具体到文件夹作为工作目录，需提前创建好。未使用过windows客户端时需在工作目录中再创建`file`文件夹以同步文件，windows客户端在首次同步文件时会自动创建`file`文件夹

## 项目依赖
[Magick.NET](https://github.com/dlemstra/Magick.NET)
