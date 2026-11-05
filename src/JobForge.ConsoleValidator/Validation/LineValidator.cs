namespace JobForge.ConsoleValidator.Validation;

public sealed class LineValidator : ILineValidator
{
    public IReadOnlyList<ValidationErrorType> Validate(string line)
    {
        // TODO (1A): deserialize into OrderRecordDto (JsonException => MalformedJson),
        // then collect InvalidId, UnsupportedCurrency (USD/EUR/AZN) and InvalidAmount (> 0).
        throw new NotImplementedException();
    }
}
