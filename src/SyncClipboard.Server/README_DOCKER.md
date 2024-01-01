# [SyncClipboard](https://github.com/Jeric-X/SyncClipboard) 独立服务端 | Dedicated server

## 使用方法 | Usage

### 示例代码片段 | Example snippets

#### docker cli

```
docker run -d \
  --name=syncclipboard-server \
  -p 5033:5033 \
  --restart unless-stopped \
  gurashark/syncclipboard-server:latest
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
    "Password": "admin"
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
  gurashark/syncclipboard-server:latest
```

## 参数 | Parameter

| 参数 \|  Parameter   | 功能 \|  Function                                    |
| -------------------- | ---------------------------------------------------- |
| --name               | 自定义容器名称 \| Custom container name              |
| -p 5033              | 端口映射 \| Port mapping, [hostport:containerport]   |
| -v /appsettings.json | 路径映射 \| Volume mapping, [hostpath:containerpath] |
| --restart            | 重启策略 \|  Restart Policy                          |

----

- readme written by [Atlantis-Gura](https://github.com/Atlantis-Gura)
