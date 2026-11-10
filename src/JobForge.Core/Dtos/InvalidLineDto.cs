using JobForge.Core.Models;

namespace JobForge.Core.Dtos;

public sealed record InvalidLineDto(int LineNumber, IReadOnlyList<ValidationErrorType> Errors);
