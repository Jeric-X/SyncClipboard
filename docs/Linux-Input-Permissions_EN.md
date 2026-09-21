# Linux Input Device Permissions (ACL)

[中文](Linux-Input-Permissions.md) | English

## When permissions are required

Device permissions depend on the Linux desktop session (X11 or Wayland) and the selection under **System settings → General → Global hotkey backend**. Restart SyncClipboard after changing the backend. Run `echo "$XDG_SESSION_TYPE"` to check the session type, usually `x11` or `wayland`.

| Desktop session | Global hotkey backend option | Input mechanism | Device permissions required? |
| --- | --- | --- | --- |
| X11 | Automatic (default) | XRecord/XTest | No |
| X11 | x11 | XRecord/XTest | No |
| X11 | libinput/uinput | libinput/uinput | Yes |
| Wayland | Automatic (default) | libinput/uinput | Yes |
| Wayland | x11 | XRecord/XTest through XWayland | No, but functionality may not work |
| Wayland | libinput/uinput | libinput/uinput | Yes |

For combinations requiring device permissions, global hotkeys need read/write access to `/dev/input/event*`; copy/paste operations indirectly triggered through SyncClipboard (implemented by simulating keyboard input) only need write access to `/dev/uinput`. The XRecord backend needs none of the device ACLs below, and the app hides the input device permission management section for it.

## Configure ACLs

On desktops with udev and active local session ACL support, use `uaccess` to automatically grant access to the active user, without joining the `input` group.

Not every Linux system provides `udevadm`; check with `command -v udevadm`. Minimal systems or alternative device managers may omit it. Having the command alone does not guarantee `uaccess` support: the corresponding rules and session management must also be available.

Create a rule with prefix `70` so it runs before `73-seat-late.rules`:

```shell
sudo tee /etc/udev/rules.d/70-syncclipboard.rules >/dev/null <<'EOF'
ACTION=="remove", GOTO="syncclipboard_end"
SUBSYSTEM=="input", KERNEL=="event*", TAG+="uaccess"
SUBSYSTEM=="misc", KERNEL=="uinput", TAG+="uaccess"
LABEL="syncclipboard_end"
EOF
```

For simulated input only, keep just the `uinput` device rule. If `/dev/uinput` is missing, run `sudo modprobe uinput` first. Reload the rules:

```shell
sudo udevadm control --reload-rules
sudo udevadm trigger
sudo udevadm settle
```

Check `getfacl /dev/uinput` and `getfacl /dev/input/event0` for a named user entry with `rw-` access. Restart SyncClipboard and refresh its permission status in System settings.

> [!WARNING]
> ACL permissions apply to the current user, not exclusively to SyncClipboard.

For temporary testing, use [setfacl](https://man7.org/linux/man-pages/man1/setfacl.1.html) without udev. Run these from the regular user's terminal; only the second command is needed for simulated input:

```shell
sudo setfacl -m "u:$(id -un):rw" /dev/input/event*
sudo setfacl -m "u:$(id -un):w" /dev/uinput
```

Manual ACLs may be lost when devices are recreated or sessions change. If `getfacl` or `setfacl` is missing, install your distribution's `acl` package (`sudo apt install acl` on Ubuntu/Debian).

## Revoke access

Fully exit SyncClipboard first, then run the following from the regular user's terminal used to run the app.

If you configured the udev rule above, remove it and reload the rules. Skip this step if you only granted access manually with `setfacl`:

```shell
sudo rm /etc/udev/rules.d/70-syncclipboard.rules
sudo udevadm control --reload-rules
sudo udevadm trigger
sudo udevadm settle
```

Remove the current user's ACL entries from existing devices. Only run the commands for devices you previously granted access to:

```shell
sudo setfacl -x "u:$(id -un)" /dev/input/event*
sudo setfacl -x "u:$(id -un)" /dev/uinput
```

`-x` removes the specified user's entries without clearing other users' ACL entries. Check the result:

```shell
getfacl /dev/uinput
getfacl /dev/input/event0
```

The corresponding `user:your-username:...` entries should be gone. Access granted through groups or other system rules is not revoked by removing these ACL entries.
