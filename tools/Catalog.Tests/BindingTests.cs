using Nitrogen.Binding;
using Xunit;

namespace NitrogenCatalog.Tests;

public sealed class BindingTests
{
    [Fact]
    public void Seed_records_parse_and_bind_across_files()
    {
        using var harness = new GrammarHarness(("capabilities/c.ncat", Samples.Capability), ("concepts/s.ncat", Samples.Concept()));
        Assert.True(harness.Parsed(0).Success);
        Assert.True(harness.Parsed(1).Success);
        Assert.Empty(harness.Codes("capabilities/c.ncat"));
        Assert.Empty(harness.Codes("concepts/s.ncat"));
    }

    [Fact]
    public void Dotted_id_resolves_as_one_name()
    {
        using var harness = new GrammarHarness(("capabilities/c.ncat", Samples.Capability), ("concepts/s.ncat", Samples.Concept()));
        var reference = Assert.Single(harness.Project["concepts/s.ncat"].References);
        var symbol = Assert.Single(harness.Project.Resolve(reference));
        Assert.Equal(("capability", "Concurrency.CoalesceInFlight", "capabilities/c.ncat"), (symbol.Kind, symbol.Name, symbol.Path));
    }

    [Fact]
    public void Missing_target_is_unresolved()
    {
        using var harness = new GrammarHarness(("concepts/s.ncat",
            Samples.Concept(extra: "  relation requires Missing.Type \"needed\";")));
        Assert.Contains(BindingCodes.Unresolved, harness.Codes("concepts/s.ncat"));
    }

    [Fact]
    public void Provides_targeting_a_concept_is_unresolved()
    {
        using var harness = new GrammarHarness(("capabilities/c.ncat", Samples.Capability),
            ("concepts/s.ncat", Samples.Concept(extra: "  provides Concurrency.SingleFlight;")));
        Assert.Contains(BindingCodes.Unresolved, harness.Codes("concepts/s.ncat"));
    }

    [Fact]
    public void Same_id_in_two_files_is_ambiguous()
    {
        using var harness = new GrammarHarness(("capabilities/c.ncat", Samples.Capability),
            ("concepts/a.ncat", Samples.Concept()), ("concepts/b.ncat", Samples.Concept()));
        Assert.Contains(BindingCodes.AmbiguousExport, harness.Codes("concepts/a.ncat"));
    }

    [Theory]
    [InlineData("concept concurrency.SingleFlight candidate { }")] // ID segment must start upper-case
    [InlineData("concept SingleFlight candidate { }")]             // ID must be qualified
    [InlineData("concept Concurrency.SingleFlight mature { }")]    // unknown status keyword
    [InlineData("evidence EV-2026-x { }")]                         // malformed event ID
    public void Malformed_header_does_not_parse(string text)
    {
        using var harness = new GrammarHarness(("concepts/x.ncat", text));
        Assert.False(harness.Parsed(0).Success);
    }
}
