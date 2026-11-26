# SyncClipboard 独立服务端 | Dedicated server

## Docker

```sh
docker run -d --name syncclipboard-server -p 5033:5033 \
  -v /data/syncclipboard-server:/app/data \
  -e SYNCCLIPBOARD_USERNAME -e SYNCCLIPBOARD_PASSWORD \
  --restart unless-stopped jericx/syncclipboard-server:latest
```

先在宿主环境设置这两个变量，或在数据目录中创建下方的账号配置。容器对数据目录需要写权限。

Set both variables in the host environment, or create the account configuration below in the mounted data directory. The server needs write access to that directory.

## server-settings.json

```json
{
  "History": {
    "MaxSavedHistoryCount": 1000,
    "HistoryRetentionMinutes": 0,
    "ProtectTaggedRecords": true,
    "ProtectTitledRecords": true
  },
  "Users": [
    { "UserName": "alice", "Password": "<choose a password>" },
    { "UserName": "bob", "Password": "<choose a different password>" }
  ]
}
```

启动时服务端为新账号分配稳定的 `Id`，并用带随机盐的 `PasswordHash` 替换 `Password`。添加账号或重置密码时，停服、编辑此文件、再启动。改名或改密码时保留原 `Id`。历史条数限制按用户计算。请备份整个数据目录，包括账号配置。

On startup the server assigns each new account a stable ID and replaces its password with a salted hash. Stop the server before editing this file. To reset a password, add a new Password field to the existing entry and restart. Keep its ID when renaming the account or changing the password. History count limits apply per user. Back up the entire data directory, including the account file.

## 从旧版本升级 | Upgrading

首次启动会把旧版 AppSettings 业务配置导入新文件。原账号沿用 default_user，保留原有历史、当前剪贴板和待同步文件。首次创建配置时，两个登录环境变量可提供初始管理员凭据。验证升级成功后，请从旧配置及其备份中移除明文密码。端口、日志和 TLS 继续在 appsettings.json 配置。

The first startup imports legacy AppSettings. The original account retains its history and clipboard under the ID default_user. Both login environment variables can supply the initial administrator credentials when the configuration is created. Remove the plaintext password from the legacy configuration and its backups after verifying the migration. Configure Kestrel, logging and TLS in appsettings.json.

## 管理 API | Management API

独立服务端和桌面内置服务提供相同的 HTTP Basic 管理接口。使用初始管理员账号认证。API 修改立即生效并写入 server-settings.json。手动编辑前先停服，修改后重新启动。桌面客户端配置升级后仍管理启停、端口和 TLS；内置服务的业务配置位于应用数据目录的 server-settings.json。

Both hosts expose the same administrator-only endpoints. Authenticate with HTTP Basic using the initial account. API updates take effect immediately and survive restart. Stop the server before editing the file manually. The embedded server stores this file in the desktop application's data directory.

| Method | Path | Request or result |
| --- | --- | --- |
| GET | /api/admin/settings | Read history retention settings |
| PUT | /api/admin/settings | Replace the History object shown above |
| GET | /api/admin/users | List account IDs and usernames |
| POST | /api/admin/users | Send UserName and Password to create an account |
| PUT | /api/admin/users/{id} | Send UserName and optionally Password to rename or reset |
| DELETE | /api/admin/users/{id} | Revoke login for a non-administrator account |

删除账号会撤销后续登录，已存的历史按原 ID 保留。重新创建同名账号会分配新的 ID。迁移后的桌面配置备份可能仍包含原密码。

Deleting an account revokes new logins and retains the history under its original ID. Recreating the username assigns a new ID. Desktop configuration backups made during migration can still contain the old password.
