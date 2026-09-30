using Xunit;

namespace NitrogenCatalog.Tests;

public sealed class ModelTests
{
    static CatalogModel Read(TempCatalog temp)
    {
        using var catalog = CatalogLoader.Load(temp.Root);
        return CatalogModel.Read(catalog);
    }

    [Fact]
    public void Capability_and_concept_fields_are_read()
    {
        using var temp = new TempCatalog();
        var model = Read(temp);
        var capability = Assert.Single(model.Records.OfType<Capability>());
        Assert.Equal(("Concurrency.CoalesceInFlight", "Coalesce in-flight work", "V", "host"),
            (capability.Id, capability.Name, capability.Result, capability.Effect));
        Assert.Equal(new[] { new Parameter("key", "K") }, capability.Inputs);
        var concept = Assert.Single(model.Records.OfType<Concept>());
        Assert.Equal(("candidate", "SingleFlight"), (concept.Status, concept.Name));
        Assert.Equal(new[] { "Concurrency.CoalesceInFlight" }, concept.Provides);
        Assert.Equal(new[] { "Cancellation policy must be explicit." }, concept.Limits);
        Assert.False(concept.Reviewed);
    }

    [Fact]
    public void Relations_examples_and_escapes_are_read()
    {
        using var temp = new TempCatalog();
        temp.Write("concepts/single-flight.ncat", Samples.Concept(extra: """
              relation requires Concurrency.CoalesceInFlight "needs \"it\"";
              example negative "fails";
              reviewed;
            """));
        var concept = Assert.Single(Read(temp).Records.OfType<Concept>());
        Assert.Equal(new[] { new Relation("requires", "Concurrency.CoalesceInFlight", "needs \"it\"") }, concept.Relations);
        Assert.Equal(new[] { new Example("negative", "fails") }, concept.Examples);
        Assert.True(concept.Reviewed);
    }

    [Fact]
    public void Evidence_fields_are_read()
    {
        using var temp = new TempCatalog();
        temp.Write("evidence/failure.ncat", Samples.Evidence(extra: "  independent;"));
        var evidence = Assert.Single(Read(temp).Records.OfType<Evidence>());
        Assert.Equal(("EV-20260929-failure", "2026-09-29", "Concurrency.CoalesceInFlight", "Concurrency.SingleFlight"),
            (evidence.Id, evidence.Date, evidence.Requests, evidence.Subject));
        Assert.Equal(("adaptation", "failed", "failed", "concurrency test"),
            (evidence.Match, evidence.Outcome, evidence.VerificationStatus, evidence.VerificationMethod));
        Assert.True(evidence.Independent);
        Assert.Equal("evidence", evidence.Origin!.File.Directory);
    }

    [Fact]
    public void File_with_parse_errors_is_skipped()
    {
        using var temp = new TempCatalog();
        temp.Write("concepts/bad.ncat", "concept Bad.Thing mature { }");
        Assert.DoesNotContain(Read(temp).Records, r => r.Id == "Bad.Thing");
    }
}
