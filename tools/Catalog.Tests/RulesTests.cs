using Xunit;

namespace NitrogenCatalog.Tests;

public sealed class RulesTests
{
    static IReadOnlyList<CatalogDiagnostic> Check(TempCatalog temp)
    {
        using var catalog = CatalogLoader.Load(temp.Root);
        return CatalogRules.Check(CatalogModel.Read(catalog)).ToList();
    }

    const string Examples = """
          example positive "works";
          example negative "fails";
        """;

    [Fact]
    public void Seed_passes() { using var temp = new TempCatalog(); Assert.Empty(Check(temp)); }

    [Fact]
    public void Wrong_record_directory_is_CA0100()
    {
        using var temp = new TempCatalog();
        temp.Delete("concepts/single-flight.ncat");
        temp.Write("capabilities/single-flight.ncat", Samples.Concept());
        var diagnostic = Assert.Single(Check(temp));
        Assert.Equal(("capabilities/single-flight.ncat", "CA0100"), (diagnostic.Path, diagnostic.Code));
        Assert.Contains("wrong directory", diagnostic.Message);
    }

    [Fact]
    public void Impossible_date_is_CA0102()
    {
        using var temp = new TempCatalog();
        temp.Write("evidence/failure.ncat", Samples.Evidence().Replace("2026-09-29", "2026-02-30"));
        Assert.Equal("CA0102", Assert.Single(Check(temp)).Code);
    }

    [Fact]
    public void Established_without_review_or_examples_is_CA0103()
    {
        using var temp = new TempCatalog();
        temp.Write("concepts/single-flight.ncat", Samples.Concept("established"));
        Assert.Contains(Check(temp), d => d.Code == "CA0103" && d.Message.Contains("positive/negative examples"));
    }

    [Fact]
    public void Established_without_independent_evidence_is_CA0103()
    {
        using var temp = new TempCatalog();
        temp.Write("concepts/single-flight.ncat", Samples.Concept("established", Examples + "  reviewed;"));
        var diagnostic = Assert.Single(Check(temp));
        Assert.Equal("CA0103", diagnostic.Code);
        Assert.Contains("independent evidence", diagnostic.Message);
    }

    [Fact]
    public void Established_with_independent_accepted_evidence_passes()
    {
        using var temp = new TempCatalog();
        temp.Write("concepts/single-flight.ncat", Samples.Concept("established", Examples + "  reviewed;"));
        temp.Write("evidence/use.ncat", Samples.Evidence("EV-20260929-use", "  independent;")
            .Replace("outcome failed;", "outcome accepted;").Replace("verification failed", "verification passed"));
        Assert.Empty(Check(temp));
    }
}
