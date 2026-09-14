using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace SyncClipboard.Server.Core.Swagger;

/// <summary>
/// Swagger 过滤器，用于为手动解析的 multipart/form-data 端点添加参数文档
/// </summary>
public class MultipartFormDataOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        // 检查是否是 POST /api/history 端点
        if (context.ApiDescription.RelativePath != "api/history" ||
            context.ApiDescription.HttpMethod?.ToUpper() != "POST")
            return;

        // 新版接口只读，复制为可修改模型并保留已有文档信息。
        var requestBody = operation.RequestBody switch
        {
            null => new OpenApiRequestBody(),
            OpenApiRequestBody body => (OpenApiRequestBody)body.CreateShallowCopy(),
            OpenApiRequestBodyReference { Target: { } target } reference =>
                (OpenApiRequestBody)reference.CopyReferenceAsTargetElementWithOverrides(target),
            _ => throw new InvalidOperationException("Cannot resolve the history upload request body.")
        };
        requestBody.Required = true;
        requestBody.Content ??= new Dictionary<string, OpenApiMediaType>();
        operation.RequestBody = requestBody;

        operation.Parameters ??= [];
        if (!operation.Parameters.Any(parameter => parameter.In == ParameterLocation.Header
            && parameter.Name == HistoryTransferDataHeaders.TransferDataHash))
        {
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = HistoryTransferDataHeaders.TransferDataHash,
                In = ParameterLocation.Header,
                Required = false,
                Description = "Optional SHA-256 of the data part. Only valid when transfer data is present.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String }
            });
        }

        // 确保有 multipart/form-data 内容
        if (!requestBody.Content.TryGetValue("multipart/form-data", out OpenApiMediaType? value))
        {
            value = new OpenApiMediaType();
            requestBody.Content.TryAdd("multipart/form-data", value);
        }

        value.Schema = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Required = new HashSet<string> { "hash", "type" },
            Properties = new Dictionary<string, IOpenApiSchema>
            {
                ["hash"] = context.SchemaGenerator.GenerateSchema(typeof(string), context.SchemaRepository),
                ["type"] = context.SchemaGenerator.GenerateSchema(typeof(ProfileType), context.SchemaRepository),
                ["createTime"] = context.SchemaGenerator.GenerateSchema(typeof(DateTimeOffset), context.SchemaRepository),
                ["lastModified"] = context.SchemaGenerator.GenerateSchema(typeof(DateTimeOffset), context.SchemaRepository),
                ["lastAccessed"] = context.SchemaGenerator.GenerateSchema(typeof(DateTimeOffset), context.SchemaRepository),
                ["starred"] = context.SchemaGenerator.GenerateSchema(typeof(bool), context.SchemaRepository),
                ["pinned"] = context.SchemaGenerator.GenerateSchema(typeof(bool), context.SchemaRepository),
                ["version"] = context.SchemaGenerator.GenerateSchema(typeof(int), context.SchemaRepository),
                ["isDeleted"] = context.SchemaGenerator.GenerateSchema(typeof(bool), context.SchemaRepository),
                ["text"] = context.SchemaGenerator.GenerateSchema(typeof(string), context.SchemaRepository),
                ["size"] = context.SchemaGenerator.GenerateSchema(typeof(long), context.SchemaRepository),
                ["hasData"] = context.SchemaGenerator.GenerateSchema(typeof(bool), context.SchemaRepository),
                ["data"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.String,
                    Format = "binary",
                    Description = "Transfer data stream. Must be the last part in the multipart/form-data.",
                },
            }
        };
    }
}
