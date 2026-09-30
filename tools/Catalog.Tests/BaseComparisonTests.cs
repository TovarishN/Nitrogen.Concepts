using Xunit;

namespace NitrogenCatalog.Tests;

public sealed class BaseComparisonTests
{
    static IReadOnlyList<CatalogDiagnostic> Compare(TempCatalog current, TempCatalog old)
    {
        using var a = CatalogLoader.Load(current.Root);
        using var b = CatalogLoader.Load(old.Root);
        return BaseComparison.Compare(CatalogModel.Read(a), CatalogModel.Read(b)).ToList();
    }

    [Theory]
    [InlineData("observation", "established")]
    [InlineData("established", "candidate")]
    public void Maturity_skip_or_downgrade_is_CA0200(string before, string after)
    {
        using var old = new TempCatalog();
        old.Write("concepts/single-flight.ncat", Samples.Concept(before));
        using var current = new TempCatalog();
        current.Write("concepts/single-flight.ncat", Samples.Concept(after));
        var diagnostic = Assert.Single(Compare(current, old));
        Assert.Equal("CA0200", diagnostic.Code);
        Assert.Contains("maturity skip or downgrade", diagnostic.Message);
    }

    [Fact]
    public void One_step_promotion_passes()
    {
        using var old = new TempCatalog();
        old.Write("concepts/single-flight.ncat", Samples.Concept("observation"));
        using var current = new TempCatalog();
        Assert.Empty(Compare(current, old));
    }

    [Fact]
    public void Changed_evidence_is_CA0201()
    {
        using var old = new TempCatalog();
        old.Write("evidence/failure.ncat", Samples.Evidence());
        using var current = new TempCatalog();
        current.Write("evidence/failure.ncat", Samples.Evidence().Replace("outcome failed;", "outcome accepted;"));
        var diagnostic = Assert.Single(Compare(current, old));
        Assert.Equal(("evidence/failure.ncat", "CA0201"), (diagnostic.Path, diagnostic.Code));
    }

    [Fact]
    public void Removed_evidence_is_CA0201()
    {
        using var old = new TempCatalog();
        old.Write("evidence/failure.ncat", Samples.Evidence());
        using var current = new TempCatalog();
        Assert.Equal("CA0201", Assert.Single(Compare(current, old)).Code);
    }

    [Fact]
    public void Reformatted_or_moved_evidence_passes()
    {
        using var old = new TempCatalog();
        old.Write("evidence/failure.ncat", Samples.Evidence());
        using var current = new TempCatalog();
        current.Write("evidence/renamed.ncat", Samples.Evidence().Replace("\n  ", "\n    "));
        Assert.Empty(Compare(current, old));
    }
}
