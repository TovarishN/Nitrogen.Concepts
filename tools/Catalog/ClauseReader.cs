using System.Text;
using Nitrogen;
using NitrogenCatalog.Syntax;

namespace NitrogenCatalog;

/// <summary>Reads a record's clause list by keyword. Missing clauses read as empty; the grammar checks report them.</summary>
sealed class ClauseReader(SyntaxList<SyntaxNode> clauses)
{
    IEnumerable<SyntaxNode> All(string keyword)
    {
        var found = new List<SyntaxNode>();
        foreach (var clause in clauses)
            if (CatalogChecks.Keyword(clause) == keyword) found.Add(clause);
        return found;
    }

    public bool Has(string keyword) => All(keyword).Any();

    /// <summary>The unquoted string of the first such clause, or "".</summary>
    public string Text(string keyword) => OptionalText(keyword) ?? "";

    public string? OptionalText(string keyword) => Texts(keyword).FirstOrDefault();

    public IReadOnlyList<string> Texts(string keyword) =>
        All(keyword).Select(c => Unquote(new StrNode(c.Tree, c.Child(1).Index).Value.ToString())).ToList();

    /// <summary>The token after the keyword (an ID, keyword value, or date) of the first such clause, or null.</summary>
    public string? Word(string keyword) => Words(keyword).FirstOrDefault();

    public IReadOnlyList<string> Words(string keyword) => All(keyword).Select(c => c.Child(1).Text.ToString()).ToList();

    public IReadOnlyList<Parameter> Parameters(string keyword)
    {
        var clause = All(keyword).FirstOrDefault();
        if (clause.IsNull) return [];
        var items = keyword == "inputs"
            ? new InputsClauseNode(clause.Tree, clause.Index).Items
            : new OutputsClauseNode(clause.Tree, clause.Index).Items;
        return Read(items);
    }

    public (IReadOnlyList<Parameter> Inputs, string Result, string Effect) Contract()
    {
        var clause = All("contract").FirstOrDefault();
        if (clause.IsNull) return ([], "", "");
        var contract = new ContractClauseNode(clause.Tree, clause.Index);
        return (Read(contract.Inputs), contract.Result.ToString(), contract.Effect.ToString());
    }

    public (string Status, string Method) Verification()
    {
        var clause = All("verification").FirstOrDefault();
        if (clause.IsNull) return ("", "");
        var verification = new VerificationClauseNode(clause.Tree, clause.Index);
        return (verification.Status.ToString(), Unquote(verification.Method.Value.ToString()));
    }

    public IReadOnlyList<Example> Examples() => All("example").Select(c =>
    {
        var example = new ExampleClauseNode(c.Tree, c.Index);
        return new Example(example.Polarity.ToString(), Unquote(example.Value.Value.ToString()));
    }).ToList();

    public IReadOnlyList<Relation> Relations() => All("relation").Select(c =>
        CapabilityRelationNode.Is(c.Tree, c.Index)
            ? Read(new CapabilityRelationNode(c.Tree, c.Index))
            : Read(new RecordRelationNode(c.Tree, c.Index))).ToList();

    static Relation Read(CapabilityRelationNode r) =>
        new(r.Relation.ToString(), r.Target.ToString(), Unquote(r.Rationale.Value.ToString()));

    static Relation Read(RecordRelationNode r) =>
        new(r.Relation.ToString(), r.Target.ToString(), Unquote(r.Rationale.Value.ToString()));

    static IReadOnlyList<Parameter> Read(SeparatedList<ParamNode> items)
    {
        var parameters = new List<Parameter>();
        for (int i = 0; i < items.Count; i++) parameters.Add(new Parameter(items[i].Name.ToString(), items[i].Type.ToString()));
        return parameters;
    }

    /// <summary>A String token's value: quotes removed, escapes decoded.</summary>
    public static string Unquote(string token)
    {
        var text = new StringBuilder();
        for (int i = 1; i < token.Length - 1; i++)
        {
            char c = token[i];
            if (c != '\\') { text.Append(c); continue; }
            char e = token[++i];
            if (e == 'u') { text.Append((char)Convert.ToInt32(token.Substring(i + 1, 4), 16)); i += 4; continue; }
            text.Append(e switch { 'n' => '\n', 'r' => '\r', 't' => '\t', '0' => '\0', _ => e });
        }
        return text.ToString();
    }
}
