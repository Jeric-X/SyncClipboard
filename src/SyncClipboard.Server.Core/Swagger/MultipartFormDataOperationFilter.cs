using Microsoft.OpenApi.Models;
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

        // 确保有 RequestBody
        operation.RequestBody ??= new OpenApiRequestBody
        {
            Required = true,
            Content = new Dictionary<string, OpenApiMediaType>()
        };

        // 确保有 multipart/form-data 内容
        if (!operation.RequestBody.Content.TryGetValue("multipart/form-data", out OpenApiMediaType? value))
        {
            value = new OpenApiMediaType();
            operation.RequestBody.Content.TryAdd("multipart/form-data", value);
        }

        value.Schema = new OpenApiSchema
        {
            Type = "object",
            Required = new HashSet<string> { "hash", "type" },
            Properties = new Dictionary<string, OpenApiSchema>
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
                ["data"] = new OpenApiSchema
                {
                    Type = "string",
                    Format = "binary",
                    Description = "Transfer data stream. Must be the last part in the multipart/form-data.",
                },
            }
        };
    }
}
