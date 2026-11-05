namespace JobForge.ConsoleValidator.Validation;

/// <summary>
/// The shape of one NDJSON line after deserialization.
/// Everything is nullable on purpose: a missing field must reach the validator
/// so it can be reported, instead of silently becoming "" or 0.
/// </summary>
public sealed record OrderRecordDto(string? Id, string? Currency, decimal? Amount);
