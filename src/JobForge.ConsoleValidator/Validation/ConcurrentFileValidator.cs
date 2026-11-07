using System.Threading.Channels;
using JobForge.ConsoleValidator.Output;

namespace JobForge.ConsoleValidator.Validation;

public sealed class ConcurrentFileValidator(ILineValidator lineValidator) : IFileValidator
{
    public async Task<ValidationReportDto> ValidateAsync(string path, CancellationToken cancellationToken)
    {
        int channelCapacity = 32;
        int workerCount = 4;

        var channel = Channel.CreateBounded<LineRecordDto>(new BoundedChannelOptions(channelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = true
        });

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var producerTask = GetProducer(path, channel.Writer, cts);
        var consumerTasks = GetConsumers(workerCount, channel.Reader, cts);

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

                    await writer.WriteAsync(new LineRecordDto(lineNumber, line), cts.Token);
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