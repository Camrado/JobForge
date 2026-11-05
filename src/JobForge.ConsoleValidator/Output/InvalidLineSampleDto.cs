using JobForge.ConsoleValidator.Validation;

namespace JobForge.ConsoleValidator.Output;

public sealed record InvalidLineSampleDto(int LineNumber, IReadOnlyList<ValidationErrorType> Errors);
