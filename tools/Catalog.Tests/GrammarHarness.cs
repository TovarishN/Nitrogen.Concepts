using Nitrogen;
using Nitrogen.Binding;
using Nitrogen.Semantics;

namespace NitrogenCatalog.Tests;

/// <summary>Parses in-memory records into one project, without the loader.</summary>
sealed class GrammarHarness : IDisposable
{
    readonly List<ParseResult> _parsed = [];
    public Project Project { get; } = new(CatalogLanguage.Instance);
    public ProjectSemantics Semantics { get; }

    public GrammarHarness(params (string Path, string Text)[] files)
    {
        Semantics = new ProjectSemantics(Project);
        foreach (var (path, text) in files)
        {
            var parsed = CatalogLanguage.Instance.Parse(text, CatalogLanguage.Start);
            _parsed.Add(parsed);
            Project.Set(path, parsed.Tree);
        }
    }

    public ParseResult Parsed(int i) => _parsed[i];

    public IReadOnlyList<string> Codes(string path) =>
        Project.Diagnostics(path).Select(d => d.Code).Concat(Semantics[path].Diagnostics().Select(d => d.Code)).ToList();

    public void Dispose()
    {
        foreach (var parsed in _parsed) parsed.Dispose();
    }
}
