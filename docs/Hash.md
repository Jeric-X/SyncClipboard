# Profile Hash 计算方法

## 概述

所有 Profile 类型的 Hash 基于 SHA256 算法，涉及文本的使用 UTF-8 编码。  
Hash 值通常以大写十六进制字符串形式表示，大小写不敏感。


## 1. TextProfile (纯文本)

将文本进行UTF8编码后计算SHA256

## 2. FileProfile 和 ImageProfile (单个文件或图片)

1. 计算文件内容的 SHA256 哈希值并转换为大写十六进制字符串
2. 获取文件名（不含路径）
3. 构造字符串：`文件名|文件内容SHA256的SHA256字符串`
4. 对该字符串进行 UTF-8 编码后再次计算 SHA256

### 伪代码

```
ContentHash = SHA256(FileContent)
CombinedString = "FileName|" + ToUpperCase(ContentHash)
Hash = SHA256(UTF8(CombinedString))
```

## 4. GroupProfile (多文件或文件夹)

### 计算方法

对所有文件和目录排序后，按特定格式和顺序拼接成完整字符串计算 SHA256

### 详细说明

#### 4.1 Entry 收集
- 收集所有输入文件/目录及其子文件/子目录，每一条作为一个 entry
- 每个 entry 以输入文件的父目录为根，取相对路径作为 EntryName
- EntryName 中的路径分隔符统一为 `/`
- 目录的 EntryName 以 `/` 结尾

#### 4.2 排序规则
- 取 EntryName 的 UTF-8 编码后的byte数组，以字典序升序排序

#### 4.3 Entry 的 Hash 字符串格式
- 目录：`{entryName}` (entryName 以 '/' 结尾)
- 文件：`{entryName}|{length}|{contentHash大写}`

其中：
- `entryName`：相对路径名
- `length`：文件字节长度
- `contentHash`：文件内容的 SHA256 哈希值（大写十六进制）

#### 4.4 哈希计算
- 将所有 entry 按序合并成一个字符串，UTF8编码后计算SHA256

### 示例

假设有以下文件结构：
```
folder/
  ├── a.txt (100 bytes, hash: abc123...)
  └── subdir/
      └── b.txt (200 bytes, hash: def456...)
```

Entry 列表（排序后）：
```
folder/
folder/a.txt|100|ABC123...
folder/subdir/
folder/subdir/b.txt|200|DEF456...
```

最终 Hash = SHA256(拼接所有 entry 字符串的 UTF-8 编码)