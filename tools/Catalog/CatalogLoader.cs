using Nitrogen;
using Nitrogen.Binding;
using Nitrogen.Semantics;

namespace NitrogenCatalog;

/// <summary>A record file: its catalog-relative path, record directory, text, and parse.</summary>
public sealed record LoadedFile(string Path, string Directory, SourceText Source, ParseResult Parsed);

/// <summary>Every record parsed into one project, with Nitrogen's parse, binding, and check diagnostics.</summary>
public sealed class LoadedCatalog(string root, Project project, ProjectSemantics semantics,
    IReadOnlyList<LoadedFile> files, IReadOnlyList<CatalogDiagnostic> diagnostics) : IDisposable
{
    public string Root { get; } = root;
    public Project Project { get; } = project;
    public ProjectSemantics Semantics { get; } = semantics;
    public IReadOnlyList<LoadedFile> Files { get; } = files;
    public IReadOnlyList<CatalogDiagnostic> Diagnostics { get; } = diagnostics;

    public void Dispose()
    {
        foreach (var file in Files) file.Parsed.Dispose();
    }
}

public static class CatalogLoader
{
    public static LoadedCatalog Load(string root)
    {
        var project = new Project(CatalogLanguage.Instance);
        var files = new List<LoadedFile>();
        var diagnostics = new List<CatalogDiagnostic>();
        foreach (var directory in CatalogLanguage.KindOfDirectory.Keys.Order(StringComparer.Ordinal))
        {
            string full = System.IO.Path.Combine(root, directory);
            if (!System.IO.Directory.Exists(full)) continue;
            foreach (var entry in System.IO.Directory.EnumerateFileSystemEntries(full).Order(StringComparer.Ordinal))
            {
                string path = directory + "/" + System.IO.Path.GetFileName(entry);
                if (new FileInfo(entry).LinkTarget is not null || !File.Exists(entry) ||
                    System.IO.Path.GetExtension(entry) != CatalogLanguage.Extension)
                {
                    diagnostics.Add(CatalogDiagnostic.ForFile(path, "CA0101", "not a catalog record: expected a regular .ncat file"));
                    continue;
                }
                var source = new SourceText(File.ReadAllText(entry));
                var parsed = CatalogLanguage.Instance.Parse(source.Text, CatalogLanguage.Start);
                var file = new LoadedFile(path, directory, source, parsed);
                files.Add(file);
                foreach (ref readonly var d in parsed.Diagnostics)
                    diagnostics.Add(CatalogDiagnostic.At(file, d.Span, d.Code.ToString(), parsed.FormatMessage(d)));
                project.Set(path, parsed.Tree);
            }
        }
        var semantics = new ProjectSemantics(project);
        foreach (var file in files)
        {
            foreach (var d in project.Diagnostics(file.Path)) diagnostics.Add(CatalogDiagnostic.At(file, d.Span, d.Code, d.Message));
            foreach (var d in semantics[file.Path].Diagnostics()) diagnostics.Add(CatalogDiagnostic.At(file, d.Span, d.Code, d.Message));
        }
        return new LoadedCatalog(root, project, semantics, files, CatalogDiagnostic.Sort(diagnostics));
    }
}
