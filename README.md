# SyncClipboard
[![Build status](https://ci.appveyor.com/api/projects/status/4hm1au4xaikj96tr?svg=true)](https://ci.appveyor.com/project/Jeric-X/syncclipboard)

## 功能
一个简单的剪切板同步工具，使用网盘作为中转站，支持文字和图片  
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
使用[Workflow](https://appsto.re/cn/2IzJ2.i)提供的`Get Contents of URL`功能发送HTTP协议  
导入这个[Workflow](https://www.icloud.com/shortcuts/229cd7657ce544daafc7ece882405b36)

## 配置

### 使用自定义服务器
#### Windows
- 填写地址：指定服务器同步文件夹的地址（提前在服务器中创建好）
- 填写用户名、密码

#### IOS
- 修改导入的Workflow
- 填写地址：第一个可输入的URL Action
- 填写用户名：第一个可输入的Text Action
- 填写密码：第二个可输入的Text Action

### 使用内置服务器 
#### Windows
- 设置尽可能无法被其他人重复使用的用户名
- 将设置中出现的基于用户名的地址填入其他客户端  
- 注意：内置服务器中储存的剪切板内容可以被任何人读取

#### IOS
- 修改导入的Workflow，将上一步得到的地址填入第一个可输入的URL Action中