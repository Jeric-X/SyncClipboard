#!/usr/bin/env python3
"""Build and serve an isolated, real macOS update from 0.0.1 to 0.0.2."""

import argparse
import hashlib
import http.server
import json
import platform
import plistlib
import shlex
import shutil
import socket
import subprocess
import sys
import threading
import time
from datetime import datetime
from pathlib import Path
from urllib.parse import urlsplit


ROOT = Path(__file__).resolve().parents[2]
EXECUTABLE = "SyncClipboard.Desktop.MacOS"


def run(*args, cwd=None):
    print("+", shlex.join(str(arg) for arg in args), flush=True)
    subprocess.run([str(arg) for arg in args], cwd=cwd, check=True)


def replace_once(path, old, new):
    text = path.read_text(encoding="utf-8-sig")
    if text.count(old) != 1:
        raise ValueError(f"源码结构已变化，无法替换 {path}: {old}")
    path.write_text(text.replace(old, new), encoding="utf-8")


def prepare(args):
    if sys.platform != "darwin":
        raise ValueError("准备 macOS 测试包需要在 Mac 上运行")
    for tool in ("dotnet", "ditto", "codesign", "hdiutil"):
        if not shutil.which(tool):
            raise ValueError(f"找不到 {tool}")
    # Fail before building if another server already owns the chosen port.
    with socket.socket() as probe:
        probe.bind(("127.0.0.1", args.port))
    architecture = "arm64" if platform.machine() == "arm64" else "x64"
    rid = f"osx-{architecture}"
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    source = output / "source"
    source.mkdir()
    print(f"创建独立源码副本: {source}", flush=True)
    shutil.copytree(ROOT / "src", source / "src", ignore=shutil.ignore_patterns(
        "bin", "obj", "TestResults", ".DS_Store"))
    shutil.copytree(ROOT / "LICENSES", source / "LICENSES")
    (source / "build/macos").mkdir(parents=True)
    for name in ("Info.plist", "icon.icns"):
        shutil.copy2(ROOT / "build/macos" / name, source / "build/macos" / name)
    shutil.copy2(ROOT / "global.json", source / "global.json")

    url = f"http://127.0.0.1:{args.port}"
    data_name = "SyncClipboardUpdateTest-" + hashlib.sha256(str(output).encode()).hexdigest()[:10]
    env = source / "src/SyncClipboard.Core/Commons/Env.cs"
    replace_once(env, 'public const string SoftName = "SyncClipboard";',
                 f'public const string SoftName = "{data_name}";')
    replace_once(env, 'public const string UpdateApiUrl = "https://api.github.com/repos/Jeric-X/SyncClipboard/releases";',
                 f'public const string UpdateApiUrl = "{url}/releases";')
    replace_once(env, 'public const string UpdateUrl = "https://github.com/Jeric-X/SyncClipboard/releases";',
                 f'public const string UpdateUrl = "{url}/";')
    replace_once(source / "src/SyncClipboard.Core/Models/UserConfigs/ProgramConfig.cs",
                 "public bool CheckUpdateOnStartUp { get; set; } = true;",
                 "public bool CheckUpdateOnStartUp { get; set; } = false;")

    project = source / "src/SyncClipboard.Desktop.MacOS/SyncClipboard.Desktop.MacOS.csproj"
    package_name = f"SyncClipboard_macos_{architecture}.dmg"
    run("dotnet", "restore", project, f"-p:RuntimeIdentifiers={rid}", cwd=source)
    for version, folder in (("0.0.1", "installed"), ("0.0.2", "payload")):
        run("dotnet", "publish", project, "-c", "Debug", f"-p:RuntimeIdentifiers={rid}",
            f"-p:VersionPrefix={version}", "-p:VersionSuffix=", "--self-contained", "true", "--no-restore", cwd=source)
        bundle = output / folder / "SyncClipboard.app"
        bundle.parent.mkdir()
        built = project.parent / f"bin/Debug/net10.0-macos/{rid}/{EXECUTABLE}.app"
        run("ditto", built, bundle)
        with (bundle / "Contents/Info.plist").open("rb") as file:
            info = plistlib.load(file)
        if info["CFBundleShortVersionString"] != version:
            raise ValueError(f"Bundle 版本不匹配: {info}")
        config = {"UpdateInfo": {"manage_type": "manual", "update_src": "github", "package_name": package_name}}
        (bundle / "Contents/MonoBundle/update_info.json").write_text(json.dumps(config), encoding="utf-8")
        run("lipo", bundle / "Contents/MacOS" / EXECUTABLE, "-verify_arch", "arm64" if architecture == "arm64" else "x86_64")
        run("codesign", "--force", "--deep", "--sign", "-", "--timestamp=none", bundle)
        run("codesign", "--verify", "--deep", "--strict", bundle)

    web = output / "web"
    web.mkdir()
    dmg = web / package_name
    run("hdiutil", "create", "-volname", "SyncClipboard Update Test", "-srcfolder", output / "payload",
        "-format", "UDZO", dmg)
    with dmg.open("rb") as file:
        digest = hashlib.file_digest(file, "sha256").hexdigest()
    releases = [{"tag_name": "v0.0.2", "prerelease": False, "html_url": url + "/", "assets": [{
        "name": package_name, "size": dmg.stat().st_size, "digest": "sha256:" + digest,
        "browser_download_url": url + "/" + package_name}]}]
    (web / "releases").write_text(json.dumps(releases), encoding="utf-8")
    (web / "index.html").write_text('<meta charset="utf-8"><h1>SyncClipboard 本地升级演练</h1>'
                                   '<p>0.0.1 → 0.0.2；更新来源为本机 HTTP 服务。</p>', encoding="utf-8")
    manifest = {"port": args.port, "data_name": data_name, "package": package_name,
                "old_version": "0.0.1", "new_version": "0.0.2"}
    (output / "test.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    # Copy the runner so an already prepared fixture remains usable after source changes.
    runner = output / "TestUpdate.py"
    shutil.copy2(Path(__file__), runner)
    command = output / "开始升级演练.command"
    command.write_text("#!/bin/sh\nexec " + shlex.join([
        sys.executable, str(runner), "serve", str(output), "--launch"]) + "\n", encoding="utf-8")
    command.chmod(0o755)
    readme = f"""本地升级演练：0.0.1 → 0.0.2

双击 开始升级演练.command，保持此 HTTP 服务窗口打开。
测试版启动后，在“关于”点击“检查更新”，然后“下载更新”和“安装更新”。
也可以先复制一段测试文字，升级后检查历史记录是否还在。
更新辅助进程会打开另一个 Terminal 窗口；更新后确认版本为 0.0.2。
下载完成提示应为“新版本已下载”；安装成功后仅安装窗口自动关闭，失败时保留。

测试安装位置：{output / 'installed/SyncClipboard.app'}
本地源：{url}/releases
独立数据目录名称：{data_name}
仅源码副本修改了 SoftName、更新地址和启动检查默认值，安装逻辑原样运行。
使用本机临时签名，首次启动可能需要重新授予系统权限。
原版 App 和测试版共用 macOS bundle ID，建议手动退出原版再演练。
不要把测试版登录启动项或同步服务配置成正式使用环境。

调试器可附加到测试进程，也可直接启动 installed/SyncClipboard.app/Contents/MacOS/{EXECUTABLE}。
构建使用 Debug，源码和符号来自 {source}。
辅助脚本在另一个进程里运行，主程序退出后调试会话结束是预期行为。
升级成功的任务目录会按现有逻辑清理，需要查看日志请在过程中复制。

重试：退出测试版，运行：
{shlex.join([sys.executable, str(ROOT / 'build/macos/TestUpdate.py'), 'prepare'])}
这会生成一套新的独立目录，不覆盖这次的现场。
停止本地 HTTP 服务：在第一个 Terminal 窗口按 Ctrl+C。
"""
    (output / "说明.txt").write_text(readme, encoding="utf-8")
    print(f"\n准备完成。双击启动：{command}\n{readme}", flush=True)


def serve(args):
    output = args.directory.resolve()
    config = json.loads((output / "test.json").read_text())
    web = output / "web"
    package = config["package"]

    class Handler(http.server.BaseHTTPRequestHandler):
        def do_GET(self):
            name = {"/": "index.html", "/releases": "releases", "/" + package: package}.get(urlsplit(self.path).path)
            if name is None:
                self.send_error(404)
                return
            file = web / name
            self.send_response(200)
            self.send_header("Content-Length", str(file.stat().st_size))
            self.send_header("Content-Type", "application/json" if name == "releases" else
                             "text/html; charset=utf-8" if name == "index.html" else "application/octet-stream")
            self.end_headers()
            try:
                with file.open("rb") as stream:
                    while block := stream.read(256 * 1024):
                        self.wfile.write(block)
                        if name == package and args.mbps > 0:
                            time.sleep(len(block) / (args.mbps * 1024 * 1024))
            except (BrokenPipeError, ConnectionResetError):
                pass  # Canceling the app's download is an expected test action.

    server = http.server.ThreadingHTTPServer(("127.0.0.1", config["port"]), Handler)
    print(f"本地更新源：http://127.0.0.1:{config['port']}/releases", flush=True)
    print(f"下载限速：{args.mbps} MiB/s；保持此窗口打开，Ctrl+C 停止。", flush=True)
    if args.launch:
        threading.Thread(target=server.serve_forever, daemon=True).start()
        try:
            run("open", "-n", output / "installed/SyncClipboard.app")
            while True:
                time.sleep(1)
        except KeyboardInterrupt:
            pass
        finally:
            server.shutdown()
            server.server_close()
    else:
        try:
            server.serve_forever()
        except KeyboardInterrupt:
            pass
        finally:
            server.server_close()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    build = commands.add_parser("prepare", help="从当前源码构建独立测试包")
    build.add_argument("--output", type=Path, default=ROOT / "build/output" / ("update-test-" + datetime.now().strftime("%Y%m%d-%H%M%S")))
    build.add_argument("--port", type=int, default=18765)
    start = commands.add_parser("serve", help="提供本地更新 API 和 DMG")
    start.add_argument("directory", type=Path)
    start.add_argument("--launch", action="store_true", help="启动测试目录中的 App")
    start.add_argument("--mbps", type=float, default=8, help="下载限速 MiB/s；0 表示不限速")
    args = parser.parse_args()
    try:
        (prepare if args.command == "prepare" else serve)(args)
    except (OSError, ValueError, subprocess.CalledProcessError) as error:
        parser.exit(1, f"错误：{error}\n")


if __name__ == "__main__":
    main()
