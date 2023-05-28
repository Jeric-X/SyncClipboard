v1.6.0 (working)
- 功能：自动删除本地临时文件和log文件（在配置文件里可以修改）
- Todo:
  - 功能：自动删除服务器临时文件

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
