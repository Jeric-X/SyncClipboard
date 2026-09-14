"""Validate the HTTP API document without opening Swagger UI."""


def require(condition, message):
    if not condition:
        raise AssertionError(message)


def resolve(document, reference):
    require(reference.startswith("#/"), f"Unexpected external schema reference: {reference}")
    value = document
    for part in reference[2:].split("/"):
        value = value[part.replace("~1", "/").replace("~0", "~")]
    return value


def check_references(document, value):
    if isinstance(value, dict):
        if "$ref" in value:
            resolve(document, value["$ref"])
        for child in value.values():
            check_references(document, child)
    elif isinstance(value, list):
        for child in value:
            check_references(document, child)


def check_upload(document):
    operation = document["paths"]["/api/history"]["post"]
    header = next(parameter for parameter in operation["parameters"]
                  if parameter["name"] == "X-SyncClipboard-Transfer-Data-Hash")
    require(header["in"] == "header" and not header.get("required", False), "Transfer hash header changed")
    body = operation["requestBody"]
    require(body.get("required") is True, "History upload must require a request body")
    schema = body["content"]["multipart/form-data"]["schema"]
    require(schema["type"] == "object" and set(schema["required"]) == {"hash", "type"},
            "History upload required fields changed")
    properties = schema["properties"]
    expected = {"hash", "type", "createTime", "lastModified", "lastAccessed", "starred",
                "pinned", "version", "isDeleted", "text", "size", "hasData", "data"}
    require(set(properties) == expected, "History upload fields changed")
    require(properties["data"]["type"] == "string" and properties["data"]["format"] == "binary",
            "History transfer data is not described as a binary part")
    require("last part" in properties["data"]["description"], "Transfer part ordering is undocumented")
    for name in ("createTime", "lastModified", "lastAccessed"):
        require(properties[name]["format"] == "date-time", f"Invalid timestamp schema: {name}")
    for name in ("starred", "pinned", "isDeleted", "hasData"):
        require(properties[name]["type"] == "boolean", f"Invalid boolean schema: {name}")
    require(properties["version"]["format"] == "int32" and properties["size"]["format"] == "int64",
            "History integer widths changed")


def check_query(document):
    media = document["paths"]["/api/history/query"]["post"]["requestBody"]["content"]["multipart/form-data"]
    schema = media["schema"]
    properties = schema["properties"]
    expected = {"page", "before", "after", "modifiedAfter", "types", "searchText",
                "starred", "sortByLastAccessed"}
    require(set(properties) == expected, "History query field names changed")
    require(set(schema.get("required", [])) <= expected, "Required query fields use different names")
    require(set(media.get("encoding", {})) <= expected, "Query encodings do not match the form field names")
    require(properties["page"]["format"] == "int32", "History page must be an integer")
    for name in ("before", "after", "modifiedAfter"):
        require(properties[name]["format"] == "date-time", f"Invalid query timestamp: {name}")
    require(properties["starred"]["type"] == "boolean"
            and properties["sortByLastAccessed"]["type"] == "boolean", "Invalid query flags")


def check_enums(document):
    schemas = document["components"]["schemas"]
    expected = {
        "ProfileType": ["Text", "File", "Image", "Group", "Unknown", "None"],
        "ProfileTypeFilter": ["None", "Text", "File", "Image", "Group", "FileAndGroup", "All"],
    }
    for name, values in expected.items():
        require(schemas[name]["type"] == "string" and schemas[name]["enum"] == values,
                f"Wire enum representation changed: {name}")


def check_security(document):
    scheme = document["components"]["securitySchemes"]["BasicAuthentication"]
    require(scheme["type"] == "http" and scheme["scheme"] == "basic", "HTTP Basic authentication is undocumented")
    require({"BasicAuthentication": []} in document["security"], "API authentication requirement is missing")


def check_document(document):
    require(document["openapi"].startswith("3.0."), "Unexpected OpenAPI document format migration")
    expected_paths = {
        "/api/history/{profileId}": {"get"},
        "/api/history/{profileId}/data": {"get"},
        "/api/history/query": {"post"},
        "/api/history": {"post"},
        "/api/history/{type}/{hash}": {"patch"},
        "/api/history/statistics": {"get"},
        "/api/history/clear": {"delete"},
        "/api/time": {"get"},
        "/api/version": {"get"},
        "/file": {"delete"},
        "/file/{fileName}": {"head", "get", "put"},
        "/SyncClipboard.json": {"get", "put"},
    }
    methods = {"get", "head", "post", "put", "patch", "delete", "options", "trace"}
    actual = {path: set(item) & methods for path, item in document["paths"].items()}
    require(actual == expected_paths, "Documented API paths or methods changed")
    check_references(document, document)
    check_upload(document)
    check_query(document)
    check_enums(document)
    check_security(document)
