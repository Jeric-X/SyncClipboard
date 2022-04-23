# SyncClipboard
[![build-master](https://github.com/Jeric-X/SyncClipboard/actions/workflows/build-master.yml/badge.svg)](https://github.com/Jeric-X/SyncClipboard/actions/workflows/build-master.yml)
[![Build status](https://ci.appveyor.com/api/projects/status/4hm1au4xaikj96tr?svg=true)](https://ci.appveyor.com/project/Jeric-X/syncclipboard)

## 功能

- 剪切板同步，使用WebDAV服务器作为中转站，支持文字、图片和文件  
- 执行远程命令，使用WebDAV服务器作为命令中转站，只是个雏形
- 优化图片类型的剪切板，功能大概为：
  - 从任意位置复制图片时，可以直接向文件系统复粘贴图片文件
  - 从文件系统复制图片类的文件时，可以直接向支持图片的文本框粘贴图片
  - 从浏览器复制图片后，后台下载原图到本地，解决直接粘贴时的动态图不动的问题

## Server

理论支持任何支持WebDAV协议的网盘、web服务器  
测试过的服务器：   
- [x] NextCloud  
- [x] 坚果云  

注：
- 坚果云需要开启WebDAV独立密码，并且一定时间内有请求次数限制

## Client-Windows  
下载最新的[Release](https://github.com/Jeric-X/SyncClipboard/releases/)，依赖.NET6

## Client-IOS 
使用[快捷指令](https://apps.apple.com/cn/app/%E5%BF%AB%E6%8D%B7%E6%8C%87%E4%BB%A4/id1462947752)提供的`Get Contents of URL`功能发送HTTP协议  
导入这个[快捷指令](https://www.icloud.com/shortcuts/229cd7657ce544daafc7ece882405b36)

## Client-Android
只用同步文字的话可以使用[HTTP Request Shortcuts](https://play.google.com/store/apps/details?id=ch.rmy.android.http_shortcuts)解决，复杂功能可以用各种脚本自动化工具解决

## 配置

### Windows
- 填写地址：指定服务器同步文件夹的地址（提前在服务器中创建好）
- 填写用户名、密码
- 支持Nextcloud的网页认证
- 部分功能的设定只能在配置文件修改
### IOS
- 修改导入的Workflow
- 填写地址：第一个可输入的URL Action
- 填写用户名：第一个可输入的Text Action
- 填写密码：第二个可输入的Text Action