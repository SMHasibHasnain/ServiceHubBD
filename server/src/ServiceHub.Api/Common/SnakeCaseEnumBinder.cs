using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ServiceHub.Api.Common;

/// <summary>
/// Lets query/route enum values use the same snake_case spelling as JSON
/// (e.g. ?status=under_review), while still accepting PascalCase names.
/// </summary>
public class SnakeCaseEnumBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        var type = Nullable.GetUnderlyingType(context.Metadata.ModelType) ?? context.Metadata.ModelType;
        return type.IsEnum && context.BindingInfo.BindingSource != BindingSource.Body ? new SnakeCaseEnumBinder(type) : null;
    }
}

public class SnakeCaseEnumBinder(Type enumType) : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext ctx)
    {
        var raw = ctx.ValueProvider.GetValue(ctx.ModelName).FirstValue;
        if (string.IsNullOrWhiteSpace(raw)) return Task.CompletedTask;

        var normalized = raw.Replace("_", "").Replace("-", "");
        if (Enum.TryParse(enumType, normalized, ignoreCase: true, out var value) && Enum.IsDefined(enumType, value!))
        {
            ctx.Result = ModelBindingResult.Success(value);
        }
        else
        {
            ctx.ModelState.TryAddModelError(ctx.ModelName, $"'{raw}' is not a valid value.");
            ctx.Result = ModelBindingResult.Failed();
        }
        return Task.CompletedTask;
    }
}
