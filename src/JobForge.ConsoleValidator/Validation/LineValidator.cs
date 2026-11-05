using System.Text.Json;

namespace JobForge.ConsoleValidator.Validation;

public sealed class LineValidator : ILineValidator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public IReadOnlyList<ValidationErrorType> Validate(string line)
    {
        OrderRecordDto? order;

        try
        {
            order = JsonSerializer.Deserialize<OrderRecordDto>(line, JsonOptions);
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
