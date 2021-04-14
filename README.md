# SyncClipboard
[![Build status](https://ci.appveyor.com/api/projects/status/4hm1au4xaikj96tr?svg=true)](https://ci.appveyor.com/project/Jeric-X/syncclipboard)

## 功能
一个简单的剪切板同步工具，使用网盘作为中转站，支持文字、图片和文件  
windows使用c#写了个客户端，IOS只能曲线救国使用APP`快捷指令`手动收发

## Server

理论支持任何支持WebDAV协议的网盘、web服务器  
测试过的服务器：   
- [x] NextCloud  
- [x] 坚果云  

注：
- 坚果云需要开启WebDAV独立密码，并且一定时间内有请求次数限制

## Client-Windows  
下载最新的[Release](https://github.com/Jeric-X/SyncClipboard/releases/)，或者下载源码，VS2019及以上编译

## Client-IOS 
使用[快捷指令](https://apps.apple.com/cn/app/%E5%BF%AB%E6%8D%B7%E6%8C%87%E4%BB%A4/id1462947752)提供的`Get Contents of URL`功能发送HTTP协议  
导入这个[Workflow](https://www.icloud.com/shortcuts/229cd7657ce544daafc7ece882405b36)

## 配置

### Windows
- 填写地址：指定服务器同步文件夹的地址（提前在服务器中创建好）
- 填写用户名、密码

### IOS
- 修改导入的Workflow
- 填写地址：第一个可输入的URL Action
- 填写用户名：第一个可输入的Text Action
- 填写密码：第二个可输入的Text Action