using Xunit;

namespace NitrogenCatalog.Tests;

public sealed class LoaderTests
{
    [Fact]
    public void Seed_loads_without_diagnostics()
    {
        using var temp = new TempCatalog();
        using var catalog = CatalogLoader.Load(temp.Root);
        Assert.Equal(new[] { "capabilities/coalesce.ncat", "concepts/single-flight.ncat" }, catalog.Files.Select(f => f.Path));
        Assert.Empty(catalog.Diagnostics);
    }

    [Fact]
    public void Missing_directories_are_allowed()
    {
        using var temp = new TempCatalog(seed: false);
        using var catalog = CatalogLoader.Load(temp.Root);
        Assert.Empty(catalog.Files);
        Assert.Empty(catalog.Diagnostics);
    }

    [Fact]
    public void Stray_file_is_CA0101()
    {
        using var temp = new TempCatalog();
        temp.Write("concepts/old.json", "{}");
        using var catalog = CatalogLoader.Load(temp.Root);
        var diagnostic = Assert.Single(catalog.Diagnostics);
        Assert.Equal(("concepts/old.json", "CA0101"), (diagnostic.Path, diagnostic.Code));
    }

    [Fact]
    public void Symbolic_link_is_CA0101()
    {
        using var temp = new TempCatalog();
        File.CreateSymbolicLink(Path.Combine(temp.Root, "concepts/link.ncat"), Path.Combine(temp.Root, "concepts/single-flight.ncat"));
        using var catalog = CatalogLoader.Load(temp.Root);
        Assert.Contains(catalog.Diagnostics, d => d.Path == "concepts/link.ncat" && d.Code == "CA0101");
    }

    [Fact]
    public void Binding_diagnostic_has_line_and_column()
    {
        using var temp = new TempCatalog();
        temp.Write("concepts/single-flight.ncat", Samples.Concept(extra: "  relation requires Missing.Type \"needed\";"));
        using var catalog = CatalogLoader.Load(temp.Root);
        var diagnostic = Assert.Single(catalog.Diagnostics);
        Assert.Equal("NB0001", diagnostic.Code);
        Assert.Equal(9, diagnostic.Line);
        Assert.Equal(21, diagnostic.Column);
        Assert.StartsWith("concepts/single-flight.ncat:9:21: NB0001 ", diagnostic.ToString());
    }

    [Fact]
    public void Parse_error_is_reported()
    {
        using var temp = new TempCatalog();
        temp.Write("concepts/bad.ncat", "concept Bad.Thing mature { }");
        using var catalog = CatalogLoader.Load(temp.Root);
        Assert.Contains(catalog.Diagnostics, d => d.Path == "concepts/bad.ncat");
    }
}
