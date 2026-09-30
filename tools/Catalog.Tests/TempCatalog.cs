namespace NitrogenCatalog.Tests;

/// <summary>A catalog in a temporary directory, seeded with the capability and concept samples.</summary>
sealed class TempCatalog : IDisposable
{
    public string Root { get; } = Directory.CreateTempSubdirectory("catalog-").FullName;

    public TempCatalog(bool seed = true)
    {
        if (!seed) return;
        Write("capabilities/coalesce.ncat", Samples.Capability);
        Write("concepts/single-flight.ncat", Samples.Concept());
    }

    public void Write(string path, string text)
    {
        string full = Path.Combine(Root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    public void Delete(string path) => File.Delete(Path.Combine(Root, path));

    public void Dispose() => Directory.Delete(Root, recursive: true);
}
