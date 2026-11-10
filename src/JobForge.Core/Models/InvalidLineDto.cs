namespace JobForge.Core.Models;

public sealed record InvalidLineDto(int LineNumber, IReadOnlyList<ValidationErrorType> Errors);
