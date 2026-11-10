using System.Text.Json;
using JobForge.Core.Dtos;
using JobForge.Core.Models;

namespace JobForge.Core.Validation.LineValidator;

public sealed class LineValidator : ILineValidator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public IReadOnlyList<ValidationErrorType> Validate(string line)
    {
        OrderLineDto? order;

        try
        {
            order = JsonSerializer.Deserialize<OrderLineDto>(line, JsonOptions);
        }
        catch (JsonException)
        {
            return [ValidationErrorType.MalformedJson];
        }

        if (order is null)
        {
            return [ValidationErrorType.MalformedJson];
        }

        var errors = new List<ValidationErrorType>();

        if (string.IsNullOrWhiteSpace(order.Id))
        {
            errors.Add(ValidationErrorType.InvalidId);
        }

        if (order.Currency is null || !Enum.IsDefined(typeof(SupportedCurrencyType), order.Currency))
        {
            errors.Add(ValidationErrorType.UnsupportedCurrency);
        }

        if (order.Amount is not > 0)
        {
            errors.Add(ValidationErrorType.InvalidAmount);
        }

        return errors;
    }
}
