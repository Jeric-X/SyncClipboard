0.6.2.1
- 适配macOS风格的菜单栏图标

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