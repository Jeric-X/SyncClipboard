# [SyncClipboard](https://github.com/Jeric-X/SyncClipboard) 独立服务端 | Dedicated server

## 使用方法 | Usage

### 示例代码片段 | Example snippets

#### docker cli

```
docker run -d \
  --name=syncclipboard-server \
  -p 5033:5033 \
  --restart unless-stopped \
  jericx/syncclipboard-server:latest
```

## 服务端配置 | Server Conf

当你想自己配置服务器设置时，请按照以下模板在宿主机中创建一个 `appsettings.json` 文件，并按照自己的需要修改端口，账号和密码：

When you wish to configure server settings on your own, follow the template below to create an `appsettings.json` file on the host machine. Modify the port, username, and password according to your requirements:

```
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://0.0.0.0:5033"
      }
    }
  },
  "AppSettings": {
    "UserName": "admin",
    "Password": "admin",
    "EnableFcmPush": false,
    "FirebaseProjectId": null
  }
}
```

并将其映射至容器中，此时docker cli代码片段如下：

Map it into the container, so the Docker CLI snippet for this would be as follows:

```
docker run -d \
  --name=syncclipboard-server \
  -p 5033:5033 \
  -v /path/to/appsettings.json:/app/appsettings.json \
  --restart unless-stopped \
  jericx/syncclipboard-server:latest
```

## 参数 | Parameter

| 参数 \|  Parameter   | 功能 \|  Function                                    |
| -------------------- | ---------------------------------------------------- |
| --name               | 自定义容器名称 \| Custom container name              |
| -p 5033              | 端口映射 \| Port mapping, [hostport:containerport]   |
| -v /appsettings.json | 路径映射 \| Volume mapping, [hostpath:containerpath] |
| --restart            | 重启策略 \|  Restart Policy                          |

## 可选 FCM Push | Optional FCM Push

FCM 默认关闭。启用时，将 `AppSettings.EnableFcmPush` 设为 `true`，并按 Firebase Admin SDK 的要求通过 `GOOGLE_APPLICATION_CREDENTIALS` 提供服务账号文件路径。可使用 `AppSettings.FirebaseProjectId` 显式指定 Firebase project ID。

FCM is disabled by default. To enable it, set `AppSettings.EnableFcmPush` to `true` and provide the service-account file path through `GOOGLE_APPLICATION_CREDENTIALS`, as required by the Firebase Admin SDK. `AppSettings.FirebaseProjectId` can explicitly select the Firebase project.

只有 Firebase Admin 初始化成功时，`GET /api/capabilities` 才会返回 `push.fcm: true`。服务账号内容不会写入应用配置或日志。

`GET /api/capabilities` reports `push.fcm: true` only after Firebase Admin initializes successfully. Service-account contents are never stored in app configuration or logs.

----

- Readme Written by [Atlantis-Gura](https://github.com/Atlantis-Gura)
