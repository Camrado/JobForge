namespace JobForge.Api.Models;

public sealed class DatasetCatalog
{
    private readonly Dictionary<string, string> _paths;

    public DatasetCatalog(IConfiguration configuration, IHostEnvironment environment)
    {
        _paths = configuration
            .GetSection("Datasets")
            .GetChildren()
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Value))
            .ToDictionary(
                entry => entry.Key,
                entry => Path.GetFullPath(entry.Value!, environment.ContentRootPath),
                StringComparer.OrdinalIgnoreCase);
    }

    public bool TryGetPath(string datasetId, out string path) => _paths.TryGetValue(datasetId, out path!);
}
