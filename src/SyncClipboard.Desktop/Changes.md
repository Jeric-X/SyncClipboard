2.9.0
- 更新版本号与Windows版相同

0.8.0
- 修复：SyncClipboard无法识别的剪贴板被强制覆盖（https://github.com/Jeric-X/SyncClipboard/issues/181）
- 修复：在部分Linux环境中复制非英文同步到其他设备后变为乱码（https://github.com/Jeric-X/SyncClipboard/issues/172）
- 修复：在部分gnome环境下的文件管理器中无法粘贴文件
- 功能：从密码管理软件复制的内容不会被同步（仅macOS）（https://github.com/Jeric-X/SyncClipboard/issues/157）  
:::info  
下版本Linux/macOS将使用[主仓库](https://github.com/Jeric-X/SyncClipboard/)发布，与Windows版本合并版本号  
如果不是紧急问题修复，下版本将更新.net运行时到8.0  
:::

0.7.8
- 功能：支持自定义临时文件的保存时长
- 功能：server端增加监听ipv6地址

0.7.7
- 功能：增加快捷键：手动上传（忽略内容控制）(https://github.com/Jeric-X/SyncClipboard/issues/122, https://github.com/Jeric-X/SyncClipboard/issues/115)
- 修复：在office或wps表格中复制内容后剪贴板失去格式信息(https://github.com/Jeric-X/SyncClipboard/issues/130)
- 修复：重试次数设为0时上传失败(https://github.com/Jeric-X/SyncClipboard/issues/117)

0.7.6
- 修复：使用aliyundrive-webdav及其衍生工具作为服务器时无法上传(https://github.com/Jeric-X/SyncClipboard/issues/98)
- 功能：添加多种同步内容控制开关（上传/下载/文本/文件/文件夹/文件类型）(https://github.com/Jeric-X/SyncClipboard/issues/90 , https://github.com/Jeric-X/SyncClipboard/issues/111)

0.7.5
- 功能：可以设置重试次数，下载失败超过重试次数自动下载会暂停，触发一次上传后会恢复自动下载(https://github.com/Jeric-X/SyncClipboard/issues/100)
- 功能：可以设置剪切文件时不上传(Linux)(https://github.com/Jeric-X/SyncClipboard/issues/107)
- 修复：快捷键不允许设置为ctrl + c或command + c (https://github.com/Jeric-X/SyncClipboard/issues/103)

0.7.4
- 修复：不设置hash上传图片/文件到服务器后，桌面客户端会无限重复设置剪贴板
- 修复：在一些场景复制图片时，无法触发上传(https://github.com/Jeric-X/SyncClipboard/issues/88)
- 修复：远程文件不存在时，只报错一次，不再图标错乱(https://github.com/Jeric-X/SyncClipboard/issues/87)
- 为修复 https://github.com/Jeric-X/SyncClipboard/issues/86，Linux无法自动识别语言，默认语言为英语
- 修复：短时间内多次复制，新复制的内容被之前复制的内容覆盖(https://github.com/Jeric-X/SyncClipboard/issues/91)
- 功能：手动上传后可以发送通知(https://github.com/Jeric-X/SyncClipboard/issues/82)

0.7.3
- 修复：macOS Menu Bar图标在同步关闭状态下被裁切、颜色错误

0.7.2
- 修复：服务端产生大量图片文件
- 修复：macOS Menu Bar图标被裁切(https://github.com/Jeric-X/SyncClipboard/issues/78)
- 变更：轮询间隔设置为0时在内部限制为0.5秒
- 功能：支持更新到预览版本

0.7.1
- 修复：macOS menu bar图标无法自动自适应系统主题(https://github.com/Jeric-X/SyncClipboard/issues/73)
- 修复：无法自动删除本地临时文件

0.7.0
- 功能：支持同步多个文件、文件夹，移动端体现为zip压缩文件，安卓端`HTTP Shortcuts`配置也需要更新以支持此功能
- 修复：最大上传文件大小设置项在混合模式无法设置(https://github.com/Jeric-X/SyncClipboard/issues/68)

0.6.4
- 功能：增加复制并上传/下载并粘贴快捷键

0.6.3
- 适配macOS风格的系统菜单栏图标(https://github.com/Jeric-X/SyncClipboard/issues/48)
- 关闭前台窗口后，dock栏图标不再显示为活跃状态（macOS）(https://github.com/Jeric-X/SyncClipboard/issues/49)

0.6.2
- 修复：复制不支持类型剪贴板会被远程剪贴板覆盖，导致无法复制

0.6.1
- 新增：按需单次上传/下载快捷键
- 修复: dock栏图标重复(Linux)
- 增强稳定性

0.6.0
- 新增：本机同步关闭时状态栏图标显示为灰色
- 新增：快捷键系统（不支持Linux Wayland）

0.5.2
- 新增：支持信任不安全的HTTPS证书(https://github.com/Jeric-X/SyncClipboard/issues/37)

0.5.1
- “剪切板”改为“剪贴板”
- 修复：转换格式功能异常

0.5.0
- 功能：添加了关闭窗口、最小化的快捷键（macOS）
- 功能：增加了系统通知功能，用于剪切板更新提示（在剪切板同步设置页面关闭）、新版本更新提示等
- 增加WebDAV服务器兼容性，新测试通过[InfiniCLOUD](https://infini-cloud.net/en/)([#33](https://github.com/Jeric-X/SyncClipboard/issues/33))

0.4.2
- 修复：mac中微信无法粘贴图片(https://github.com/Jeric-X/SyncClipboard/issues/31)

0.4.1
- 修复：文件格式问题引发的错误

0.4.0
- 系统操作：复制数据文件夹路径（Linux）
- 系统操作：在Nautilus中打开数据文件夹（Linux）
- 新菜单项：打开配置文件所在文件夹（macOS）
- 使用TextEdit打开配置文件（macOS）
- 不兼容变更：macOS配置文件改为储存在~/Library/Application Support/SyncClipboard/，建议老用户移动~/.config/SyncClipboard/中的内容到新目录

0.3.0
- 功能：深色模式切换开关
- 功能：添加了一个诊断页面
- 修复：存在开关的设置条目高度异常

0.2.0
- 功能：图片/文件同步支持
- 功能：客户端混合模式
- 修复：客户端设置的一处文字错误
- 修复：mac端服务器启动失败

0.1.3
- 修复：macos: 运行在后台时，dock栏图标无法唤起主界面

0.1.2
- Linux support
- macOS support

0.1.1
- Linux support