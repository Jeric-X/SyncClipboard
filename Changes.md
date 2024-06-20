v2.8.3
- 修复：不设置hash上传图片/文件到服务器后，桌面客户端会无限重复设置剪贴板
- 修复：远程文件不存在时，只报错一次，不再无限弹窗、图标错乱(#87)
- 修复：短时间内多次复制，新复制的内容被之前复制的内容覆盖(#91)
- 功能：手动上传后可以发送通知(#82)

v2.8.2
- 修复：服务端产生大量图片文件
- 变更：轮询间隔设置为0时在内部限制为0.5秒
- 功能：支持更新到预览版本

v2.8.1
- 修复：无法自动删除本地临时文件
- 功能：同步的文字中间含有URL也可以使用浏览器打开
- 功能：自动检测图标缺失并提示用户下载

v2.8.0
- 功能：支持同步多个文件、文件夹，移动端体现为zip压缩文件，安卓端`HTTP Shortcuts`配置也需要更新以支持此功能
- 修复：最大上传文件大小设置项在混合模式无法设置(#68)
- 修复：Windows端无法下载`.lnk`、`.url`、`.wsh`类型的文件

v2.7.5
- 功能：增加复制并上传/下载并粘贴快捷键

v2.7.4
- 修复：启动时崩溃(#46)
- 修复：Cad软件相关问题(#50)

v2.7.3
- 修复：复制excel表格数据被识别成图片(#43)
- 修复：IOS自动同步无法识别备忘录中的文字

v2.7.2
- 修复：复制不支持类型剪贴板会被远程剪贴板覆盖，导致无法复制

v2.7.1
- 新增：按需单次上传/下载快捷键
- 增强稳定性

v2.7.0
- 新增：本机同步关闭时状态栏图标显示为灰色
- 新增：快捷键系统

v2.6.3
- 新增：支持信任不安全的HTTPS证书(#37)

v2.6.2
- “剪切板”改为“剪贴板”
- 修复：转换格式功能异常

v2.6.1
- 增加WebDAV服务器兼容性，新测试通过[InfiniCLOUD](https://infini-cloud.net/en/)(#33)
- 其他问题修复

v2.6.0
- 修复：文件格式问题引发的错误(#25)
- 修复：设置内置服务器时，用于输入账号的文本框会显示密码(#26)
- 新增：提供了服务端的docker镜像(#27)

v2.5.1
- 功能：转换格式时增加进度提示
- 功能：下载网页原图时增加了一个取消按钮
- 修复：开启图片轻松拷贝时复制图片异常发送通知
- 修复：托盘菜单颜色无法跟随主题

v2.5.0
- 功能：深色模式

v2.4.2
- 修复：混合模式服务器hash信息缺失
- 修复：服务器删除正在被占用文件相关错误
- 修复：服务器关，混合模式开，无法开启同步
- 修复：下载网页原图获取到错误的文件名

v2.4.1
- 功能：启动时若存在已有的实例时，打开已有实例的窗口
- 功能：新设置项：启动时是否隐藏窗口
- 移除：Winform版
- 新增：Linux, macOS支持

v2.3.0
- 窗口大小适配系统缩放比例
- 修复：设置本地剪切板偶发性出错
- 支持多语言

v2.2.0
- 修复：移动端无法上传图片/文件(#15)
- Nextcloud登录页面添加刷新按钮
- 开启客户端混合模式时，不起作用的选项设置为不可操作
- 安卓端脚本
  - 适配HTTP Request Shortcuts新版本(#16)
  - 修复：文件名未使用url编码引起的问题

v2.1.0
- 更改临时文件储存位置到系统appdata目录
- 开启本地服务器时支持客户端混合模式
- WinUI:
	- 支持托盘图标tooltip
	- 设置任务栏和任务管理器图标
	- 关闭右键菜单动画

v2.0.1
- WinUI:
	- 基于WinUI3的全新界面
- Winform:
	- 修复：设置窗口icon变形
- 修复：下载小于4长度的字符串无法通知

v1.7.0
- 功能：server可以全平台独立运行

v1.6.0
- 功能：自动删除本地临时文件和log文件（在配置文件里可以修改）
- 功能：每次上传剪切板自动删除服务器曾经存储的临时文件（在配置文件里可以修改）
- 修复：html类型剪切版图片的正则表达式

v1.5.0
- 功能：新增内置服务器
- 修复：复制单个视频文件导致剪切板异常与cpu升高
- 功能：图片兼容性优化支持avif

v1.4.2
- 修复：从excel/ppt复制文字会变成图片(#9)
- 修复：剪切图片类文件会失去剪切语义，变成复制(#10)

v1.4.1
- 修复：webp转gif后动画撕裂

v1.4.0
- 修复：gif到bitmap错误转换导致的问题
- 功能：增加`图片兼容性优化`，复制heic/webp等较新格式图片时自动转换为jpg/gif

v1.3.11
- 修复：文件名过长引起的异常
- 修复：初次使用时的404问题
- 功能：md5为空时不检查，适配无法计算md5的平台

v1.3.10
- 修复bug: 无法一次复制多个文件（EasyCopyImageSerivce引起）
- 增加重新复制按钮
- 修复bug: 复制超大文本引起cpu爆高
- 修复bug: 文件过大不上传，引起的404

v1.3.9
- 修复：commandService失效
- 增加了执行命令的倒计时toast通知
- 使用HttpClient替换了过时的WebRequest
- 添加了一批右键菜单
- 修复：无法读取正在被使用的文件的md5引起的各种问题
- 修复：计算md5引起的主界面卡顿
- 添加了通知的图片预览、各种按钮
- 添加下载文件的进度条
- EasyCopyImage现在可以配置代理

v1.3.8
- 配置文件现在是格式化过的了
- 修复：点击下载完成的toast无法打开文件所在文件夹，只是打开我的电脑

v1.3.7
- upgrade to .net6
todo:
- 功能测试

v1.3.6
- Fix: Uploading blocks UI thread.

v1.3.5
- Add EasyCopyImageSerivce for optimizing image clipboard.
- Add max size of file in config.

v1.3.4
- Fix a bug when local clipboard is nothing.

v1.3.3
- Add cookie support.
- Add CommandService.
- Optimize Log system.

v1.3.2
- fix bugs when clicking COPY button multi times in a short period of time.

v1.3.1
- fix bug: `config won't effect until next startup`

v1.3.0
- Add independent config file.
- Remove internal server.
- Add nextcloud official login flow.

v1.2.3
- Fix bug with bmp file.
- Add clicking event when file is downloaded. 

v1.2.2
- Add local clipboard type "bitmap".
- Fix auto start with boot.

v1.2.1
- Add icon animation.

v1.2.0
- Add image support. Image will be set in more formats(html, QQ_Unicode_RichEdit_Format, bitmap, filedrop).

v1.1.9
- Now we can sync files
- Optimize log

v1.1.8
- add log file
- fix bug of checking update 

v1.1.7
- fix null reference exception

v1.1.6
- 整理目录
- trying to fix #5

v1.1.5
- 修复线程异常退出导致的Mutex失效Exception

v1.1.4
- 只支持单实例
- 修复Clipboard线程不安全造成的异常

v1.1.3
- add mutex when writing/reading remote files
- optimize architecture

v1.1.2
- change profile file's syntax
- update ios workflow

v1.1.1
- fix bug

v1.1.0
- support copy image (now upload only)

v1.0.6
- Support tls1.2
- Fix built-in server

v1.0.5
- Fix start with boot

v1.0.4
- change contextmenu style
- othor optimization 

v1.0.3
- Add check update

v1.0.2
- Add deployment
- Fix bugs

v1.0.1
- 发布
