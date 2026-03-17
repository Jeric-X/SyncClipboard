using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace SyncClipboard.Server.Core.Swagger;

/// <summary>
/// Swagger 过滤器，为 QueryHistory 端点的 form-data 参数显示小驼峰名称
/// </summary>
public class QueryHistoryOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        // 检查是否是 POST /api/history/query 端点
        if (context.ApiDescription.RelativePath != "api/history/query" ||
            context.ApiDescription.HttpMethod?.ToUpper() != "POST")
            return;

        if (operation.RequestBody?.Content.TryGetValue("multipart/form-data", out OpenApiMediaType? value) != true)
            return;

        // 将属性名改为小驼峰
        if (value?.Schema?.Properties != null)
        {
            var newProperties = new Dictionary<string, OpenApiSchema>();
            foreach (var prop in value.Schema.Properties)
            {
                var newKey = ToCamelCase(prop.Key);
                newProperties[newKey] = prop.Value;
            }
            value.Schema.Properties = newProperties;

            // 更新 Required 列表中的属性名
            if (value.Schema.Required != null && value.Schema.Required.Count > 0)
            {
                var newRequired = new HashSet<string>();
                foreach (var req in value.Schema.Required)
                {
                    newRequired.Add(ToCamelCase(req));
                }
                value.Schema.Required = newRequired;
            }
        }
    }

    private static string ToCamelCase(string str)
    {
        if (string.IsNullOrEmpty(str) || char.IsLower(str[0]))
            return str;

        return char.ToLower(str[0]) + str[1..];
    }
}
