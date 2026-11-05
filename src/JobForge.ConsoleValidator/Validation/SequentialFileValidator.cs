using JobForge.ConsoleValidator.Output;

namespace JobForge.ConsoleValidator.Validation;

public sealed class SequentialFileValidator(ILineValidator lineValidator) : IFileValidator
{
    private const int MaxSamples = 20;

    public async Task<ValidationReportDto> ValidateAsync(string path, CancellationToken cancellationToken)
    {
        int total = 0, valid = 0, invalid = 0;
        var errorCategoryCounts = new Dictionary<ValidationErrorType, int>
        {
            [ValidationErrorType.MalformedJson] = 0,
            [ValidationErrorType.InvalidAmount] = 0,
            [ValidationErrorType.InvalidId] = 0,
            [ValidationErrorType.UnsupportedCurrency] = 0
        };
        var samples = new List<InvalidLineSampleDto>();

        using var reader = new StreamReader(path);
        string? line;

        var lineNumber = 0;
        while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
        {
            lineNumber++;
            
            var errors = lineValidator.Validate(line);
            
            total++;
            if (errors.Any())
            {
                invalid++;

                if (samples.Count < MaxSamples)
                {
                    samples.Add(new InvalidLineSampleDto(lineNumber, errors));
                }
            }
            else
            {
                valid++;
            }

            foreach (var errorType in errors)
            {
                errorCategoryCounts[errorType]++;
            }
        }

        return new ValidationReportDto(total, valid, invalid, errorCategoryCounts, samples);
    }
}
