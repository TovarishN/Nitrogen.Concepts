namespace NitrogenCatalog;

public static class CatalogValidator
{
    /// <summary>All diagnostics, sorted. The index is checked only when everything else passes.</summary>
    public static IReadOnlyList<CatalogDiagnostic> Validate(string root, string? baseRoot = null)
    {
        using var catalog = CatalogLoader.Load(root);
        var model = CatalogModel.Read(catalog);
        var diagnostics = new List<CatalogDiagnostic>(catalog.Diagnostics);
        diagnostics.AddRange(CatalogRules.Check(model));
        if (baseRoot is not null)
        {
            using var old = CatalogLoader.Load(baseRoot);
            diagnostics.AddRange(old.Diagnostics.Select(d => d with { Path = "base:" + d.Path }));
            diagnostics.AddRange(BaseComparison.Compare(model, CatalogModel.Read(old)));
        }
        if (diagnostics.Count == 0) diagnostics.AddRange(IndexBuilder.CheckFresh(root, model));
        return CatalogDiagnostic.Sort(diagnostics);
    }

    /// <summary>Writes index.json when the records have no diagnostics; returns them otherwise.</summary>
    public static IReadOnlyList<CatalogDiagnostic> WriteIndex(string root)
    {
        using var catalog = CatalogLoader.Load(root);
        var model = CatalogModel.Read(catalog);
        var diagnostics = CatalogDiagnostic.Sort(catalog.Diagnostics.Concat(CatalogRules.Check(model)));
        if (diagnostics.Count == 0)
            File.WriteAllText(Path.Combine(root, IndexBuilder.FileName), IndexBuilder.Serialize(IndexBuilder.Build(model)));
        return diagnostics;
    }
}
