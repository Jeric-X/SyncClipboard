# SyncClipboard API

[返回项目首页](../README.md) · 中文 | [English](API_EN.md)

在独立服务器运行环境下设定环境变量ASPNETCORE_ENVIRONMENT为Development后运行服务器，或桌面客户端打开服务器并打开设置里的诊断模式后，
访问`http://ip:端口/swagger/index.html`可以打开API描述页面

API路径不以`/api/`起始的为WebDAV兼容API，实现客户端时，调用此类API可以同时支持基于WebDAV服务器与SyncClipboard官方服务器的剪贴板同步功能，其中的关键API的说明如下

### 获取剪贴板
```shell
GET /SyncClipboard.json
GET /file/dataName            # optional
```

### 上传剪贴板
```shell
PUT /file/dataName            # optional
PUT /SyncClipboard.json
```

### SyncClipboard.json
```jsonc
{
  "type": "Text",                          // or Image/File/Group, required
  "hash": "string",                        // optional, empty string is treated as null
  "text": "string",                        // required
  "hasData": true,                         // or false, required
  "dataName": "string",                    // if hasData is true, required
  "transferDataHash": "string",            // optional
  "size": 0,                               // optional
  "syncedAt": "2026-10-09T15:30:00+08:00"  // optional
}
```

- API所有字段大小写敏感
- `text`储存剪贴板预览字符串，或完整的Text类型剪贴板内容
- `hasData`标识是否使用一个额外文件存储完整的剪贴板信息
  - 对于Image/File/Group类型，`hasData`恒为true
  - 对于Text类型，可以根据原字符串的长度，可选是否使用额外的UTF8编码的`.txt`文件存储完整字符串，如果这样做，`text`字段仅储存完整字符串的起始部分内容
- `hash`值为剪贴板内容的唯一标识，计算方法请参考[docs/Hash.md](Hash.md)
  - 发送方应尽量提供`hash`信息
  - 当`hash`值存在时，接收方应验证`hash`信息与剪贴板内容的一致性，在不一致时执行错误处理流程
  - 当`hash`为空时，或处于无法计算`hash`的环境，可以使用`type`/`text`的组合简单判断剪贴板内容的相等性
- `transferDataHash`是实际传输文件完整字节流的 SHA-256，使用 64 位大写十六进制字符串，用于校验传输文件完整性
  - 仅在 `hasData` 为 `true` 时使用
- `size`标识复制文件的总字节大小，或Text类型剪贴板完整字符串的长度，仅用于展示
- `syncedAt`记录当前远端剪贴板的提交时间，使用带 UTC 偏移量的 ISO 8601 时间
  - 官方服务器收到剪贴板推送时会使用服务器时间，忽略客户端提供的时间
  - WebDAV/S3 由客户端在推送时设置
  - 客户端时钟可能不同，不应使用该字段判断并发写入顺序

### S3 同步协议规范

使用 S3 兼容对象存储作为同步后端时的协议与数据格式规范请参阅 [S3 Adapter Design](S3-Adapter-Design.md)。

## 历史记录 API

以下接口由官方独立服务端和客户端内置服务端共同提供，使用 HTTP Basic 身份验证。WebDAV 和 S3 不提供这些历史接口。示例中的 `USER`、`PASSWORD`、服务器地址和内容哈希需要替换为实际值；哈希计算见 [Hash.md](Hash.md)。

### 能力协商

`GET /api/capabilities`：

```json
{"historySync":true,"historyTags":true,"historyName":true}
```

`historySync` 表示基础历史同步，`historyTags` 和 `historyName` 分别表示标签和名称同步能力。客户端应独立判断这两个字段，只向支持相应能力的服务端同步该字段；缺失或为 `false` 表示未声明支持。旧端的能力端点可能返回 404，不能由版本或标签能力推断名称能力。连接或账号改变后应重新探测。名称和标签属于历史元数据，不改变记录的类型、内容哈希或粘贴内容。

### 名称和标签的三态

JSON 字段使用 `name`、`tags`。上传的 multipart 文本段也使用这两个名称；服务端兼容旧客户端发送的 `Tags`。

| 字段 | 缺省或 JSON `null` | 明确清空 | 设置值 |
| --- | --- | --- | --- |
| `name` | 新记录未知；合并已有记录时保留原值 | `""` | `"应用图标"` |
| `tags` | 新记录未知；合并已有记录时保留原值 | `[]` | `["工作","Work"]` |

两个字段独立合并。名称写入时去除首尾空白，纯空白等同于清空。标签采用 NFC 规范化、去除首尾空白、按大小写敏感规则去重和排序；每个标签为 1–64 个 Unicode 字符，不含控制字符或换行，最多 32 个不同标签。

写入仍按记录现有的版本和修改时间规则裁决；发送清空值并不绕过冲突判断。旧请求省略字段不会清空已有值。陈旧请求仅可给服务端仍未知的字段补入非空值，不能覆盖已知值或已明确清空的值。

### 首次或重复上传

`POST /api/history` 使用 `multipart/form-data`，所有元数据必须在可选的二进制 `data` 段之前。`hash` 和 `type` 必填。`type` 使用 `Text`、`Image`、`File` 或 `Group`。可选元数据还包括 `text`、`size`、`createTime`、`lastModified`、`lastAccessed`、`version`、`starred`、`pinned`、`isDeleted`、`hasData`。

`name` 是普通文本，不是 JSON 字符串：空文本段表示清空，缺省表示未知，文本 `null` 是合法的四个字符名称。`tags` 是包含 JSON 数组的文本段；`[]` 清空，缺省或文本 `null` 表示未知，空文本不是合法 JSON。

```sh
curl -u 'USER:PASSWORD' 'http://localhost:5033/api/history' \
  --form-string 'hash=CONTENT_HASH' \
  --form-string 'type=Image' \
  --form-string 'text=icon.png' \
  --form-string 'name=应用图标' \
  --form-string 'tags=["工作","Work"]' \
  --form-string 'hasData=true' \
  -F 'data=@icon.png'
```

名称清空时用 `--form-string 'name='`，标签清空时用 `--form-string 'tags=[]'`。存在 `data` 时，可在请求头 `X-SyncClipboard-Transfer-Data-Hash` 中提供实际传输文件的 SHA-256（64 位大写十六进制）。文件传输格式沿用对应类型的既有规则。

成功返回 **200** 和最终的 `HistoryRecordDto`，重复上传也返回最终服务端记录，不能假定回包与请求相同。无效元数据返回 400，传输数据校验失败返回 422。

### 修改元数据

`PATCH /api/history/{type}/{hash}` 接收 JSON。名称、标签、收藏、置顶、删除标记、版本和时间均为可选项。设置名称和标签：

```sh
curl -u 'USER:PASSWORD' -X PATCH \
  'http://localhost:5033/api/history/Image/CONTENT_HASH' \
  -H 'Content-Type: application/json' \
  -d '{"name":"应用图标","tags":["工作","Work"]}'
```

清空名称和标签：

```json
{"name":"","tags":[]}
```

只修改收藏，保留名称和标签：

```json
{"starred":true}
```

同步客户端可携带 `version`、`lastModified` 和 `lastAccessed`（ISO 8601，带时区）。省略版本和修改时间时，服务端会按已有规则填充。收到 **200** 表示接受更新，**409** 表示冲突；两者都返回最终服务端元数据，客户端应按并发合并规则处理回包。记录不存在或无法恢复时返回 404。

PATCH 回包示例（200 和 409 使用同一结构，具体值取自最终服务端记录）：

```json
{
  "name": "应用图标",
  "tags": ["Work", "工作"],
  "starred": true,
  "pinned": false,
  "isDelete": false,
  "version": 2,
  "lastModified": "2026-11-18T00:00:00Z",
  "lastAccessed": "2026-11-18T00:00:00Z"
}
```

未知的 `name`、`tags` 在 PATCH 回包中省略；已清空的值会返回 `""`、`[]`。客户端合并时应保留三态区别，并防止迟到回包覆盖用户后来作出的编辑。注意 PATCH 使用 `isDelete`，完整历史记录使用 `isDeleted`。

### 查询与实时通知

- `GET /api/history/{profileId}`：返回单条完整记录，`profileId` 为 `Type-Hash`（例如 `Image-CONTENT_HASH`）；不存在时返回 404。
- `POST /api/history/query`：表单查询，返回完整记录数组，`page` 从 1 开始，每页最多 50 条。
- `GET /api/history/{profileId}/data`：下载记录传输数据，响应头 `X-SyncClipboard-Transfer-Data-Hash` 提供其校验值。
- SignalR `/SyncClipboardHub` 的 `RemoteHistoryChanged` 通知：参数为同一完整记录结构。

查询的 `searchText` 默认只匹配原内容；服务端支持 `historyName` 时，可发送 `searchNames=true` 同时匹配名称。标签筛选 `tags` 是 JSON 数组文本段，匹配其中任一完整标签；`onlyUntagged=true` 不能与非空标签筛选同时使用。还可组合 `types`（类型标志）、`starred`、`before`、`after`、`modifiedAfter`、`sortByLastAccessed`，由服务端过滤后分页。

```sh
curl -u 'USER:PASSWORD' 'http://localhost:5033/api/history/query' \
  --form-string 'page=1' \
  --form-string 'searchText=图标' \
  --form-string 'searchNames=true' \
  --form-string 'tags=["工作"]'
```

创建、单条查询和实时通知中的完整记录示例；分页查询返回此类对象的数组：

```json
{
  "name": "应用图标",
  "tags": ["Work", "工作"],
  "hash": "CONTENT_HASH",
  "text": "icon.png",
  "type": "Image",
  "createTime": "2026-11-18T00:00:00Z",
  "lastModified": "2026-11-18T00:00:00Z",
  "lastAccessed": "2026-11-18T00:00:00Z",
  "starred": true,
  "pinned": false,
  "size": 1024,
  "hasData": true,
  "version": 2,
  "isDeleted": false
}
```

完整记录中的未知字段返回 `null`，明确清空返回 `""`、`[]`。旧端也可能省略字段，客户端不应因此清空本地值。增量查询使用 `modifiedAfter`；搜索结果缺少某条记录不表示服务端已删除它，应通过不带关键词的完整对账确认。
