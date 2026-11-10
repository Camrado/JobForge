using JobForge.Core.Dtos;
using JobForge.Core.Models;

namespace JobForge.Core.Report;

public sealed class ReportAccumulator
{
    public const int MaxSamples = 20;

    private readonly Dictionary<ValidationErrorType, int> _errorCounts =
        Enum.GetValues<ValidationErrorType>().ToDictionary(type => type, _ => 0);

    private readonly PriorityQueue<InvalidLineDto, int> _samples = new();

    public int Total { get; private set; }
    public int Valid { get; private set; }
    public int Invalid { get; private set; }

    public void Add(int lineNumber, IReadOnlyList<ValidationErrorType> errors)
    {
        Total++;

        if (errors.Count == 0)
        {
            Valid++;
            return;
        }

        Invalid++;

        foreach (var errorType in errors)
        {
            _errorCounts[errorType]++;
        }

        OfferSample(new InvalidLineDto(lineNumber, errors));
    }

    public void Merge(ReportAccumulator other)
    {
        Total += other.Total;
        Valid += other.Valid;
        Invalid += other.Invalid;

        foreach (var (errorType, count) in other._errorCounts)
        {
            _errorCounts[errorType] += count;
        }

        foreach (var (sample, _) in other._samples.UnorderedItems)
        {
            OfferSample(sample);
        }
    }

    public ValidationReportDto ToReport()
    {
        var samples = _samples.UnorderedItems
            .Select(item => item.Element)
            .OrderBy(sample => sample.LineNumber)
            .ToList();

        return new ValidationReportDto(Total, Valid, Invalid, new Dictionary<ValidationErrorType, int>(_errorCounts), samples);
    }

    private void OfferSample(InvalidLineDto sample)
    {
        if (_samples.Count < MaxSamples)
        {
            _samples.Enqueue(sample, -sample.LineNumber);
            return;
        }

        var highestKept = _samples.Peek();
        if (sample.LineNumber < highestKept.LineNumber)
        {
            _samples.DequeueEnqueue(sample, -sample.LineNumber);
        }
    }
}
