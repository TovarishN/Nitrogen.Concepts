using System.Globalization;

namespace NitrogenCatalog;

/// <summary>Rules that need the file system or the whole catalog (spec: CA0100, CA0102, CA0103).</summary>
public static class CatalogRules
{
    public static IEnumerable<CatalogDiagnostic> Check(CatalogModel model)
    {
        var evidence = model.Records.OfType<Evidence>().ToList();
        foreach (var record in model.Records)
        {
            var origin = record.Origin!;
            string expected = CatalogLanguage.KindOfDirectory[origin.File.Directory];
            if (expected != record.Kind)
                yield return At(record, "CA0100", $"wrong directory for {record.Kind}: {origin.File.Directory}/ holds {expected} records");

            if (record is Evidence item && !DateOnly.TryParseExact(item.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                yield return At(record, "CA0102", $"'{item.Date}' is not a calendar date");

            if (record is Concept { Status: "established" } concept)
            {
                var polarities = concept.Examples.Select(e => e.Polarity).ToHashSet();
                if (!concept.Reviewed || !polarities.Contains("positive") || !polarities.Contains("negative"))
                    yield return At(record, "CA0103", "established concept requires reviewed contract and positive/negative examples");
                if (!evidence.Any(e => e.Subject == concept.Id && e.Independent && e.Outcome == "accepted" && e.VerificationStatus == "passed"))
                    yield return At(record, "CA0103", "established concept requires independent evidence");
            }
        }
    }

    static CatalogDiagnostic At(CatalogRecord record, string code, string message) =>
        CatalogDiagnostic.At(record.Origin!.File, record.Origin.Span, code, message);
}
