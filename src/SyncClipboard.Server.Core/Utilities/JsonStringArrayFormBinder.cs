using System.Text.Json;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace SyncClipboard.Server.Core.Utilities;

public sealed class JsonStringArrayFormBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        var value = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (value == ValueProviderResult.None)
            return Task.CompletedTask;

        bindingContext.ModelState.SetModelValue(bindingContext.ModelName, value);
        if (value.Length != 1)
        {
            bindingContext.ModelState.TryAddModelError(bindingContext.ModelName,
                "Provide a single JSON array field.");
            return Task.CompletedTask;
        }

        try
        {
            var items = JsonSerializer.Deserialize<string[]>(value.FirstValue!);
            bindingContext.Result = ModelBindingResult.Success(items);
        }
        catch (JsonException)
        {
            bindingContext.ModelState.TryAddModelError(bindingContext.ModelName,
                "Expected a JSON array of strings or null.");
        }

        return Task.CompletedTask;
    }
}
