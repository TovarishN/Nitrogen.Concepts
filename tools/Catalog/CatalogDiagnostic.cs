using Nitrogen;

namespace NitrogenCatalog;

/// <summary>One validation finding, printed as <c>path:line:col: CODE message</c>.</summary>
public sealed record CatalogDiagnostic(string Path, int Line, int Column, string Code, string Message)
{
    public static CatalogDiagnostic At(LoadedFile file, TextSpan span, string code, string message)
    {
        var (line, column) = file.Source.GetLineColumn(span.Start);
        return new CatalogDiagnostic(file.Path, line, column, code, message);
    }

    public static CatalogDiagnostic ForFile(string path, string code, string message) => new(path, 1, 1, code, message);

    public static IReadOnlyList<CatalogDiagnostic> Sort(IEnumerable<CatalogDiagnostic> diagnostics) =>
        diagnostics.Distinct()
            .OrderBy(d => d.Path, StringComparer.Ordinal).ThenBy(d => d.Line).ThenBy(d => d.Column)
            .ThenBy(d => d.Code, StringComparer.Ordinal).ThenBy(d => d.Message, StringComparer.Ordinal)
            .ToList();

    public override string ToString() => $"{Path}:{Line}:{Column}: {Code} {Message}";
}
