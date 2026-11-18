using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using SyncClipboard.Server.Core.Models;

namespace SyncClipboard.Server.Core.Swagger;

public class HistoryMetadataDocumentationFilter : ISchemaFilter, IOperationFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type != typeof(HistoryRecordDto) && context.Type != typeof(HistoryRecordUpdateDto))
            return;

        var name = schema.Properties["name"];
        name.Nullable = true;
        name.Description = "Record name. Missing/null means unknown and preserves the existing value when merging. "
            + "An empty string explicitly clears it. Surrounding whitespace is trimmed on write.";
        name.Example = new OpenApiString("应用图标");
        var tags = schema.Properties["tags"];
        tags.Nullable = true;
        tags.Description = "Record tags. Missing/null means unknown and preserves the existing value when merging. "
            + "An empty array explicitly clears them. Each field is merged independently.";
        tags.Example = new OpenApiArray { new OpenApiString("工作"), new OpenApiString("Work") };
        var example = MetadataExample();
        example["version"] = new OpenApiInteger(2);
        example["lastModified"] = new OpenApiString("2026-11-18T00:00:00Z");
        example["lastAccessed"] = new OpenApiString("2026-11-18T00:00:00Z");
        example["starred"] = new OpenApiBoolean(true);
        example["pinned"] = new OpenApiBoolean(false);
        example[context.Type == typeof(HistoryRecordDto) ? "isDeleted" : "isDelete"] = new OpenApiBoolean(false);
        if (context.Type == typeof(HistoryRecordDto))
        {
            example["type"] = new OpenApiString("Image");
            example["hash"] = new OpenApiString("<content hash>");
            example["text"] = new OpenApiString("icon.png");
            example["createTime"] = new OpenApiString("2026-11-18T00:00:00Z");
            example["size"] = new OpenApiLong(1024);
            example["hasData"] = new OpenApiBoolean(true);
        }
        schema.Example = example;
    }

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var path = context.ApiDescription.RelativePath;
        var method = context.ApiDescription.HttpMethod;
        if (path == "api/history" && method == "POST")
        {
            operation.Description = "Upload metadata before the optional data part. Returns the final server record, "
                + "including name and tags, even for a duplicate upload. Updates follow the existing version/time conflict rules.";
            AddResponse(operation, context, "200", typeof(HistoryRecordDto), "Final server record.");
        }
        else if (path == "api/history/{type}/{hash}" && method == "PATCH")
        {
            operation.Description = "Update metadata using the existing version/time conflict rules. "
                + "Omitted/null name and tags preserve the stored values; empty string/array explicitly clear them. "
                + "Both 200 and 409 return the final server metadata; unknown name/tags may be omitted.";
            AddResponse(operation, context, "200", typeof(HistoryRecordUpdateDto), "Accepted; final server metadata.");
            AddResponse(operation, context, "409", typeof(HistoryRecordUpdateDto), "Conflict; final server metadata.");
            foreach (var media in operation.RequestBody.Content.Values)
            {
                media.Examples = new Dictionary<string, OpenApiExample>
                {
                    ["set"] = new() { Summary = "Set name and tags", Value = MetadataExample() },
                    ["clear"] = new()
                    {
                        Summary = "Explicitly clear both fields",
                        Value = new OpenApiObject { ["name"] = new OpenApiString(""), ["tags"] = new OpenApiArray() }
                    },
                    ["starOnly"] = new()
                    {
                        Summary = "Change only starred status, preserving name and tags",
                        Value = new OpenApiObject { ["starred"] = new OpenApiBoolean(true) }
                    }
                };
            }
        }
    }

    private static void AddResponse(
        OpenApiOperation operation, OperationFilterContext context, string status, Type type, string description)
    {
        operation.Responses[status] = new OpenApiResponse
        {
            Description = description,
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/json"] = new()
                {
                    Schema = context.SchemaGenerator.GenerateSchema(type, context.SchemaRepository)
                }
            }
        };
    }

    private static OpenApiObject MetadataExample() => new()
    {
        ["name"] = new OpenApiString("应用图标"),
        ["tags"] = new OpenApiArray { new OpenApiString("工作"), new OpenApiString("Work") }
    };
}
