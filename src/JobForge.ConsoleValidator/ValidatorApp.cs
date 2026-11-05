using JobForge.ConsoleValidator.Output;
using JobForge.ConsoleValidator.Validation;
using Microsoft.Extensions.Logging;

namespace JobForge.ConsoleValidator;

public sealed class ValidatorApp(
    IFileValidator fileValidator,
    IReportPrinter reportPrinter,
    ILogger<ValidatorApp> logger)
{
    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length != 1)
        {
            await Console.Error.WriteLineAsync("Usage: dotnet run --project src/JobForge.ConsoleValidator -- <path-to-ndjson-file>");
            return 2;
        }

        var path = args[0];

        try
        {
            var report = await fileValidator.ValidateAsync(path, cancellationToken);
            reportPrinter.Print(report);
            return 0;
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Validation of {Path} was canceled", path);
            return 130;
        }
        // TODO (Checkpoint 2): a file that can't be opened or read must fail the run.
        // Decide which exceptions mean "the file is unusable", catch them here,
        // log them, and return a non-zero exit code instead of printing a report.
    }
}
