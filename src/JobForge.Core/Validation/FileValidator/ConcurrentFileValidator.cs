using System.Threading.Channels;
using JobForge.Core.Models;
using JobForge.Core.Report;
using JobForge.Core.Validation.LineValidator;

namespace JobForge.Core.Validation.FileValidator;

public sealed class ConcurrentFileValidator(
    ILineValidator lineValidator,
    ConcurrentValidatorOptions? options = null) : IFileValidator
{
    private readonly ConcurrentValidatorOptions _options = options ?? new ConcurrentValidatorOptions();

    public async Task<ValidationReportDto> ValidateAsync(string path, CancellationToken cancellationToken)
    {
        var channel = Channel.CreateBounded<LineRecordDto>(new BoundedChannelOptions(_options.ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = true
        });

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var producerTask = GetProducer(path, channel.Writer, cts);
        var consumerTasks = GetConsumers(_options.WorkerCount, channel.Reader, cts);

        await Task.WhenAll(consumerTasks.Append(producerTask));

        var accumulator = new ReportAccumulator();

        foreach (var consumerTask in consumerTasks)
        {
            var consumerReport = await consumerTask;
            accumulator.Merge(consumerReport);
        }

        return accumulator.ToReport();
    }

    private Task GetProducer(
        string path,
        ChannelWriter<LineRecordDto> writer,
        CancellationTokenSource cts)
    {
        return Task.Run(async () =>
        {
            try
            {
                using var reader = new StreamReader(path);

                var lineNumber = 0;
                while (await reader.ReadLineAsync(cts.Token) is { } line)
                {
                    lineNumber++;

                    var record = new LineRecordDto(lineNumber, line);

                    if (!writer.TryWrite(record))
                    {
                        // Test hook
                        _options.OnProducerBlocked?.Invoke(record);
                        await writer.WriteAsync(record, cts.Token);
                    }
                }

                writer.Complete();
            }
            catch (Exception ex)
            {
                writer.Complete(ex);
                await cts.CancelAsync();
                throw;
            }
        }, cts.Token);
    }

    private Task<ReportAccumulator>[] GetConsumers(
        int workerCount,
        ChannelReader<LineRecordDto> reader,
        CancellationTokenSource cts)
    {
        return Enumerable.Range(1, workerCount).Select(_ => Task.Run(async () =>
        {
            var accumulator = new ReportAccumulator();

            try
            {
                await foreach (var record in reader.ReadAllAsync(cts.Token))
                {
                    // Test hook: lets a test pause this consumer at a wait that sees the stop signal.
                    if (_options.OnRecordTaken is not null)
                    {
                        await _options.OnRecordTaken(record, cts.Token);
                    }

                    accumulator.Add(record.LineNumber, lineValidator.Validate(record.LineText));
                }

                return accumulator;
            }
            catch (Exception)
            {
                await cts.CancelAsync();
                throw;
            }
        }, cts.Token)).ToArray();
    }
}