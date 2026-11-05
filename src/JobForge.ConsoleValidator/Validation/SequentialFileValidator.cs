using JobForge.ConsoleValidator.Output;

namespace JobForge.ConsoleValidator.Validation;

public sealed class SequentialFileValidator(ILineValidator lineValidator) : IFileValidator
{
    public Task<ValidationReportDto> ValidateAsync(string path, CancellationToken cancellationToken)
    {
        // TODO (1A): stream the file line by line, track line numbers,
        // call lineValidator.Validate for each line, and aggregate counts + first 20 samples.
        throw new NotImplementedException();
    }
}
