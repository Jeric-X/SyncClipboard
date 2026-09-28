#!/bin/sh
# No task content is evaluated as shell code. Every path is a quoted argument.
set -eu
work=$1
mode=${2:-supervisor}
if [ "$mode" = worker ]; then
    exec >>"$work/install.log" 2>&1
    printf '%s\n' "$$" >"$work/worker-pid"
else
    printf '%s\n' "$$" >"$work/helper-pid"
fi
kind=$(cat "$work/kind.txt")
target=$(cat "$work/target.txt")
stage=$(cat "$work/stage.txt")
backup=$(cat "$work/backup.txt")
executable=$(cat "$work/executable.txt")
parent_pid=$(cat "$work/pid.txt")
elevate=$(cat "$work/elevate.txt")

fail() {
    printf '%s\n' "$1" >"$work/failed.tmp"
    mv "$work/failed.tmp" "$work/failed"
}

launch() {
    if [ "$kind" = MacBundle ]; then
        /usr/bin/open "$executable"
    else
        # AppImage's inherited mount/loader variables refer to the old image.
        unset APPIMAGE APPDIR ARGV0 OWD LD_LIBRARY_PATH LD_PRELOAD
        nohup "$executable" </dev/null >>"$work/application.log" 2>&1 &
    fi
}

if [ "$mode" = supervisor ]; then
    worker_pid=
    interrupted() {
        trap '' HUP INT TERM
        # osascript is only the authorization bridge; the privileged worker consumes this marker.
        touch "$work/cancel"
        if [ -n "$worker_pid" ]; then wait "$worker_pid" 2>/dev/null || true; fi
        fail 'Update window was closed. Check the update log and backup before retrying.'
        exit 1
    }
    trap interrupted HUP INT TERM
    printf 'SyncClipboard update / 更新\n%s\n' "$work"
    if [ "$elevate" = yes ]; then
        if [ "$kind" != MacBundle ]; then fail 'The update directory is not writable.'; exit 1; fi
        /usr/bin/osascript "$work/ElevateMacUpdate.applescript" "$work/InstallMacBundle.sh" "$work" >>"$work/install.log" 2>&1 &
    else
        /bin/sh "$work/InstallMacBundle.sh" "$work" worker &
    fi
    worker_pid=$!
    language=$(cat "$work/language.txt" 2>/dev/null || true)
    while kill -0 "$worker_pid" 2>/dev/null; do
        if [ -f "$work/progress" ]; then
            phase=$(sed -n '1p' "$work/progress")
            percent=$(sed -n '2p' "$work/progress")
            label=$phase
            case "$language:$phase" in
                zh*:waiting) label='等待程序退出' ;;
                zh*:backup) label='备份程序文件' ;;
                zh*:installing) label='安装更新' ;;
                zh*:restoring) label='恢复旧版本' ;;
                zh*:verifying) label='校验程序文件' ;;
            esac
            if [ "${percent:--1}" -ge 0 ] 2>/dev/null; then
                # du measures allocated copy size, so percentages are approximate until the copy finishes.
                filled=$((percent / 5))
                bar=$(printf '%*s' "$filled" '' | tr ' ' '#')
                printf '\r%-24s [%-20s] ~%3s%%   ' "$label" "$bar" "$percent"
            else
                printf '\r%-60s' "$label..."
            fi
        fi
        sleep 0.2
    done
    wait "$worker_pid" || true
    trap - HUP INT TERM
    printf '\n'
    if [ -f "$work/installed" ] || [ -f "$work/restored" ]; then
        launch || fail 'Could not restart SyncClipboard. Start it manually.'
    elif [ ! -f "$work/failed" ]; then
        fail 'Authorization was canceled or the update helper exited unexpectedly.'
    fi
    if [ -f "$work/failed" ]; then
        cat "$work/failed"
        printf '\nLog / 日志: %s/install.log\nPress Enter to finish / 按回车结束安装进程\n' "$work"
        if [ -t 0 ]; then read -r answer || true; fi
        exit 1
    fi
    printf 'Update completed / 更新完成\n'
    exit 0
fi

progress() {
    printf '%s\n%s\n' "$1" "$2" >"$work/progress.tmp"
    mv "$work/progress.tmp" "$work/progress"
}

check_canceled() {
    if [ -f "$work/cancel" ]; then
        fail 'Update canceled.'
        return 1
    fi
}

# Both the payload and the backup stay in the updater's private work directory.
# Copying may cross filesystems; only remove the installed copy after backup succeeds.
copy_with_progress() {
    source_path=$1
    destination_path=$2
    phase=$3
    if [ "$phase" != restoring ]; then check_canceled || return 1; fi
    [ -e "$source_path" ] || return 1
    total=$(du -sk "$source_path" | awk '{print $1}')
    progress "$phase" 0
    if [ "$kind" = MacBundle ]; then
        /usr/bin/ditto "$source_path" "$destination_path" &
    else
        cp -p "$source_path" "$destination_path" &
    fi
    copy_pid=$!
    while kill -0 "$copy_pid" 2>/dev/null; do
        if [ "$phase" != restoring ]; then check_canceled || return 1; fi
        copied=$(du -sk "$destination_path" 2>/dev/null | awk '{print $1}')
        copied=${copied:-0}
        percent=0
        if [ "$total" -gt 0 ]; then percent=$((copied * 100 / total)); fi
        if [ "$percent" -gt 99 ]; then percent=99; fi
        progress "$phase" "$percent"
        sleep 0.2
    done
    wait "$copy_pid" || { copy_pid=; return 1; }
    copy_pid=
    if [ "$phase" != restoring ]; then check_canceled || return 1; fi
    progress "$phase" 100
}

backup_complete=no
backup_created=no
replacement_started=no
parent_exited=no
copy_pid=
on_error() {
    trap - EXIT HUP INT TERM
    if [ -n "$copy_pid" ]; then
        kill "$copy_pid" 2>/dev/null || true
        wait "$copy_pid" 2>/dev/null || true
        copy_pid=
    fi
    if [ "$replacement_started" = yes ] && [ "$backup_complete" = yes ]; then
        # Removal can fail after deleting some files. Still attempt to restore the completed backup.
        rm -rf "$target" || true
        if copy_with_progress "$backup" "$target" restoring && {
            [ "$kind" != MacBundle ] || /usr/bin/codesign --verify --deep --strict "$target"
        }; then
            touch "$work/restored"
        else
            fail 'Update failed and rollback failed. The backup is preserved in the update directory.'
        fi
    elif [ "$backup_created" = yes ] && [ "$backup_complete" = no ]; then
        # A partial backup is disposable; the original has not been touched.
        rm -rf "$backup" || true
    fi
    if [ "$parent_exited" = yes ] && [ "$replacement_started" = no ] && [ -e "$target" ]; then
        touch "$work/restored"
    fi
    if [ ! -f "$work/failed" ]; then fail 'Update failed. See install.log for details.'; fi
    progress failed 0
    exit 1
}
trap on_error EXIT HUP INT TERM

if [ -e "$backup" ]; then fail 'An update backup path already exists.'; exit 1; fi
case "$kind" in
    MacBundle) /usr/bin/codesign --verify --deep --strict "$stage" ;;
    AppImage) chmod 755 "$stage" ;;
    *) fail 'Unsupported update format.'; exit 1 ;;
esac
# Check access before the application exits, without creating a sibling staging file.
if [ ! -w "$(dirname "$target")" ]; then fail 'The update directory is not writable.'; exit 1; fi
progress waiting -1
touch "$work/ready"

count=0
while [ ! -f "$work/commit" ]; do
    if [ -f "$work/cancel" ] || [ "$count" -ge 300 ]; then fail 'Update handoff canceled.'; exit 1; fi
    sleep 1
    count=$((count + 1))
done
count=0
while kill -0 "$parent_pid" 2>/dev/null; do
    if [ -f "$work/cancel" ] || [ "$count" -ge 60 ]; then fail 'Timed out waiting for SyncClipboard to exit.'; exit 1; fi
    sleep 1
    count=$((count + 1))
done
parent_exited=yes
check_canceled
backup_created=yes
copy_with_progress "$target" "$backup" backup
backup_complete=yes
if [ "$kind" = MacBundle ] && [ "$(id -u)" = 0 ]; then
    # The normal user's next startup must be able to clean this backup, even after authorization.
    /usr/sbin/chown -R -P "$(/usr/bin/stat -f '%u:%g' "$work")" "$backup"
fi
check_canceled
replacement_started=yes
rm -rf "$target"
copy_with_progress "$stage" "$target" installing
progress verifying -1
if [ "$kind" = MacBundle ]; then /usr/bin/codesign --verify --deep --strict "$target"; fi
check_canceled
touch "$work/installed"
touch "$work/completed"
progress 'done' 100
trap - EXIT HUP INT TERM
# The newly started main application cleans successful tasks after all helpers have exited.
