# Linux 输入设备权限（ACL）

中文 | [English](Linux-Input-Permissions_EN.md)

## 什么时候需要权限

是否需要设备权限，取决于当前 Linux 桌面会话使用 X11 还是 Wayland，以及「系统设置 → 通用 → 全局快捷键后端」的选择。更改后端后需要重启 SyncClipboard 才会生效。可运行 `echo "$XDG_SESSION_TYPE"` 查看当前会话类型，通常为 `x11` 或 `wayland`。

| 桌面会话 | 全局快捷键后端选项 | 实际使用方式 | 是否需要设备权限 |
| --- | --- | --- | --- |
| X11 | 自动（默认） | XRecord/XTest | 不需要 |
| X11 | x11 | XRecord/XTest | 不需要 |
| X11 | libinput/uinput | libinput/uinput | 需要 |
| Wayland | 自动（默认） | libinput/uinput | 需要 |
| Wayland | x11 | 通过 XWayland 使用 XRecord/XTest | 不需要，但功能可能不生效 |
| Wayland | libinput/uinput | libinput/uinput | 需要 |

需要设备权限的组合中，监听全局快捷键需要 `/dev/input/event*` 的读写权限，使用SyncClipboard间接触发的复制、粘贴(本质为模拟按键输入)只需要 `/dev/uinput` 的写权限。XRecord 后端无需配置下述设备 ACL，应用也会隐藏输入设备权限管理模块。

## 配置 ACL

对于使用 udev 并支持本地活动会话 ACL 的桌面系统，可通过 `uaccess` 为当前活动用户自动设置 ACL，无需加入 `input` 组。

`udevadm` 并非所有 Linux 系统都有，可用 `command -v udevadm` 检查。精简系统或使用其他设备管理器的系统可能没有；即使有该命令，`uaccess` 也需要相应规则和会话管理支持。

创建规则，文件名前缀使用 `70`，确保先于系统的 `73-seat-late.rules` 执行：

```shell
sudo tee /etc/udev/rules.d/70-syncclipboard.rules >/dev/null <<'EOF'
ACTION=="remove", GOTO="syncclipboard_end"
SUBSYSTEM=="input", KERNEL=="event*", TAG+="uaccess"
SUBSYSTEM=="misc", KERNEL=="uinput", TAG+="uaccess"
LABEL="syncclipboard_end"
EOF
```

若只需要模拟按键，可仅保留 `uinput` 那条设备规则。若 `/dev/uinput` 不存在，先执行 `sudo modprobe uinput`。随后重新加载规则：

```shell
sudo udevadm control --reload-rules
sudo udevadm trigger
sudo udevadm settle
```

用 `getfacl /dev/uinput` 和 `getfacl /dev/input/event0` 查看 ACL，应包含当前用户的 `rw-` 条目。`uaccess` 授予的读写权限覆盖上述需求。重启 SyncClipboard，并在「系统设置」中查看权限状态。ACL 授权作用于该用户的程序，并非只授权 SyncClipboard。

临时测试也可直接使用 [setfacl](https://man7.org/linux/man-pages/man1/setfacl.1.html)，不依赖 udev。在运行 SyncClipboard 的普通用户终端执行；只需要模拟按键时仅执行第二条：

```shell
sudo setfacl -m "u:$(id -un):rw" /dev/input/event*
sudo setfacl -m "u:$(id -un):w" /dev/uinput
```

设备重建或会话切换后，手动设置的 ACL 可能丢失。若缺少 `getfacl`、`setfacl`，请安装发行版的 `acl` 包（Ubuntu/Debian：`sudo apt install acl`）。
