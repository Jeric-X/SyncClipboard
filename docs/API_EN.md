# SyncClipboard API

[Project home](../README_EN.md) · [中文](API.md) | English

In a standalone server environment, set the environment variable ASPNETCORE_ENVIRONMENT to Development before running the server, or open the server in the desktop client and enable diagnostic mode in settings.
Then visit `http://ip:port/swagger/index.html` to access the API description page.

APIs that do not start with `/api/` are WebDAV-compatible APIs. When implementing clients, calling these APIs can support clipboard synchronization based on both WebDAV servers and official SyncClipboard servers. The key APIs are described below.

### Get Clipboard
```shell
GET /SyncClipboard.json
GET /file/dataName            # optional
```

### Upload Clipboard
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

- All API fields are case-sensitive
- `text` stores the clipboard preview string, or the complete content of Text type clipboard
- `hasData` indicates whether an additional file is used to store the complete clipboard information
  - For Image/File/Group types, `hasData` is always true
  - For Text type, depending on the original string length, you can optionally use an additional UTF8-encoded `.txt` file to store the complete string. If so, the `text` field only stores the beginning part of the complete string
- `hash` value is a unique identifier of the clipboard content. For the calculation method, please refer to [docs/Hash.md](Hash.md)
  - The sender should provide `hash` information whenever possible
  - When the `hash` value exists, the receiver should verify the consistency between the `hash` information and the clipboard content, and execute the error handling process when inconsistent
  - When `hash` is empty, or in an environment where `hash` cannot be calculated, you can use the combination of `type`/`text` to simply determine the equality of clipboard content
- `transferDataHash` is the SHA-256 of the complete byte stream of the actual transfer file, encoded as 64 uppercase hexadecimal characters, and is used to verify transfer file integrity
  - Use it only when `hasData` is `true`
- `size` indicates the total byte size of the copied file, or the length of the complete string for Text type clipboard
- `syncedAt` records when the current remote clipboard was submitted, as an ISO 8601 timestamp with a UTC offset
  - When the official server receives a clipboard upload, it uses the server's time and ignores the time provided by the client
  - For WebDAV/S3, the client sets this field when uploading
  - Client clocks may differ, so this field should not be used to determine concurrent write ordering

### S3 Sync Protocol Specification

For protocol and data format specifications when using S3-compatible object storage as sync backend, please refer to [S3 Adapter Design](S3-Adapter-Design.md).

## History API

The official standalone server and the desktop client's built-in server share these endpoints, authenticated with HTTP Basic authentication. WebDAV and S3 do not provide them. Replace `USER`, `PASSWORD`, the server URL and content hash in the examples with actual values; see [Hash.md](Hash.md) for hash calculation.

### Capability negotiation

`GET /api/capabilities`:

```json
{"historySync":true,"historyTags":true,"historyName":true}
```

`historySync` indicates basic history synchronization. `historyTags` and `historyName` independently indicate tag and name synchronization support. Clients should check each field and synchronize that metadata only when supported; a missing or `false` field does not declare support. Older servers may return 404 for the capability endpoint. Do not infer name support from a version or tag support. Probe again after changing connections or accounts. Names and tags are history metadata; they do not change the record type, content hash or pasted content.

### Name and tag states

JSON uses `name` and `tags`. Multipart text parts use the same names; the server also accepts the older client's `Tags` spelling.

| Field | Missing or JSON `null` | Explicit clear | Set value |
| --- | --- | --- | --- |
| `name` | Unknown for a new record; preserve the existing value when merging | `""` | `"App icon"` |
| `tags` | Unknown for a new record; preserve the existing value when merging | `[]` | `["Work","工作"]` |

Each field is merged independently. Names are trimmed on write; whitespace-only input clears the name. Tags are NFC-normalized, trimmed, deduplicated and sorted with case-sensitive comparisons. Each tag must contain 1–64 Unicode characters without control characters or line breaks; a record can have at most 32 distinct tags.

Writes follow the existing record version and modification-time conflict rules; explicit clearing does not bypass conflict resolution. Older requests that omit fields do not clear existing values. A stale request may only backfill nonempty metadata into fields that remain unknown on the server; it cannot overwrite known or explicitly cleared values.

### Initial or duplicate upload

`POST /api/history` accepts `multipart/form-data`. All metadata must precede the optional binary `data` part. `hash` and `type` are required. Use `Text`, `Image`, `File` or `Group` for `type`. Other optional metadata includes `text`, `size`, `createTime`, `lastModified`, `lastAccessed`, `version`, `starred`, `pinned`, `isDeleted` and `hasData`.

`name` is plain text, not a JSON string: an empty text part clears it, omission means unknown, and the literal text `null` is a valid four-character name. `tags` is a text part containing a JSON array: `[]` clears it, omission or the text `null` means unknown, and empty text is invalid JSON.

```sh
curl -u 'USER:PASSWORD' 'http://localhost:5033/api/history' \
  --form-string 'hash=CONTENT_HASH' \
  --form-string 'type=Image' \
  --form-string 'text=icon.png' \
  --form-string 'name=App icon' \
  --form-string 'tags=["Work","工作"]' \
  --form-string 'hasData=true' \
  -F 'data=@icon.png'
```

Use `--form-string 'name='` to clear the name and `--form-string 'tags=[]'` to clear tags. When sending `data`, the optional `X-SyncClipboard-Transfer-Data-Hash` request header can declare the actual transfer file's SHA-256 as 64 uppercase hexadecimal characters. Transfer data follows the existing format for its profile type.

Success returns **200** with the final `HistoryRecordDto`. Duplicate uploads also return the final server record; do not assume the response equals the request. Invalid metadata returns 400, and transfer-data validation failures return 422.

### Update metadata

`PATCH /api/history/{type}/{hash}` accepts JSON. Name, tags, starred status, pinned status, deletion status, version and timestamps are optional. To set a name and tags:

```sh
curl -u 'USER:PASSWORD' -X PATCH \
  'http://localhost:5033/api/history/Image/CONTENT_HASH' \
  -H 'Content-Type: application/json' \
  -d '{"name":"App icon","tags":["Work","工作"]}'
```

Clear both fields:

```json
{"name":"","tags":[]}
```

Change only starred status, preserving name and tags:

```json
{"starred":true}
```

Synchronization clients can include `version`, `lastModified` and `lastAccessed` (ISO 8601 with a time zone). When version and modification time are omitted, the server supplies them under the existing rules. **200** indicates an accepted update and **409** a conflict; both return the final server metadata, which clients should merge with concurrency checks. A missing record or a record that cannot be restored returns 404.

Example PATCH response (200 and 409 share this structure; values reflect the final server record):

```json
{
  "name": "App icon",
  "tags": ["Work", "工作"],
  "starred": true,
  "pinned": false,
  "isDelete": false,
  "version": 2,
  "lastModified": "2026-11-18T00:00:00Z",
  "lastAccessed": "2026-11-18T00:00:00Z"
}
```

Unknown `name` and `tags` are omitted from PATCH responses; explicitly cleared values appear as `""` and `[]`. Preserve this distinction when merging, and prevent late responses from overwriting subsequent local edits. PATCH uses `isDelete`, while full history records use `isDeleted`.

### Queries and real-time notifications

- `GET /api/history/{profileId}` returns one full record. `profileId` is `Type-Hash`, such as `Image-CONTENT_HASH`; a missing record returns 404.
- `POST /api/history/query` accepts form fields and returns an array of full records. `page` starts at 1, with at most 50 records per page.
- `GET /api/history/{profileId}/data` downloads the transfer data. The `X-SyncClipboard-Transfer-Data-Hash` response header provides its checksum.
- SignalR `/SyncClipboardHub` sends `RemoteHistoryChanged` with the same full-record structure.

By default, `searchText` matches only the original content. When the server supports `historyName`, send `searchNames=true` to also match record names. The `tags` filter is a JSON-array text part and matches any complete tag name. `onlyUntagged=true` cannot be combined with a nonempty tag filter. You can also combine `types` (type flags), `starred`, `before`, `after`, `modifiedAfter` and `sortByLastAccessed`; filtering happens before pagination.

```sh
curl -u 'USER:PASSWORD' 'http://localhost:5033/api/history/query' \
  --form-string 'page=1' \
  --form-string 'searchText=icon' \
  --form-string 'searchNames=true' \
  --form-string 'tags=["Work"]'
```

Example full record used by creation responses, single-record queries and real-time notifications; paginated queries return an array of these objects:

```json
{
  "name": "App icon",
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

Full records return `null` for unknown metadata, and `""` or `[]` for explicit clears. Older servers may omit these fields; clients should not clear local values merely because fields are absent. Incremental queries use `modifiedAfter`. A record missing from search results is not evidence of server deletion; confirm it with a full reconciliation without a search keyword.
