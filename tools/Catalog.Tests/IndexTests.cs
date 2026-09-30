using System.Text.Json.Nodes;
using Xunit;

namespace NitrogenCatalog.Tests;

public sealed class IndexTests
{
    static JsonObject Entry(TempCatalog temp)
    {
        using var catalog = CatalogLoader.Load(temp.Root);
        return IndexBuilder.Build(CatalogModel.Read(catalog))["capabilities"]!["Concurrency.CoalesceInFlight"]!.AsObject();
    }

    static string[] Ids(JsonObject entry, string key) => entry[key]!.AsArray().Select(n => n!.GetValue<string>()).ToArray();

    [Fact]
    public void Concept_provider_is_discoverable()
    {
        using var temp = new TempCatalog();
        Assert.Equal(new[] { "Concurrency.SingleFlight" }, Ids(Entry(temp), "concepts"));
    }

    [Fact]
    public void Failed_reuse_remains_in_capability_index()
    {
        using var temp = new TempCatalog();
        temp.Write("evidence/failure.ncat", Samples.Evidence());
        Assert.Equal(new[] { "EV-20260929-failure" }, Ids(Entry(temp), "evidence"));
    }

    [Fact]
    public void Relation_only_provider_is_discoverable()
    {
        using var temp = new TempCatalog();
        temp.Write("concepts/single-flight.ncat", Samples.Concept(provides: false,
            extra: "  relation provides Concurrency.CoalesceInFlight \"supplies the capability\";"));
        Assert.Equal(new[] { "Concurrency.SingleFlight" }, Ids(Entry(temp), "concepts"));
    }

    [Fact]
    public void Requirement_is_indexed_separately_from_provider()
    {
        using var temp = new TempCatalog();
        temp.Write("concepts/single-flight.ncat", Samples.Concept(provides: false,
            extra: "  relation requires Concurrency.CoalesceInFlight \"needs the capability\";"));
        var entry = Entry(temp);
        Assert.Empty(Ids(entry, "concepts"));
        Assert.Equal(new[] { "Concurrency.SingleFlight" }, Ids(entry, "requiredBy"));
    }

    [Fact]
    public void Serialized_index_matches_python_format()
    {
        using var temp = new TempCatalog();
        using var catalog = CatalogLoader.Load(temp.Root);
        const string expected = """
            {
              "schemaVersion": 1,
              "capabilities": {
                "Concurrency.CoalesceInFlight": {
                  "concepts": [
                    "Concurrency.SingleFlight"
                  ],
                  "realizations": [],
                  "requiredBy": [],
                  "evidence": []
                }
              }
            }

            """;
        Assert.Equal(expected.ReplaceLineEndings("\n"), IndexBuilder.Serialize(IndexBuilder.Build(CatalogModel.Read(catalog))));
    }

    [Fact]
    public void Stale_index_is_CA0104()
    {
        using var temp = new TempCatalog();
        temp.Write("index.json", "{\"schemaVersion\": 1, \"capabilities\": {}}\n");
        using var catalog = CatalogLoader.Load(temp.Root);
        var diagnostic = Assert.Single(IndexBuilder.CheckFresh(temp.Root, CatalogModel.Read(catalog)));
        Assert.Equal(("index.json", "CA0104"), (diagnostic.Path, diagnostic.Code));
        Assert.Contains("stale index", diagnostic.Message);
    }
}
