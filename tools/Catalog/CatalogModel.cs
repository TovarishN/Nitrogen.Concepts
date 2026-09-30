using Nitrogen;
using NitrogenCatalog.Syntax;

namespace NitrogenCatalog;

public sealed record Origin(LoadedFile File, TextSpan Span);
public sealed record Parameter(string Name, string Type);
public sealed record Relation(string Kind, string Target, string Rationale);
public sealed record Example(string Polarity, string Description);

/// <summary>A record as plain data. <see cref="Origin"/> locates it; clear it to compare content.</summary>
public abstract record CatalogRecord
{
    public required string Id { get; init; }
    public required Origin? Origin { get; init; }
    public abstract string Kind { get; }
    public IReadOnlyList<Relation> Relations { get; init; } = Array.Empty<Relation>();
    public IReadOnlyList<string> Supersedes { get; init; } = Array.Empty<string>();
}

public sealed record Concept : CatalogRecord
{
    public override string Kind => "concept";
    public required string Status { get; init; }
    public required string Name { get; init; }
    public required string Definition { get; init; }
    public required IReadOnlyList<Parameter> Inputs { get; init; }
    public required IReadOnlyList<Parameter> Outputs { get; init; }
    public required IReadOnlyList<string> Constraints { get; init; }
    public required IReadOnlyList<string> Invariants { get; init; }
    public required IReadOnlyList<string> Limits { get; init; }
    public required IReadOnlyList<string> Provides { get; init; }
    public required IReadOnlyList<Example> Examples { get; init; }
    public required bool Reviewed { get; init; }
}

public sealed record Capability : CatalogRecord
{
    public override string Kind => "capability";
    public required string Name { get; init; }
    public required string Definition { get; init; }
    public required IReadOnlyList<Parameter> Inputs { get; init; }
    public required string Result { get; init; }
    public required string Effect { get; init; }
}

public sealed record Realization : CatalogRecord
{
    public override string Kind => "realization";
    public required string Form { get; init; }
    public required string Name { get; init; }
    public required string Definition { get; init; }
    public required string Source { get; init; }
    public required string Revision { get; init; }
    public required string Runtime { get; init; }
    public required IReadOnlyList<string> Hosts { get; init; }
    public required string Validation { get; init; }
    public required IReadOnlyList<string> Provides { get; init; }
}

/// <summary>Evidence has only scalar fields, so record equality (with Origin cleared) compares content.</summary>
public sealed record Evidence : CatalogRecord
{
    public override string Kind => "evidence";
    public required string Date { get; init; }
    public required string Problem { get; init; }
    public required string Domain { get; init; }
    public required string Requests { get; init; }
    public required string? Subject { get; init; }
    public required string Match { get; init; }
    public required string Outcome { get; init; }
    public required string VerificationStatus { get; init; }
    public required string VerificationMethod { get; init; }
    public required string? Execution { get; init; }
    public required string? Adaptation { get; init; }
    public required string Reason { get; init; }
    public required string Source { get; init; }
    public required bool Independent { get; init; }
}

public sealed record CatalogModel(IReadOnlyList<CatalogRecord> Records)
{
    /// <summary>Reads every file that parsed without errors. Call before disposing the catalog.</summary>
    public static CatalogModel Read(LoadedCatalog catalog) =>
        new(catalog.Files.Where(f => !f.Parsed.HasErrors).Select(ReadFile).OfType<CatalogRecord>().ToList());

    static CatalogRecord? ReadFile(LoadedFile file)
    {
        var tree = file.Parsed.Tree;
        for (int i = 0; i < tree.NodeCount; i++)
        {
            if (ConceptNode.Is(tree, i)) return ReadConcept(file, new ConceptNode(tree, i));
            if (CapabilityNode.Is(tree, i)) return ReadCapability(file, new CapabilityNode(tree, i));
            if (RealizationNode.Is(tree, i)) return ReadRealization(file, new RealizationNode(tree, i));
            if (EvidenceNode.Is(tree, i)) return ReadEvidence(file, new EvidenceNode(tree, i));
        }
        return null;
    }

    static Concept ReadConcept(LoadedFile file, ConceptNode node)
    {
        var clauses = new ClauseReader(node.Clauses);
        return new Concept
        {
            Id = node.Id.ToString(), Origin = new Origin(file, node.Id.Span), Status = node.Status.ToString(),
            Name = clauses.Text("name"), Definition = clauses.Text("definition"),
            Inputs = clauses.Parameters("inputs"), Outputs = clauses.Parameters("outputs"),
            Constraints = clauses.Texts("constraint"), Invariants = clauses.Texts("invariant"), Limits = clauses.Texts("limit"),
            Provides = clauses.Words("provides"), Examples = clauses.Examples(), Reviewed = clauses.Has("reviewed"),
            Relations = clauses.Relations(), Supersedes = clauses.Words("supersedes"),
        };
    }

    static Capability ReadCapability(LoadedFile file, CapabilityNode node)
    {
        var clauses = new ClauseReader(node.Clauses);
        var (inputs, result, effect) = clauses.Contract();
        return new Capability
        {
            Id = node.Id.ToString(), Origin = new Origin(file, node.Id.Span),
            Name = clauses.Text("name"), Definition = clauses.Text("definition"),
            Inputs = inputs, Result = result, Effect = effect,
            Relations = clauses.Relations(), Supersedes = clauses.Words("supersedes"),
        };
    }

    static Realization ReadRealization(LoadedFile file, RealizationNode node)
    {
        var clauses = new ClauseReader(node.Clauses);
        return new Realization
        {
            Id = node.Id.ToString(), Origin = new Origin(file, node.Id.Span), Form = node.Form.ToString(),
            Name = clauses.Text("name"), Definition = clauses.Text("definition"),
            Source = clauses.Text("source"), Revision = clauses.Text("revision"), Runtime = clauses.Text("runtime"),
            Hosts = clauses.Texts("host"), Validation = clauses.Word("validation") ?? "",
            Provides = clauses.Words("provides"), Relations = clauses.Relations(), Supersedes = clauses.Words("supersedes"),
        };
    }

    static Evidence ReadEvidence(LoadedFile file, EvidenceNode node)
    {
        var clauses = new ClauseReader(node.Clauses);
        var (status, method) = clauses.Verification();
        return new Evidence
        {
            Id = node.Id.ToString(), Origin = new Origin(file, node.Id.Span),
            Date = clauses.Word("date") ?? "", Problem = clauses.Text("problem"), Domain = clauses.Text("domain"),
            Requests = clauses.Word("requests") ?? "", Subject = clauses.Word("subject"),
            Match = clauses.Word("match") ?? "", Outcome = clauses.Word("outcome") ?? "",
            VerificationStatus = status, VerificationMethod = method,
            Execution = clauses.Word("execution"), Adaptation = clauses.OptionalText("adaptation"),
            Reason = clauses.Text("reason"), Source = clauses.Text("source"), Independent = clauses.Has("independent"),
        };
    }
}
