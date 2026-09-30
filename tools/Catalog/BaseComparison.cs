namespace NitrogenCatalog;

/// <summary>Pull-request rules against the base checkout (spec: CA0200, CA0201).</summary>
public static class BaseComparison
{
    static readonly Dictionary<string, int> Rank = new() { ["observation"] = 0, ["candidate"] = 1, ["established"] = 2 };

    public static IEnumerable<CatalogDiagnostic> Compare(CatalogModel current, CatalogModel old)
    {
        var oldConcepts = old.Records.OfType<Concept>().ToDictionary(c => c.Id);
        foreach (var concept in current.Records.OfType<Concept>())
        {
            if (!oldConcepts.TryGetValue(concept.Id, out var previous)) continue;
            int difference = Rank[concept.Status] - Rank[previous.Status];
            if (difference is < 0 or > 1)
                yield return CatalogDiagnostic.At(concept.Origin!.File, concept.Origin.Span, "CA0200",
                    $"maturity skip or downgrade from {previous.Status} to {concept.Status}");
        }

        var currentEvidence = current.Records.OfType<Evidence>().ToDictionary(e => e.Id, e => e with { Origin = null });
        foreach (var evidence in old.Records.OfType<Evidence>())
        {
            if (currentEvidence.TryGetValue(evidence.Id, out var now) && now == evidence with { Origin = null }) continue;
            yield return CatalogDiagnostic.At(evidence.Origin!.File, evidence.Origin.Span, "CA0201", "evidence must be append-only");
        }
    }
}
