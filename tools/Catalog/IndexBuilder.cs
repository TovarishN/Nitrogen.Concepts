using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NitrogenCatalog;

/// <summary>The capability-first index: providers, requirers, and evidence per capability.</summary>
public static class IndexBuilder
{
    public const string FileName = "index.json";

    sealed class Entry
    {
        public SortedSet<string> Concepts { get; } = new(StringComparer.Ordinal);
        public SortedSet<string> Realizations { get; } = new(StringComparer.Ordinal);
        public SortedSet<string> RequiredBy { get; } = new(StringComparer.Ordinal);
        public SortedSet<string> Evidence { get; } = new(StringComparer.Ordinal);
    }

    public static JsonObject Build(CatalogModel model)
    {
        var entries = new SortedDictionary<string, Entry>(StringComparer.Ordinal);
        foreach (var capability in model.Records.OfType<Capability>()) entries[capability.Id] = new Entry();
        foreach (var record in model.Records)
        {
            if (record is Evidence evidence)
            {
                if (entries.TryGetValue(evidence.Requests, out var requested)) requested.Evidence.Add(evidence.Id);
                continue;
            }
            IReadOnlyList<string>? provides = record switch { Concept c => c.Provides, Realization r => r.Provides, _ => null };
            if (provides is null) continue;
            var provided = new HashSet<string>(provides);
            foreach (var relation in record.Relations)
            {
                if (relation.Kind == "provides") provided.Add(relation.Target);
                else if (relation.Kind == "requires" && entries.TryGetValue(relation.Target, out var required)) required.RequiredBy.Add(record.Id);
            }
            foreach (var id in provided)
                if (entries.TryGetValue(id, out var entry)) (record is Concept ? entry.Concepts : entry.Realizations).Add(record.Id);
        }

        var capabilities = new JsonObject();
        foreach (var (id, entry) in entries)
            capabilities[id] = new JsonObject
            {
                ["concepts"] = Array(entry.Concepts),
                ["realizations"] = Array(entry.Realizations),
                ["requiredBy"] = Array(entry.RequiredBy),
                ["evidence"] = Array(entry.Evidence),
            };
        return new JsonObject { ["schemaVersion"] = 1, ["capabilities"] = capabilities };
    }

    /// <summary>Two-space indented JSON with "\n" line endings and a trailing newline, as the Python tool wrote.</summary>
    public static string Serialize(JsonObject index)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, NewLine = "\n" }))
            index.WriteTo(writer);
        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    public static IEnumerable<CatalogDiagnostic> CheckFresh(string root, CatalogModel model)
    {
        string path = Path.Combine(root, FileName);
        if (!File.Exists(path))
            return [CatalogDiagnostic.ForFile(FileName, "CA0104", "missing index; run: index --write")];
        if (File.ReadAllText(path).ReplaceLineEndings("\n") != Serialize(Build(model)))
            return [CatalogDiagnostic.ForFile(FileName, "CA0104", "stale index; run: index --write")];
        return [];
    }

    static JsonArray Array(SortedSet<string> ids) => new(ids.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray());
}
