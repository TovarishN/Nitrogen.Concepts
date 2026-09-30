using System.Collections.Generic;
using System.Linq;
using Nitrogen;
using Nitrogen.Semantics;

namespace NitrogenCatalog;

/// <summary>Clause-list checks called from Catalog.ngr. Clauses may appear in any order, so presence
/// and repetition are checked here rather than by the grammar.</summary>
public static class CatalogChecks
{
    public const string ProvidesKeyword = "provides";

    public static readonly string[] ConceptRequired = ["name", "definition"];
    public static readonly string[] ConceptSingle = ["name", "definition", "inputs", "outputs", "reviewed"];
    public static readonly string[] CapabilityRequired = ["name", "definition", "contract"];
    public static readonly string[] RealizationRequired = ["name", "definition", "source", "revision", "runtime", "validation"];
    public static readonly string[] EvidenceRequired =
        ["date", "problem", "domain", "requests", "match", "outcome", "verification", "reason", "source"];
    public static readonly string[] EvidenceSingle = [.. EvidenceRequired, "subject", "execution", "adaptation", "independent"];

    static readonly string[] Listed = [ProvidesKeyword, "supersedes", "host"];

    /// <summary>The clause's leading keyword, such as "name" or "relation".</summary>
    public static string Keyword(SyntaxNode clause) => clause.Child(0).Text.ToString();

    static string Keyword(SemanticNode clause) => Part(clause, 0);

    static string Part(SemanticNode clause, int child) =>
        clause.Semantics.Tree.GetText(clause.Semantics.Tree.Child(clause.Node, child)).ToString();

    public static int Count(IReadOnlyList<SemanticNode> clauses, string keyword)
    {
        int count = 0;
        foreach (var clause in clauses)
            if (Keyword(clause) == keyword) count++;
        return count;
    }

    /// <summary>The first required keyword with no clause, or null.</summary>
    public static string? Missing(IReadOnlyList<SemanticNode> clauses, string[] keywords) =>
        keywords.FirstOrDefault(keyword => Count(clauses, keyword) == 0);

    /// <summary>The first single-valued keyword with more than one clause, or null.</summary>
    public static string? Repeated(IReadOnlyList<SemanticNode> clauses, string[] keywords) =>
        keywords.FirstOrDefault(keyword => Count(clauses, keyword) > 1);

    /// <summary>The first repeated provides, supersedes, or host entry, as "keyword value", or null.</summary>
    public static string? Duplicate(IReadOnlyList<SemanticNode> clauses)
    {
        var seen = new HashSet<string>();
        foreach (var clause in clauses)
        {
            string keyword = Keyword(clause);
            if (!Listed.Contains(keyword)) continue;
            string entry = keyword + " " + Part(clause, 1);
            if (!seen.Add(entry)) return entry;
        }
        return null;
    }
}
