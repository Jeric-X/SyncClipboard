# SyncClipboard
## 功能
一个简单的剪切板同步工具，C/S架构
## Server
理论支持任何支持WebDAV协议的网盘、web服务器   
> 测试过的服务器：   
> - [x] NextCloud  

## Client-Windows  
下载最新的[Release](https://github.com/Jeric-X/SyncClipboard/releases/)
## Client-IOS 
使用[Workflow](https://appsto.re/cn/2IzJ2.i)提供的`Get Contents of URL`功能发送HTTP协议  
导入这个[Workflow](https://workflow.is/workflows/6da4c1de8b1446cda56e336b1ed50b25)
## 配置
### 使用内置服务器  
#### Windows
- 设置尽可能无法被其他人重复使用的用户名
- 将设置中出现的基于用户名的地址填入其他客户端  
- 注意：内置服务器中储存的剪切板内容可以被任何人读取

#### IOS
- 修改导入的Workflow，将上一步得到的地址填入第一个可输入的URL Action中

### 使用自定义服务器
#### Windows
- 填写地址：指定服务器同步文件的地址，SyncClipboard使用一个json文件储存剪切板
- 填写用户名、密码

#### IOS
- 修改导入的Workflow
- 填写地址：第一个可输入的URL Action
- 填写用户名：第一个可输入的Text Action
- 填写密码：第二个可输入的Text Action