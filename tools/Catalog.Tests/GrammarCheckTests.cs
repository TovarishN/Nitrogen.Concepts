using Xunit;

namespace NitrogenCatalog.Tests;

public sealed class GrammarCheckTests
{
    static IReadOnlyList<string> Codes(string path, string text)
    {
        using var harness = new GrammarHarness(("capabilities/c.ncat", Samples.Capability), (path, text));
        return harness.Codes(path);
    }

    [Fact]
    public void Missing_name_is_CA0001()
    {
        var codes = Codes("concepts/s.ncat", Samples.Concept().Replace("  name \"SingleFlight\";", ""));
        Assert.Contains("CA0001", codes);
    }

    [Fact]
    public void Evidence_without_verification_is_CA0001()
    {
        var codes = Codes("evidence/e.ncat", Samples.Evidence().Replace("  verification failed \"concurrency test\";", ""));
        Assert.Contains("CA0001", codes);
    }

    [Fact]
    public void Repeated_name_is_CA0002()
    {
        var codes = Codes("concepts/s.ncat", Samples.Concept(extra: "  name \"Again\";"));
        Assert.Contains("CA0002", codes);
    }

    [Fact]
    public void Realization_without_provides_is_CA0003()
    {
        const string text = """
            realization Concurrency.Impl implementation
            {
              name "Impl"; definition "d"; source "s"; revision "r"; runtime ".NET 10"; validation unverified;
            }
            """;
        Assert.Contains("CA0003", Codes("realizations/r.ncat", text));
    }

    [Fact]
    public void Empty_text_is_CA0004()
    {
        var codes = Codes("concepts/s.ncat", Samples.Concept(extra: "  constraint \"\";"));
        Assert.Contains("CA0004", codes);
    }

    [Fact]
    public void Duplicate_provides_is_CA0005()
    {
        var codes = Codes("concepts/s.ncat", Samples.Concept(extra: "  provides Concurrency.CoalesceInFlight;"));
        Assert.Contains("CA0005", codes);
    }

    [Fact]
    public void Complete_records_have_no_check_diagnostics()
    {
        Assert.Empty(Codes("concepts/s.ncat", Samples.Concept()));
        using var harness = new GrammarHarness(("capabilities/c.ncat", Samples.Capability),
            ("concepts/s.ncat", Samples.Concept()), ("evidence/e.ncat", Samples.Evidence()));
        Assert.Empty(harness.Codes("evidence/e.ncat"));
    }
}
