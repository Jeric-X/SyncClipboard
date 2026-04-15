# SyncClipboard S3 同步协议规范

本文档描述 SyncClipboard 使用 S3 兼容对象存储作为同步后端的**存储布局约定**以及相关开发指南。

---

## 1. S3 专有配置项

以下配置项为 S3 服务器依赖。

| 参数 | 说明 | 必填 | 默认值 |
|------|------|------|--------|
| `ServiceURL` | S3 兼容端点 URL。使用 AWS 原生服务时留空 | 否 | 空 |
| `Region` | AWS 区域标识（如 `us-east-1`、`ap-northeast-1`） | 否 | `us-east-1` |
| `BucketName` | 存储桶名称 | **是** | — |
| `ObjectPrefix` | 所有对象 key 的统一前缀，用于隔离多客户端或多用途 | 否 | 空 |
| `ForcePathStyle` | 是否使用路径风格寻址（`endpoint/bucket/key`），大多数 S3 兼容服务需要开启 | 否 | `false` |
| `AccessKeyId` | 访问密钥 ID | **是** | — |
| `SecretAccessKey` | 访问密钥 Secret | **是** | — |

### 端点选择规则

- **AWS 原生**：`ServiceURL` 留空，SDK 根据 `Region` 自动解析标准 AWS S3 端点。
- **S3 兼容服务**（MinIO / Cloudflare R2 / 阿里云 OSS 等）：填写 `ServiceURL`（如 `https://s3.example.com`），一般同时启用 `ForcePathStyle = true`。

### 认证方式

使用 **AWS Signature V4** 签名。

---

## 2. 对象存储布局

所有对象 key 的格式为 `{ObjectPrefix}/{相对路径}`。前缀的首尾 `/` 会被 trim。

```
{BucketName}/
  └── {ObjectPrefix}/                       # 为空时没有此级
       ├── SyncClipboard.json               # 剪贴板 Profile 元数据文件
       └── file/                            # 附件目录（零或多个数据文件）
            ├── {dataName1}                 # 例如 "Text_2025-04-15_08-45-23_abc12345.tmp.txt"
            └── {dataName2}                 # 例如 "screenshot.png"
```

---

## 3. Profile 元数据格式（`SyncClipboard.json`）

参照 [API 说明](../README.md#syncclipboardjson)。

---

## 4. S3 API 调用约定

以下按操作列出具体的 S3 API 调用。所有操作均使用 AWS Signature V4 认证。

### 4.1 连接测试

| S3 操作 | `ListObjectsV2` |
|---------|-----------------|
| Bucket | `{BucketName}` |
| Prefix | `{ObjectPrefix}` |
| MaxKeys | `1` |

成功返回即说明凭据和桶可用。

### 4.2 初始化（确保目录结构）

检查 `file/` 目录标记是否存在：

| S3 操作 | `HeadObject`（GetObjectMetadata） |
|---------|----------------------------------|
| Key | `{prefix}/file/` |

如果返回 **404 / NoSuchKey**，则创建目录标记：

| S3 操作 | `PutObject` |
|---------|-------------|
| Key | `{prefix}/file/` |
| Body | 空字符串 |
| Content-Type | `application/x-directory` |

### 4.3 读取 Profile

| S3 操作 | `GetObject` |
|---------|-------------|
| Key | `{prefix}/SyncClipboard.json` |

- 成功：读取响应体 → JSON 反序列化为 ProfileDto
- 404 / NoSuchKey：返回空（首次使用，尚无 Profile）

### 4.4 写入 Profile

| S3 操作 | `PutObject` |
|---------|-------------|
| Key | `{prefix}/SyncClipboard.json` |
| Body | JSON 序列化的 ProfileDto |
| Content-Type | `application/json; charset=utf-8` |

### 4.5 上传数据文件

| S3 操作 | `PutObject` |
|---------|-------------|
| Key | `{prefix}/file/{fileName}` |
| Body | 文件二进制内容 |

`fileName` 即 ProfileDto 中的 `dataName` 字段值。

### 4.6 下载数据文件

| S3 操作 | `GetObject` |
|---------|-------------|
| Key | `{prefix}/file/{dataName}` |

`dataName` 来自 ProfileDto 的 `dataName` 字段。

### 4.7 清理旧文件

分页列出 `file/` 目录下所有对象并批量删除：

**第一步：列出对象**

| S3 操作 | `ListObjectsV2` |
|---------|-----------------|
| Prefix | `{prefix}/file/` |
| MaxKeys | `1000` |

使用 `ContinuationToken` 分页直到全部列出。

**第二步：批量删除**

| S3 操作 | `DeleteObjects` |
|---------|-----------------|
| Objects | 上一步列出的所有 key |


---

## 5. S3 兼容服务注意事项

### 5.1 签名与编码

对于非 AWS 的 S3 兼容服务，在 PUT 请求时应注意：

| 设置 | 说明 |
|------|------|
| 禁用 Chunked Transfer Encoding | 不使用 `Transfer-Encoding: chunked` |
| 禁用 Payload Signing | 不在请求体上计算 SHA-256 签名（使用 `UNSIGNED-PAYLOAD`） |
| 禁用 Trailer Checksum | 不追加 `x-amz-checksum-*` trailer |

许多 S3 兼容网关（R2、MinIO、OSS gateway 等）不支持 AWS SDK v3+ 默认启用的流式 Trailer 签名。

### 5.2 路径风格 vs 虚拟主机风格

- **AWS 标准**：虚拟主机风格 `{bucket}.s3.{region}.amazonaws.com/{key}`
- **兼容服务**：通常需要路径风格 `{endpoint}/{bucket}/{key}`（开启 `ForcePathStyle`）

### 5.3 协议选择

- 如果 `ServiceURL` 以 `http://` 开头，使用 HTTP；否则使用 HTTPS
- AWS 原生始终使用 HTTPS

---

