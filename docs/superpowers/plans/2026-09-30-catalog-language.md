# Catalog Language Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the catalog's JSON records, JSON Schema, and Python validator with a Nitrogen `.ncat` language and a C# host tool that validates the catalog and generates `index.json`.

**Architecture:** `language/Catalog.ngr` defines one record per file; Nitrogen binding resolves every cross-record ID and grammar checks report missing or repeated clauses. `tools/Catalog` loads all records into one Nitrogen `Project`, reads them into a plain model, applies catalog-wide rules and the `--base` comparison, and writes or checks `index.json`. Nitrogen is consumed as a pinned git submodule.

**Tech Stack:** .NET 10, C# (LangVersion preview), Nitrogen.Runtime + Nitrogen.Generator (submodule), xUnit 2.9, System.Text.Json, GitHub Actions.

**Spec:** [2026-09-30-catalog-language-design.md](../specs/2026-09-30-catalog-language-design.md)

---

## Background for the implementer

- Nitrogen is a .NET language workbench. A `.ngr` grammar is compiled at build time by the `Nitrogen.Generator` source generator into `CatalogModule` (rules), `CatalogKinds` (node kinds), and one typed view per rule (`ConceptNode`, `NameClauseNode`, …). The pattern to copy is `external/Nitrogen/Nitrogen.Geometry/Nitrogen.Geometry.csproj`.
- A field `Name:Rule` becomes a view property; a token field is a `Token` (`.ToString()` gives its text, `.Span` its location). `X*` becomes `SyntaxList<T>`; `(X; ",")*` becomes `SeparatedList<T>` (indexer + `Count`). A list of an alternative rule (`Clauses:ConceptClause*`) is `SyntaxList<SyntaxNode>` whose items are the concrete alternative nodes (there is no wrapper node for pure alternative rules).
- Binding: `declares concept Id export` makes a project-wide symbol; `references capability Target` must resolve to a symbol of that kind. `Project.Diagnostics(path)` reports `NB0001` (unresolved, including wrong kind) and `NB0003` (the same exported name in two files).
- Semantics: a `{ check CODE condition : message at Field; }` block on a rule is evaluated by `ProjectSemantics[path].Diagnostics()`, returning `SemanticDiagnostic(Code, Span, Message)`. Conditions and messages are C# expressions. **Do not put nested string literals inside an interpolated message** (the grammar's C# scanner ends the string at the inner quote); reference static members instead.
- Useful Nitrogen reading: `external/Nitrogen/Nitrogen.Tests/Grammars/Scopes.ngr` (binding), `Typed.ngr` (checks), `Nitrogen.Tests/Semantics/SemanticsTests.cs` (building a `Language`, `Project`, `ProjectSemantics` in tests).

## File structure

| Path | Responsibility |
| --- | --- |
| `external/Nitrogen` | Git submodule, pinned Nitrogen commit. |
| `language/Catalog.ngr` | Record grammar, binding, per-record checks. |
| `language/CatalogChecks.cs` | Clause-list helpers called from grammar checks (also compiled by the language server). |
| `tools/Catalog/Catalog.csproj` | Host tool project; compiles the grammar and helper. |
| `tools/Catalog/CatalogLanguage.cs` | The built `Language`, start rule, extension, record directories. |
| `tools/Catalog/CatalogDiagnostic.cs` | `path:line:col: CODE message` diagnostic and sorting. |
| `tools/Catalog/CatalogLoader.cs` | Enumerate, parse, bind, and collect Nitrogen diagnostics (`LoadedCatalog`, `LoadedFile`). |
| `tools/Catalog/CatalogModel.cs` | Plain record types and `CatalogModel.Read`. |
| `tools/Catalog/ClauseReader.cs` | Reads clause lists into model values; the only code besides `CatalogModel` that touches syntax views. |
| `tools/Catalog/CatalogRules.cs` | `CA0100`, `CA0102`, `CA0103`. |
| `tools/Catalog/IndexBuilder.cs` | Build/serialize `index.json`; `CA0104`. |
| `tools/Catalog/BaseComparison.cs` | `CA0200`, `CA0201`. |
| `tools/Catalog/CatalogValidator.cs` | Orchestrates validate / write-index. |
| `tools/Catalog/CatalogCli.cs`, `Program.cs` | Argument parsing and output. |
| `tools/Catalog.Tests/*` | xUnit tests. |
| `Catalog.slnx` | Solution for the two projects. |
| `nitrogen.json` | Editor language registration. |

---

### Task 1: Submodule and project scaffolding

**Files:**
- Create: `.gitmodules` (via `git submodule add`), `Catalog.slnx`, `tools/Catalog/Catalog.csproj`, `tools/Catalog/Program.cs`, `tools/Catalog.Tests/Catalog.Tests.csproj`, `tools/Catalog.Tests/SmokeTests.cs`
- Modify: `.gitignore`

- [ ] **Step 1: Add the Nitrogen submodule pinned to the reviewed commit**

```bash
git submodule add git@github.com:TovarishN/Nitrogen.git external/Nitrogen
git -C external/Nitrogen checkout 7f337d96521a120f4f15bcfa95526f8f9c612ac8
git add .gitmodules external/Nitrogen
```

Expected: `git submodule status` shows ` 7f337d9… external/Nitrogen`.

- [ ] **Step 2: Create the tool project**

`tools/Catalog/Catalog.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>preview</LangVersion>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <RootNamespace>NitrogenCatalog</RootNamespace>
    <AssemblyName>nitrogen-catalog</AssemblyName>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\external\Nitrogen\Nitrogen.Runtime\Nitrogen.Runtime.csproj" />
    <ProjectReference Include="..\..\external\Nitrogen\Nitrogen.Generator\Nitrogen.Generator.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
  </ItemGroup>
</Project>
```

The grammar item group is added in Task 2, once `language/` exists.

`tools/Catalog/Program.cs`:

```csharp
return 0;
```

- [ ] **Step 3: Create the test project**

`tools/Catalog.Tests/Catalog.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>preview</LangVersion>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <RootNamespace>NitrogenCatalog.Tests</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Catalog\Catalog.csproj" />
    <ProjectReference Include="..\..\external\Nitrogen\Nitrogen.Runtime\Nitrogen.Runtime.csproj" />
  </ItemGroup>
</Project>
```

`tools/Catalog.Tests/SmokeTests.cs`:

```csharp
using Xunit;

namespace NitrogenCatalog.Tests;

public sealed class SmokeTests
{
    [Fact]
    public void Nitrogen_runtime_is_referenced() => Assert.NotNull(typeof(Nitrogen.Language));
}
```

`Catalog.slnx`:

```xml
<Solution>
  <Project Path="tools/Catalog/Catalog.csproj" />
  <Project Path="tools/Catalog.Tests/Catalog.Tests.csproj" />
</Solution>
```

Append to `.gitignore`:

```
bin/
obj/
```

- [ ] **Step 4: Build and test**

Run: `dotnet test Catalog.slnx`
Expected: build succeeds, `Passed: 1`.

- [ ] **Step 5: Commit**

```bash
git add .gitignore Catalog.slnx tools .gitmodules external/Nitrogen
git commit -m "Scaffold catalog tool on a pinned Nitrogen submodule"
```

---

### Task 2: Grammar and binding (verifies the dotted-ID risk)

**Files:**
- Create: `language/Catalog.ngr`, `language/CatalogChecks.cs`, `tools/Catalog/CatalogLanguage.cs`, `tools/Catalog.Tests/GrammarHarness.cs`, `tools/Catalog.Tests/Samples.cs`, `tools/Catalog.Tests/BindingTests.cs`
- Modify: `tools/Catalog/Catalog.csproj`

- [ ] **Step 1: Write the test samples and harness**

`tools/Catalog.Tests/Samples.cs`:

```csharp
namespace NitrogenCatalog.Tests;

/// <summary>Record texts matching the pre-migration Python test fixtures.</summary>
static class Samples
{
    public const string Capability = """
        capability Concurrency.CoalesceInFlight
        {
          name "Coalesce in-flight work";
          definition "Share one pending computation per key.";
          contract (key: K) -> V effect host;
        }
        """;

    public static string Concept(string status = "candidate", string extra = "", bool provides = true) => $$"""
        concept Concurrency.SingleFlight {{status}}
        {
          name "SingleFlight";
          definition "Concurrent requests for one key share one pending computation.";
          inputs (key: K);
          outputs (value: V);
          limit "Cancellation policy must be explicit.";
        {{(provides ? "  provides Concurrency.CoalesceInFlight;" : "")}}
        {{extra}}
        }
        """;

    public static string Evidence(string id = "EV-20260929-failure", string extra = "") => $$"""
        evidence {{id}}
        {
          date 2026-09-29;
          problem "async cache";
          domain "software";
          requests Concurrency.CoalesceInFlight;
          subject Concurrency.SingleFlight;
          match adaptation;
          outcome failed;
          verification failed "concurrency test";
          reason "Cancellation policy did not match.";
          source "local test";
        {{extra}}
        }
        """;
}
```

`tools/Catalog.Tests/GrammarHarness.cs`:

```csharp
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
```

- [ ] **Step 2: Write the failing binding tests**

`tools/Catalog.Tests/BindingTests.cs`:

```csharp
using Nitrogen.Binding;
using Xunit;

namespace NitrogenCatalog.Tests;

public sealed class BindingTests
{
    [Fact]
    public void Seed_records_parse_and_bind_across_files()
    {
        using var harness = new GrammarHarness(("capabilities/c.ncat", Samples.Capability), ("concepts/s.ncat", Samples.Concept()));
        Assert.True(harness.Parsed(0).Success);
        Assert.True(harness.Parsed(1).Success);
        Assert.Empty(harness.Codes("capabilities/c.ncat"));
        Assert.Empty(harness.Codes("concepts/s.ncat"));
    }

    [Fact]
    public void Dotted_id_resolves_as_one_name()
    {
        using var harness = new GrammarHarness(("capabilities/c.ncat", Samples.Capability), ("concepts/s.ncat", Samples.Concept()));
        var reference = Assert.Single(harness.Project["concepts/s.ncat"].References);
        var symbol = Assert.Single(harness.Project.Resolve(reference));
        Assert.Equal(("capability", "Concurrency.CoalesceInFlight", "capabilities/c.ncat"), (symbol.Kind, symbol.Name, symbol.Path));
    }

    [Fact]
    public void Missing_target_is_unresolved()
    {
        using var harness = new GrammarHarness(("concepts/s.ncat",
            Samples.Concept(extra: "  relation requires Missing.Type \"needed\";")));
        Assert.Contains(BindingCodes.Unresolved, harness.Codes("concepts/s.ncat"));
    }

    [Fact]
    public void Provides_targeting_a_concept_is_unresolved()
    {
        using var harness = new GrammarHarness(("capabilities/c.ncat", Samples.Capability),
            ("concepts/s.ncat", Samples.Concept(extra: "  provides Concurrency.SingleFlight;")));
        Assert.Contains(BindingCodes.Unresolved, harness.Codes("concepts/s.ncat"));
    }

    [Fact]
    public void Same_id_in_two_files_is_ambiguous()
    {
        using var harness = new GrammarHarness(("capabilities/c.ncat", Samples.Capability),
            ("concepts/a.ncat", Samples.Concept()), ("concepts/b.ncat", Samples.Concept()));
        Assert.Contains(BindingCodes.AmbiguousExport, harness.Codes("concepts/a.ncat"));
    }

    [Theory]
    [InlineData("concept concurrency.SingleFlight candidate { }")] // ID segment must start upper-case
    [InlineData("concept SingleFlight candidate { }")]             // ID must be qualified
    [InlineData("concept Concurrency.SingleFlight mature { }")]    // unknown status keyword
    [InlineData("evidence EV-2026-x { }")]                         // malformed event ID
    public void Malformed_header_does_not_parse(string text)
    {
        using var harness = new GrammarHarness(("concepts/x.ncat", text));
        Assert.False(harness.Parsed(0).Success);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test Catalog.slnx`
Expected: FAIL to compile — `CatalogLanguage` does not exist.

- [ ] **Step 4: Write the grammar, helper, and language**

`language/Catalog.ngr`:

```
// The Nitrogen semantic catalog: one record per file.
// Design: docs/superpowers/specs/2026-09-30-catalog-language-design.md
syntax module Catalog
{
  symbols { concept capability realization evidence }

  token Upper      = ['A'..'Z'];
  token Digit      = ['0'..'9'];
  token IdPart     = ['a'..'z' 'A'..'Z' '0'..'9'];
  token RecordId   = Upper IdPart* ("." Upper IdPart*)+;
  token EventId    = "EV-" Digit Digit Digit Digit Digit Digit Digit Digit "-" ['a'..'z' '0'..'9']+;
  token Date       = Digit Digit Digit Digit "-" Digit Digit "-" Digit Digit;
  token Identifier = ['a'..'z' 'A'..'Z' '_'] ['a'..'z' 'A'..'Z' '0'..'9' '_']*;
  token TypeName   = ['a'..'z' 'A'..'Z' '_'] ['a'..'z' 'A'..'Z' '0'..'9' '_' '.']*;
  token Hex        = ['0'..'9' 'a'..'f' 'A'..'F'];
  token Escape     = "\\" (['n' 'r' 't' '0' '\\' '"' '\''] / "u" Hex Hex Hex Hex);
  token String     = "\"" (Escape / [^ '"' '\\' '\n'])* "\"";

  syntax Record = Concept / Capability / Realization / Evidence;

  syntax Concept = "concept" Id:RecordId Status:("observation" / "candidate" / "established") "{" Clauses:ConceptClause* "}"
    declares concept Id export;
  syntax ConceptClause = NameClause / DefinitionClause / InputsClause / OutputsClause / ConstraintClause / InvariantClause
    / LimitClause / ProvidesClause / ExampleClause / ReviewedClause / CapabilityRelation / RecordRelation / SupersedesClause;

  syntax Capability = "capability" Id:RecordId "{" Clauses:CapabilityClause* "}"
    declares capability Id export;
  syntax CapabilityClause = NameClause / DefinitionClause / ContractClause / CapabilityRelation / RecordRelation / SupersedesClause;

  syntax Realization = "realization" Id:RecordId Form:("nitrogen-module" / "implementation" / "api" / "composition") "{" Clauses:RealizationClause* "}"
    declares realization Id export;
  syntax RealizationClause = NameClause / DefinitionClause / SourceClause / RevisionClause / RuntimeClause / HostClause
    / ValidationClause / ProvidesClause / CapabilityRelation / RecordRelation / SupersedesClause;

  syntax Evidence = "evidence" Id:EventId "{" Clauses:EvidenceClause* "}"
    declares evidence Id export;
  syntax EvidenceClause = DateClause / ProblemClause / DomainClause / RequestsClause / SubjectClause / MatchClause / OutcomeClause
    / VerificationClause / ExecutionClause / AdaptationClause / ReasonClause / SourceClause / IndependentClause;

  syntax Str              = Value:String;
  syntax Param            = Name:Identifier ":" Type:TypeName;

  syntax NameClause       = "name" Value:Str ";";
  syntax DefinitionClause = "definition" Value:Str ";";
  syntax InputsClause     = "inputs" "(" Items:(Param; ",")* ")" ";";
  syntax OutputsClause    = "outputs" "(" Items:(Param; ",")* ")" ";";
  syntax ContractClause   = "contract" "(" Inputs:(Param; ",")* ")" "->" Result:TypeName "effect" Effect:("pure" / "host") ";";
  syntax ConstraintClause = "constraint" Value:Str ";";
  syntax InvariantClause  = "invariant" Value:Str ";";
  syntax LimitClause      = "limit" Value:Str ";";
  syntax ExampleClause    = "example" Polarity:("positive" / "negative") Value:Str ";";
  syntax ReviewedClause   = "reviewed" ";";
  syntax ProvidesClause   = "provides" Target:RecordId ";" references capability Target;
  syntax SupersedesClause = "supersedes" Target:RecordId ";" references (concept | capability | realization) Target;
  syntax CapabilityRelation = "relation" Relation:("provides" / "requires") Target:RecordId Rationale:Str ";"
    references capability Target;
  syntax RecordRelation   = "relation" Relation:("specializes" / "generalizes" / "composes-with" / "conflicts-with" / "derived-from" / "alternative-to")
    Target:RecordId Rationale:Str ";" references (concept | capability | realization) Target;

  syntax SourceClause     = "source" Value:Str ";";
  syntax RevisionClause   = "revision" Value:Str ";";
  syntax RuntimeClause    = "runtime" Value:Str ";";
  syntax HostClause       = "host" Value:Str ";";
  syntax ValidationClause = "validation" Value:("unverified" / "locally-validated" / "readmitted") ";";

  syntax DateClause         = "date" Value:Date ";";
  syntax ProblemClause      = "problem" Value:Str ";";
  syntax DomainClause       = "domain" Value:Str ";";
  syntax RequestsClause     = "requests" Target:RecordId ";" references capability Target;
  syntax SubjectClause      = "subject" Target:RecordId ";" references (concept | capability | realization) Target;
  syntax MatchClause        = "match" Value:("exact" / "specialization" / "adaptation" / "composition" / "none") ";";
  syntax OutcomeClause      = "outcome" Value:("accepted" / "rejected" / "modified" / "failed" / "observed") ";";
  syntax VerificationClause = "verification" Status:("passed" / "failed" / "not-run") Method:Str ";";
  syntax ExecutionClause    = "execution" Value:("passed" / "failed" / "not-run") ";";
  syntax AdaptationClause   = "adaptation" Value:Str ";";
  syntax ReasonClause       = "reason" Value:Str ";";
  syntax IndependentClause  = "independent" ";";
}
```

`language/CatalogChecks.cs` (empty for now; filled in Task 3 — it must exist because the project compiles it):

```csharp
namespace NitrogenCatalog;

/// <summary>Clause-list checks called from Catalog.ngr. Clauses may appear in any order, so presence
/// and repetition are checked here rather than by the grammar.</summary>
public static class CatalogChecks
{
}
```

Add this item group to `tools/Catalog/Catalog.csproj` before `</Project>`:

```xml
  <ItemGroup>
    <Compile Include="..\..\language\CatalogChecks.cs" Link="language\CatalogChecks.cs" />
    <AdditionalFiles Include="..\..\language\Catalog.ngr" Namespace="NitrogenCatalog.Syntax" />
    <CompilerVisibleItemMetadata Include="AdditionalFiles" MetadataName="Namespace" />
    <CompilerVisibleProperty Include="RootNamespace" />
  </ItemGroup>
```

`tools/Catalog/CatalogLanguage.cs`:

```csharp
using Nitrogen;
using NitrogenCatalog.Syntax;

namespace NitrogenCatalog;

/// <summary>The catalog language built from Catalog.ngr, and where its records live.</summary>
public static class CatalogLanguage
{
    public const string Extension = ".ncat";

    public static readonly IReadOnlyDictionary<string, string> KindOfDirectory = new Dictionary<string, string>
    {
        ["concepts"] = "concept",
        ["capabilities"] = "capability",
        ["realizations"] = "realization",
        ["evidence"] = "evidence",
    };

    public static Language Instance { get; } = new LanguageBuilder().Add(CatalogModule.Instance).Build();

    public static Rule Start => CatalogModule.Record;
}
```

- [ ] **Step 5: Run tests**

Run: `dotnet test Catalog.slnx`
Expected: all `BindingTests` pass.

If `Dotted_id_resolves_as_one_name` fails because the name is split or qualified, stop and report: the spec's first risk is realized. If the grammar fails to compile, read the generator diagnostic (it names the `.ngr` line), fix that line only, and rerun. Record any grammar deviation in the commit message.

- [ ] **Step 6: Commit**

```bash
git add language tools
git commit -m "Add catalog grammar with cross-file binding"
```

---

### Task 3: Per-record grammar checks (verifies the clause-check risk)

**Files:**
- Modify: `language/Catalog.ngr`, `language/CatalogChecks.cs`
- Create: `tools/Catalog.Tests/GrammarCheckTests.cs`

- [ ] **Step 1: Write the failing tests**

`tools/Catalog.Tests/GrammarCheckTests.cs`:

```csharp
using Xunit;

namespace NitrogenCatalog.Tests;

public sealed class GrammarCheckTests
{
    static IReadOnlyList<string> Codes(string path, string text)
    {
        using var harness = new GrammarHarness(("capabilities/c.ncat", Samples.Capability), (path, text));
        return harness.Codes(path);
    }

    [Fact]
    public void Missing_name_is_CA0001()
    {
        var codes = Codes("concepts/s.ncat", Samples.Concept().Replace("  name \"SingleFlight\";", ""));
        Assert.Contains("CA0001", codes);
    }

    [Fact]
    public void Evidence_without_verification_is_CA0001()
    {
        var codes = Codes("evidence/e.ncat", Samples.Evidence().Replace("  verification failed \"concurrency test\";", ""));
        Assert.Contains("CA0001", codes);
    }

    [Fact]
    public void Repeated_name_is_CA0002()
    {
        var codes = Codes("concepts/s.ncat", Samples.Concept(extra: "  name \"Again\";"));
        Assert.Contains("CA0002", codes);
    }

    [Fact]
    public void Realization_without_provides_is_CA0003()
    {
        const string text = """
            realization Concurrency.Impl implementation
            {
              name "Impl"; definition "d"; source "s"; revision "r"; runtime ".NET 10"; validation unverified;
            }
            """;
        Assert.Contains("CA0003", Codes("realizations/r.ncat", text));
    }

    [Fact]
    public void Empty_text_is_CA0004()
    {
        var codes = Codes("concepts/s.ncat", Samples.Concept(extra: "  constraint \"\";"));
        Assert.Contains("CA0004", codes);
    }

    [Fact]
    public void Duplicate_provides_is_CA0005()
    {
        var codes = Codes("concepts/s.ncat", Samples.Concept(extra: "  provides Concurrency.CoalesceInFlight;"));
        Assert.Contains("CA0005", codes);
    }

    [Fact]
    public void Complete_records_have_no_check_diagnostics()
    {
        Assert.Empty(Codes("concepts/s.ncat", Samples.Concept()));
        using var harness = new GrammarHarness(("capabilities/c.ncat", Samples.Capability),
            ("concepts/s.ncat", Samples.Concept()), ("evidence/e.ncat", Samples.Evidence()));
        Assert.Empty(harness.Codes("evidence/e.ncat"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Catalog.slnx --filter GrammarCheckTests`
Expected: FAIL — `Complete_records_have_no_check_diagnostics` passes, the six others fail (no checks yet).

- [ ] **Step 3: Implement the helper**

Replace `language/CatalogChecks.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using Nitrogen;

namespace NitrogenCatalog;

/// <summary>Clause-list checks called from Catalog.ngr. Clauses may appear in any order, so presence
/// and repetition are checked here rather than by the grammar.</summary>
public static class CatalogChecks
{
    public static readonly string[] ConceptRequired = ["name", "definition"];
    public static readonly string[] ConceptSingle = ["name", "definition", "inputs", "outputs", "reviewed"];
    public static readonly string[] CapabilityRequired = ["name", "definition", "contract"];
    public static readonly string[] RealizationRequired = ["name", "definition", "source", "revision", "runtime", "validation"];
    public static readonly string[] EvidenceRequired =
        ["date", "problem", "domain", "requests", "match", "outcome", "verification", "reason", "source"];
    public static readonly string[] EvidenceSingle = [.. EvidenceRequired, "subject", "execution", "adaptation", "independent"];

    static readonly string[] Listed = ["provides", "supersedes", "host"];

    /// <summary>The clause's leading keyword, such as "name" or "relation".</summary>
    public static string Keyword(SyntaxNode clause) => clause.Child(0).Text.ToString();

    public static int Count(SyntaxList<SyntaxNode> clauses, string keyword)
    {
        int count = 0;
        foreach (var clause in clauses)
            if (Keyword(clause) == keyword) count++;
        return count;
    }

    /// <summary>The first required keyword with no clause, or null.</summary>
    public static string? Missing(SyntaxList<SyntaxNode> clauses, string[] keywords) =>
        keywords.FirstOrDefault(keyword => Count(clauses, keyword) == 0);

    /// <summary>The first single-valued keyword with more than one clause, or null.</summary>
    public static string? Repeated(SyntaxList<SyntaxNode> clauses, string[] keywords) =>
        keywords.FirstOrDefault(keyword => Count(clauses, keyword) > 1);

    /// <summary>The first repeated provides, supersedes, or host entry, as "keyword value", or null.</summary>
    public static string? Duplicate(SyntaxList<SyntaxNode> clauses)
    {
        var seen = new HashSet<string>();
        foreach (var clause in clauses)
        {
            string keyword = Keyword(clause);
            if (!Listed.Contains(keyword)) continue;
            string entry = keyword + " " + clause.Child(1).Text.ToString();
            if (!seen.Add(entry)) return entry;
        }
        return null;
    }
}
```

- [ ] **Step 4: Add the checks to the grammar**

In `language/Catalog.ngr`, replace the four record rules and `Str` with:

```
  syntax Concept = "concept" Id:RecordId Status:("observation" / "candidate" / "established") "{" Clauses:ConceptClause* "}"
    declares concept Id export
  {
    check CA0001 CatalogChecks.Missing(Clauses, CatalogChecks.ConceptRequired) is null
      : $"concept is missing '{CatalogChecks.Missing(Clauses, CatalogChecks.ConceptRequired)}'" at Id;
    check CA0002 CatalogChecks.Repeated(Clauses, CatalogChecks.ConceptSingle) is null
      : $"'{CatalogChecks.Repeated(Clauses, CatalogChecks.ConceptSingle)}' appears more than once" at Id;
    check CA0005 CatalogChecks.Duplicate(Clauses) is null
      : $"duplicate '{CatalogChecks.Duplicate(Clauses)}'" at Id;
  }

  syntax Capability = "capability" Id:RecordId "{" Clauses:CapabilityClause* "}"
    declares capability Id export
  {
    check CA0001 CatalogChecks.Missing(Clauses, CatalogChecks.CapabilityRequired) is null
      : $"capability is missing '{CatalogChecks.Missing(Clauses, CatalogChecks.CapabilityRequired)}'" at Id;
    check CA0002 CatalogChecks.Repeated(Clauses, CatalogChecks.CapabilityRequired) is null
      : $"'{CatalogChecks.Repeated(Clauses, CatalogChecks.CapabilityRequired)}' appears more than once" at Id;
    check CA0005 CatalogChecks.Duplicate(Clauses) is null
      : $"duplicate '{CatalogChecks.Duplicate(Clauses)}'" at Id;
  }

  syntax Realization = "realization" Id:RecordId Form:("nitrogen-module" / "implementation" / "api" / "composition") "{" Clauses:RealizationClause* "}"
    declares realization Id export
  {
    check CA0001 CatalogChecks.Missing(Clauses, CatalogChecks.RealizationRequired) is null
      : $"realization is missing '{CatalogChecks.Missing(Clauses, CatalogChecks.RealizationRequired)}'" at Id;
    check CA0002 CatalogChecks.Repeated(Clauses, CatalogChecks.RealizationRequired) is null
      : $"'{CatalogChecks.Repeated(Clauses, CatalogChecks.RealizationRequired)}' appears more than once" at Id;
    check CA0003 CatalogChecks.Count(Clauses, CatalogChecks.ProvidesKeyword) > 0
      : "realization must provide at least one capability" at Id;
    check CA0005 CatalogChecks.Duplicate(Clauses) is null
      : $"duplicate '{CatalogChecks.Duplicate(Clauses)}'" at Id;
  }

  syntax Evidence = "evidence" Id:EventId "{" Clauses:EvidenceClause* "}"
    declares evidence Id export
  {
    check CA0001 CatalogChecks.Missing(Clauses, CatalogChecks.EvidenceRequired) is null
      : $"evidence is missing '{CatalogChecks.Missing(Clauses, CatalogChecks.EvidenceRequired)}'" at Id;
    check CA0002 CatalogChecks.Repeated(Clauses, CatalogChecks.EvidenceSingle) is null
      : $"'{CatalogChecks.Repeated(Clauses, CatalogChecks.EvidenceSingle)}' appears more than once" at Id;
  }

  syntax Str = Value:String
  {
    check CA0004 Value.Text.Length > 2 : "text must not be empty";
  }
```

Add to `CatalogChecks` (so the grammar holds no string literals inside checks):

```csharp
    public const string ProvidesKeyword = "provides";
```

- [ ] **Step 5: Run tests**

Run: `dotnet test Catalog.slnx`
Expected: all tests pass.

If the generator rejects the check syntax, or the helper cannot be called from a check, stop and report: the spec's second risk is realized, and `CA0001`–`CA0005` move to `CatalogRules` (the plan's Task 6 then gains those rules, operating on `ClauseReader`).

- [ ] **Step 6: Commit**

```bash
git add language tools
git commit -m "Check required, repeated, and duplicate clauses in the catalog grammar"
```

---

### Task 4: Diagnostics and loader

**Files:**
- Create: `tools/Catalog/CatalogDiagnostic.cs`, `tools/Catalog/CatalogLoader.cs`, `tools/Catalog.Tests/TempCatalog.cs`, `tools/Catalog.Tests/LoaderTests.cs`

- [ ] **Step 1: Write the temp-catalog fixture**

`tools/Catalog.Tests/TempCatalog.cs`:

```csharp
namespace NitrogenCatalog.Tests;

/// <summary>A catalog in a temporary directory, seeded with the capability and concept samples.</summary>
sealed class TempCatalog : IDisposable
{
    public string Root { get; } = Directory.CreateTempSubdirectory("catalog-").FullName;

    public TempCatalog(bool seed = true)
    {
        if (!seed) return;
        Write("capabilities/coalesce.ncat", Samples.Capability);
        Write("concepts/single-flight.ncat", Samples.Concept());
    }

    public void Write(string path, string text)
    {
        string full = Path.Combine(Root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    public void Delete(string path) => File.Delete(Path.Combine(Root, path));

    public void Dispose() => Directory.Delete(Root, recursive: true);
}
```

- [ ] **Step 2: Write the failing loader tests**

`tools/Catalog.Tests/LoaderTests.cs`:

```csharp
using Xunit;

namespace NitrogenCatalog.Tests;

public sealed class LoaderTests
{
    [Fact]
    public void Seed_loads_without_diagnostics()
    {
        using var temp = new TempCatalog();
        using var catalog = CatalogLoader.Load(temp.Root);
        Assert.Equal(new[] { "capabilities/coalesce.ncat", "concepts/single-flight.ncat" }, catalog.Files.Select(f => f.Path));
        Assert.Empty(catalog.Diagnostics);
    }

    [Fact]
    public void Missing_directories_are_allowed()
    {
        using var temp = new TempCatalog(seed: false);
        using var catalog = CatalogLoader.Load(temp.Root);
        Assert.Empty(catalog.Files);
        Assert.Empty(catalog.Diagnostics);
    }

    [Fact]
    public void Stray_file_is_CA0101()
    {
        using var temp = new TempCatalog();
        temp.Write("concepts/old.json", "{}");
        using var catalog = CatalogLoader.Load(temp.Root);
        var diagnostic = Assert.Single(catalog.Diagnostics);
        Assert.Equal(("concepts/old.json", "CA0101"), (diagnostic.Path, diagnostic.Code));
    }

    [Fact]
    public void Symbolic_link_is_CA0101()
    {
        using var temp = new TempCatalog();
        File.CreateSymbolicLink(Path.Combine(temp.Root, "concepts/link.ncat"), Path.Combine(temp.Root, "concepts/single-flight.ncat"));
        using var catalog = CatalogLoader.Load(temp.Root);
        Assert.Contains(catalog.Diagnostics, d => d.Path == "concepts/link.ncat" && d.Code == "CA0101");
    }

    [Fact]
    public void Binding_diagnostic_has_line_and_column()
    {
        using var temp = new TempCatalog();
        temp.Write("concepts/single-flight.ncat", Samples.Concept(extra: "  relation requires Missing.Type \"needed\";"));
        using var catalog = CatalogLoader.Load(temp.Root);
        var diagnostic = Assert.Single(catalog.Diagnostics);
        Assert.Equal("NB0001", diagnostic.Code);
        Assert.Equal(9, diagnostic.Line);
        Assert.Equal(21, diagnostic.Column);
        Assert.StartsWith("concepts/single-flight.ncat:9:21: NB0001 ", diagnostic.ToString());
    }

    [Fact]
    public void Parse_error_is_reported()
    {
        using var temp = new TempCatalog();
        temp.Write("concepts/bad.ncat", "concept Bad.Thing mature { }");
        using var catalog = CatalogLoader.Load(temp.Root);
        Assert.Contains(catalog.Diagnostics, d => d.Path == "concepts/bad.ncat");
    }
}
```

Line 9 column 21 is the `M` of `Missing.Type` in `Samples.Concept(extra: …)`: line 1 is the header, lines 2–8 are `{` through the `provides` line, line 9 is the `extra` line `  relation requires Missing.Type …`. If the raw-string layout differs, compute the position from the sample text and fix the expected numbers, not the code.

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test Catalog.slnx --filter LoaderTests`
Expected: FAIL to compile — `CatalogLoader` does not exist.

- [ ] **Step 4: Implement diagnostics and loader**

`tools/Catalog/CatalogDiagnostic.cs`:

```csharp
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
```

`tools/Catalog/CatalogLoader.cs`:

```csharp
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
```

- [ ] **Step 5: Run tests**

Run: `dotnet test Catalog.slnx`
Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add tools
git commit -m "Load catalog records into one Nitrogen project with located diagnostics"
```

---

### Task 5: Catalog model

**Files:**
- Create: `tools/Catalog/CatalogModel.cs`, `tools/Catalog/ClauseReader.cs`, `tools/Catalog.Tests/ModelTests.cs`

- [ ] **Step 1: Write the failing tests**

`tools/Catalog.Tests/ModelTests.cs`:

```csharp
using Xunit;

namespace NitrogenCatalog.Tests;

public sealed class ModelTests
{
    static CatalogModel Read(TempCatalog temp)
    {
        using var catalog = CatalogLoader.Load(temp.Root);
        return CatalogModel.Read(catalog);
    }

    [Fact]
    public void Capability_and_concept_fields_are_read()
    {
        using var temp = new TempCatalog();
        var model = Read(temp);
        var capability = Assert.Single(model.Records.OfType<Capability>());
        Assert.Equal(("Concurrency.CoalesceInFlight", "Coalesce in-flight work", "V", "host"),
            (capability.Id, capability.Name, capability.Result, capability.Effect));
        Assert.Equal(new[] { new Parameter("key", "K") }, capability.Inputs);
        var concept = Assert.Single(model.Records.OfType<Concept>());
        Assert.Equal(("candidate", "SingleFlight"), (concept.Status, concept.Name));
        Assert.Equal(new[] { "Concurrency.CoalesceInFlight" }, concept.Provides);
        Assert.Equal(new[] { "Cancellation policy must be explicit." }, concept.Limits);
        Assert.False(concept.Reviewed);
    }

    [Fact]
    public void Relations_examples_and_escapes_are_read()
    {
        using var temp = new TempCatalog();
        temp.Write("concepts/single-flight.ncat", Samples.Concept(extra: """
              relation requires Concurrency.CoalesceInFlight "needs \"it\"";
              example negative "fails";
              reviewed;
            """));
        var concept = Assert.Single(Read(temp).Records.OfType<Concept>());
        Assert.Equal(new[] { new Relation("requires", "Concurrency.CoalesceInFlight", "needs \"it\"") }, concept.Relations);
        Assert.Equal(new[] { new Example("negative", "fails") }, concept.Examples);
        Assert.True(concept.Reviewed);
    }

    [Fact]
    public void Evidence_fields_are_read()
    {
        using var temp = new TempCatalog();
        temp.Write("evidence/failure.ncat", Samples.Evidence(extra: "  independent;"));
        var evidence = Assert.Single(Read(temp).Records.OfType<Evidence>());
        Assert.Equal(("EV-20260929-failure", "2026-09-29", "Concurrency.CoalesceInFlight", "Concurrency.SingleFlight"),
            (evidence.Id, evidence.Date, evidence.Requests, evidence.Subject));
        Assert.Equal(("adaptation", "failed", "failed", "concurrency test"),
            (evidence.Match, evidence.Outcome, evidence.VerificationStatus, evidence.VerificationMethod));
        Assert.True(evidence.Independent);
        Assert.Equal("evidence", evidence.Origin!.File.Directory);
    }

    [Fact]
    public void File_with_parse_errors_is_skipped()
    {
        using var temp = new TempCatalog();
        temp.Write("concepts/bad.ncat", "concept Bad.Thing mature { }");
        Assert.DoesNotContain(Read(temp).Records, r => r.Id == "Bad.Thing");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Catalog.slnx --filter ModelTests`
Expected: FAIL to compile — `CatalogModel` does not exist.

- [ ] **Step 3: Implement the model**

`tools/Catalog/CatalogModel.cs`:

```csharp
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
```

`tools/Catalog/ClauseReader.cs`:

```csharp
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
```

`ClauseReader` uses `SyntaxNode.IsNull` on `default` to detect absence. If a generated view property type differs from what is written here (for example `Items` is `SyntaxList<ParamNode>` rather than `SeparatedList<ParamNode>`), open the generated file under `tools/Catalog/obj/**/generated/**/Catalog*.g.cs` and use its type.

- [ ] **Step 4: Run tests**

Run: `dotnet test Catalog.slnx`
Expected: all tests pass.

- [ ] **Step 5: Commit**

```bash
git add tools
git commit -m "Read catalog records into a plain model"
```

---

### Task 6: Catalog rules

**Files:**
- Create: `tools/Catalog/CatalogRules.cs`, `tools/Catalog.Tests/RulesTests.cs`

- [ ] **Step 1: Write the failing tests**

`tools/Catalog.Tests/RulesTests.cs`:

```csharp
using Xunit;

namespace NitrogenCatalog.Tests;

public sealed class RulesTests
{
    static IReadOnlyList<CatalogDiagnostic> Check(TempCatalog temp)
    {
        using var catalog = CatalogLoader.Load(temp.Root);
        return CatalogRules.Check(CatalogModel.Read(catalog)).ToList();
    }

    const string Examples = """
          example positive "works";
          example negative "fails";
        """;

    [Fact]
    public void Seed_passes() { using var temp = new TempCatalog(); Assert.Empty(Check(temp)); }

    [Fact]
    public void Wrong_record_directory_is_CA0100()
    {
        using var temp = new TempCatalog();
        temp.Delete("concepts/single-flight.ncat");
        temp.Write("capabilities/single-flight.ncat", Samples.Concept());
        var diagnostic = Assert.Single(Check(temp));
        Assert.Equal(("capabilities/single-flight.ncat", "CA0100"), (diagnostic.Path, diagnostic.Code));
        Assert.Contains("wrong directory", diagnostic.Message);
    }

    [Fact]
    public void Impossible_date_is_CA0102()
    {
        using var temp = new TempCatalog();
        temp.Write("evidence/failure.ncat", Samples.Evidence().Replace("2026-09-29", "2026-02-30"));
        Assert.Equal("CA0102", Assert.Single(Check(temp)).Code);
    }

    [Fact]
    public void Established_without_review_or_examples_is_CA0103()
    {
        using var temp = new TempCatalog();
        temp.Write("concepts/single-flight.ncat", Samples.Concept("established"));
        Assert.Contains(Check(temp), d => d.Code == "CA0103" && d.Message.Contains("positive/negative examples"));
    }

    [Fact]
    public void Established_without_independent_evidence_is_CA0103()
    {
        using var temp = new TempCatalog();
        temp.Write("concepts/single-flight.ncat", Samples.Concept("established", Examples + "  reviewed;"));
        var diagnostic = Assert.Single(Check(temp));
        Assert.Equal("CA0103", diagnostic.Code);
        Assert.Contains("independent evidence", diagnostic.Message);
    }

    [Fact]
    public void Established_with_independent_accepted_evidence_passes()
    {
        using var temp = new TempCatalog();
        temp.Write("concepts/single-flight.ncat", Samples.Concept("established", Examples + "  reviewed;"));
        temp.Write("evidence/use.ncat", Samples.Evidence("EV-20260929-use", "  independent;")
            .Replace("outcome failed;", "outcome accepted;").Replace("verification failed", "verification passed"));
        Assert.Empty(Check(temp));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Catalog.slnx --filter RulesTests`
Expected: FAIL to compile — `CatalogRules` does not exist.

- [ ] **Step 3: Implement the rules**

`tools/Catalog/CatalogRules.cs`:

```csharp
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
```

- [ ] **Step 4: Run tests**

Run: `dotnet test Catalog.slnx`
Expected: all tests pass.

- [ ] **Step 5: Commit**

```bash
git add tools
git commit -m "Check record directories, dates, and established concepts"
```

---

### Task 7: Index builder

**Files:**
- Create: `tools/Catalog/IndexBuilder.cs`, `tools/Catalog.Tests/IndexTests.cs`

- [ ] **Step 1: Write the failing tests**

`tools/Catalog.Tests/IndexTests.cs`:

```csharp
using System.Text.Json.Nodes;
using Xunit;

namespace NitrogenCatalog.Tests;

public sealed class IndexTests
{
    static JsonObject Entry(TempCatalog temp)
    {
        using var catalog = CatalogLoader.Load(temp.Root);
        return IndexBuilder.Build(CatalogModel.Read(catalog))["capabilities"]!["Concurrency.CoalesceInFlight"]!.AsObject();
    }

    static string[] Ids(JsonObject entry, string key) => entry[key]!.AsArray().Select(n => n!.GetValue<string>()).ToArray();

    [Fact]
    public void Concept_provider_is_discoverable()
    {
        using var temp = new TempCatalog();
        Assert.Equal(new[] { "Concurrency.SingleFlight" }, Ids(Entry(temp), "concepts"));
    }

    [Fact]
    public void Failed_reuse_remains_in_capability_index()
    {
        using var temp = new TempCatalog();
        temp.Write("evidence/failure.ncat", Samples.Evidence());
        Assert.Equal(new[] { "EV-20260929-failure" }, Ids(Entry(temp), "evidence"));
    }

    [Fact]
    public void Relation_only_provider_is_discoverable()
    {
        using var temp = new TempCatalog();
        temp.Write("concepts/single-flight.ncat", Samples.Concept(provides: false,
            extra: "  relation provides Concurrency.CoalesceInFlight \"supplies the capability\";"));
        Assert.Equal(new[] { "Concurrency.SingleFlight" }, Ids(Entry(temp), "concepts"));
    }

    [Fact]
    public void Requirement_is_indexed_separately_from_provider()
    {
        using var temp = new TempCatalog();
        temp.Write("concepts/single-flight.ncat", Samples.Concept(provides: false,
            extra: "  relation requires Concurrency.CoalesceInFlight \"needs the capability\";"));
        var entry = Entry(temp);
        Assert.Empty(Ids(entry, "concepts"));
        Assert.Equal(new[] { "Concurrency.SingleFlight" }, Ids(entry, "requiredBy"));
    }

    [Fact]
    public void Serialized_index_matches_python_format()
    {
        using var temp = new TempCatalog();
        using var catalog = CatalogLoader.Load(temp.Root);
        const string expected = """
            {
              "schemaVersion": 1,
              "capabilities": {
                "Concurrency.CoalesceInFlight": {
                  "concepts": [
                    "Concurrency.SingleFlight"
                  ],
                  "realizations": [],
                  "requiredBy": [],
                  "evidence": []
                }
              }
            }

            """;
        Assert.Equal(expected.ReplaceLineEndings("\n"), IndexBuilder.Serialize(IndexBuilder.Build(CatalogModel.Read(catalog))));
    }

    [Fact]
    public void Stale_index_is_CA0104()
    {
        using var temp = new TempCatalog();
        temp.Write("index.json", "{\"schemaVersion\": 1, \"capabilities\": {}}\n");
        using var catalog = CatalogLoader.Load(temp.Root);
        var diagnostic = Assert.Single(IndexBuilder.CheckFresh(temp.Root, CatalogModel.Read(catalog)));
        Assert.Equal(("index.json", "CA0104"), (diagnostic.Path, diagnostic.Code));
        Assert.Contains("stale index", diagnostic.Message);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Catalog.slnx --filter IndexTests`
Expected: FAIL to compile — `IndexBuilder` does not exist.

- [ ] **Step 3: Implement the builder**

`tools/Catalog/IndexBuilder.cs`:

```csharp
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
```

- [ ] **Step 4: Run tests**

Run: `dotnet test Catalog.slnx`
Expected: all tests pass. If `Serialized_index_matches_python_format` fails only on empty arrays (`[\n]` vs `[]`), check the actual output and keep the Python form `[]` by writing arrays manually with `Utf8JsonWriter` when empty.

- [ ] **Step 5: Commit**

```bash
git add tools
git commit -m "Build and check the capability index"
```

---

### Task 8: Base comparison

**Files:**
- Create: `tools/Catalog/BaseComparison.cs`, `tools/Catalog.Tests/BaseComparisonTests.cs`

- [ ] **Step 1: Write the failing tests**

`tools/Catalog.Tests/BaseComparisonTests.cs`:

```csharp
using Xunit;

namespace NitrogenCatalog.Tests;

public sealed class BaseComparisonTests
{
    static IReadOnlyList<CatalogDiagnostic> Compare(TempCatalog current, TempCatalog old)
    {
        using var a = CatalogLoader.Load(current.Root);
        using var b = CatalogLoader.Load(old.Root);
        return BaseComparison.Compare(CatalogModel.Read(a), CatalogModel.Read(b)).ToList();
    }

    [Theory]
    [InlineData("observation", "established")]
    [InlineData("established", "candidate")]
    public void Maturity_skip_or_downgrade_is_CA0200(string before, string after)
    {
        using var old = new TempCatalog();
        old.Write("concepts/single-flight.ncat", Samples.Concept(before));
        using var current = new TempCatalog();
        current.Write("concepts/single-flight.ncat", Samples.Concept(after));
        var diagnostic = Assert.Single(Compare(current, old));
        Assert.Equal("CA0200", diagnostic.Code);
        Assert.Contains("maturity skip or downgrade", diagnostic.Message);
    }

    [Fact]
    public void One_step_promotion_passes()
    {
        using var old = new TempCatalog();
        old.Write("concepts/single-flight.ncat", Samples.Concept("observation"));
        using var current = new TempCatalog();
        Assert.Empty(Compare(current, old));
    }

    [Fact]
    public void Changed_evidence_is_CA0201()
    {
        using var old = new TempCatalog();
        old.Write("evidence/failure.ncat", Samples.Evidence());
        using var current = new TempCatalog();
        current.Write("evidence/failure.ncat", Samples.Evidence().Replace("outcome failed;", "outcome accepted;"));
        var diagnostic = Assert.Single(Compare(current, old));
        Assert.Equal(("evidence/failure.ncat", "CA0201"), (diagnostic.Path, diagnostic.Code));
    }

    [Fact]
    public void Removed_evidence_is_CA0201()
    {
        using var old = new TempCatalog();
        old.Write("evidence/failure.ncat", Samples.Evidence());
        using var current = new TempCatalog();
        Assert.Equal("CA0201", Assert.Single(Compare(current, old)).Code);
    }

    [Fact]
    public void Reformatted_or_moved_evidence_passes()
    {
        using var old = new TempCatalog();
        old.Write("evidence/failure.ncat", Samples.Evidence());
        using var current = new TempCatalog();
        current.Write("evidence/renamed.ncat", Samples.Evidence().Replace("\n  ", "\n    "));
        Assert.Empty(Compare(current, old));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Catalog.slnx --filter BaseComparisonTests`
Expected: FAIL to compile — `BaseComparison` does not exist.

- [ ] **Step 3: Implement the comparison**

`tools/Catalog/BaseComparison.cs`:

```csharp
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
```

`CA0201` is located in the base file (the one that changed or disappeared), matching the Python tool.

- [ ] **Step 4: Run tests**

Run: `dotnet test Catalog.slnx`
Expected: all tests pass.

- [ ] **Step 5: Commit**

```bash
git add tools
git commit -m "Compare maturity and evidence against the base catalog"
```

---

### Task 9: Validator and CLI

**Files:**
- Create: `tools/Catalog/CatalogValidator.cs`, `tools/Catalog/CatalogCli.cs`, `tools/Catalog.Tests/ValidatorTests.cs`
- Modify: `tools/Catalog/Program.cs`
- Delete: `tools/Catalog.Tests/SmokeTests.cs`

- [ ] **Step 1: Write the failing tests**

`tools/Catalog.Tests/ValidatorTests.cs`:

```csharp
using Xunit;

namespace NitrogenCatalog.Tests;

public sealed class ValidatorTests
{
    static (int Exit, string Output) Run(string root, params string[] args)
    {
        var output = new StringWriter();
        int exit = CatalogCli.Run(["--root", root, .. args], output);
        return (exit, output.ToString().ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Written_index_validates()
    {
        using var temp = new TempCatalog();
        Assert.Equal(0, Run(temp.Root, "index", "--write").Exit);
        Assert.Equal((0, "Catalog valid\n"), Run(temp.Root, "validate"));
    }

    [Fact]
    public void Missing_index_fails_validation()
    {
        using var temp = new TempCatalog();
        var (exit, output) = Run(temp.Root, "validate");
        Assert.Equal(1, exit);
        Assert.Contains("CA0104", output);
    }

    [Fact]
    public void Index_is_not_checked_while_records_have_errors()
    {
        using var temp = new TempCatalog();
        temp.Write("concepts/duplicate.ncat", Samples.Concept());
        var (exit, output) = Run(temp.Root, "validate");
        Assert.Equal(1, exit);
        Assert.Contains("NB0003", output);
        Assert.DoesNotContain("CA0104", output);
    }

    [Fact]
    public void Index_is_not_written_while_records_have_errors()
    {
        using var temp = new TempCatalog();
        temp.Write("concepts/duplicate.ncat", Samples.Concept());
        Assert.Equal(1, Run(temp.Root, "index", "--write").Exit);
        Assert.False(File.Exists(Path.Combine(temp.Root, "index.json")));
    }

    [Fact]
    public void Base_comparison_runs()
    {
        using var old = new TempCatalog();
        old.Write("concepts/single-flight.ncat", Samples.Concept("observation"));
        using var temp = new TempCatalog();
        temp.Write("concepts/single-flight.ncat", Samples.Concept("established"));
        var (exit, output) = Run(temp.Root, "validate", "--base", old.Root);
        Assert.Equal(1, exit);
        Assert.Contains("CA0200", output);
    }

    [Fact]
    public void Unknown_command_prints_usage()
    {
        using var temp = new TempCatalog();
        var (exit, output) = Run(temp.Root, "frobnicate");
        Assert.Equal(2, exit);
        Assert.StartsWith("usage:", output);
    }

    [Fact]
    public void Repository_catalog_is_valid_and_seed_is_discoverable()
    {
        string root = RepositoryRoot();
        Assert.Equal((0, "Catalog valid\n"), Run(root, "validate"));
        using var catalog = CatalogLoader.Load(root);
        var entry = IndexBuilder.Build(CatalogModel.Read(catalog))["capabilities"]!["Concurrency.CoalesceInFlight"]!;
        Assert.Equal("Concurrency.SingleFlight", entry["concepts"]![0]!.GetValue<string>());
        Assert.Empty(entry["realizations"]!.AsArray());
    }

    static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Catalog.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Catalog.slnx not found");
    }
}
```

`Repository_catalog_is_valid_and_seed_is_discoverable` fails until Task 10 migrates the records; that is expected here.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Catalog.slnx --filter ValidatorTests`
Expected: FAIL to compile — `CatalogCli` does not exist.

- [ ] **Step 3: Implement validator and CLI**

`tools/Catalog/CatalogValidator.cs`:

```csharp
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
```

`tools/Catalog/CatalogCli.cs`:

```csharp
namespace NitrogenCatalog;

public static class CatalogCli
{
    const string Usage = "usage: nitrogen-catalog [--root PATH] (validate [--base PATH] | index --write)";

    public static int Run(string[] args, TextWriter output)
    {
        string root = Directory.GetCurrentDirectory();
        string? baseRoot = null;
        var words = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--root" && i + 1 < args.Length) root = args[++i];
            else if (args[i] == "--base" && i + 1 < args.Length) baseRoot = args[++i];
            else words.Add(args[i]);
        }

        IReadOnlyList<CatalogDiagnostic> diagnostics;
        if (words is ["validate"]) diagnostics = CatalogValidator.Validate(root, baseRoot);
        else if (words is ["index", "--write"] && baseRoot is null) diagnostics = CatalogValidator.WriteIndex(root);
        else
        {
            output.WriteLine(Usage);
            return 2;
        }

        foreach (var diagnostic in diagnostics) output.WriteLine(diagnostic);
        if (diagnostics.Count > 0) return 1;
        output.WriteLine(words[0] == "validate" ? "Catalog valid" : "Index written");
        return 0;
    }
}
```

`tools/Catalog/Program.cs`:

```csharp
return NitrogenCatalog.CatalogCli.Run(args, Console.Out);
```

Delete `tools/Catalog.Tests/SmokeTests.cs`.

- [ ] **Step 4: Run tests**

Run: `dotnet test Catalog.slnx`
Expected: everything passes except `Repository_catalog_is_valid_and_seed_is_discoverable` (repository still has JSON records, so `CA0101`).

- [ ] **Step 5: Commit**

```bash
git add tools
git commit -m "Add validate and index commands"
```

---

### Task 10: Migrate records, remove Python, update docs, CI, and editor config

**Files:**
- Create: `capabilities/Concurrency.CoalesceInFlight.ncat`, `concepts/Concurrency.SingleFlight.ncat`, `nitrogen.json`
- Delete: `capabilities/Concurrency.CoalesceInFlight.json`, `concepts/Concurrency.SingleFlight.json`, `schemas/`, `tools/validate_catalog.py`, `tests/`, `requirements-dev.txt`
- Modify: `.github/workflows/validate.yml`, `README.md`, `.gitignore`

- [ ] **Step 1: Convert the seed records**

`capabilities/Concurrency.CoalesceInFlight.ncat`:

```
capability Concurrency.CoalesceInFlight
{
  name "Coalesce in-flight work";
  definition "Share one pending computation among concurrent requests with an equal key.";
  contract (key: K) -> V effect host;
}
```

`concepts/Concurrency.SingleFlight.ncat`:

```
concept Concurrency.SingleFlight candidate
{
  name "SingleFlight";
  definition "Concurrent requests for one key share one pending computation.";
  inputs (key: K);
  outputs (value: V);
  constraint "The key has stable equality for the lifetime of the pending computation.";
  invariant "At most one computation for a key is in flight at a time.";
  limit "Cancellation and failure propagation need an explicit policy.";
  provides Concurrency.CoalesceInFlight;
}
```

```bash
git rm capabilities/Concurrency.CoalesceInFlight.json concepts/Concurrency.SingleFlight.json
git rm -r schemas tests tools/validate_catalog.py requirements-dev.txt
```

- [ ] **Step 2: Check the index is unchanged**

```bash
cp index.json "$TMPDIR/index.before.json"
dotnet run --project tools/Catalog -- index --write
diff "$TMPDIR/index.before.json" index.json && echo IDENTICAL
```

Expected: `Index written`, then `IDENTICAL`.

- [ ] **Step 3: Run the full suite and validation**

Run: `dotnet test Catalog.slnx && dotnet run --project tools/Catalog -- validate`
Expected: all tests pass (including `Repository_catalog_is_valid_and_seed_is_discoverable`); last line `Catalog valid`.

- [ ] **Step 4: Break a record on purpose and check the diagnostic**

```bash
sed -i.bak 's/provides Concurrency.CoalesceInFlight;/provides Concurrency.Missing;/' concepts/Concurrency.SingleFlight.ncat
dotnet run --project tools/Catalog -- validate; echo "exit $?"
mv concepts/Concurrency.SingleFlight.ncat.bak concepts/Concurrency.SingleFlight.ncat
```

Expected: `concepts/Concurrency.SingleFlight.ncat:10:12: NB0001 unresolved capability 'Concurrency.Missing'` and `exit 1`.

- [ ] **Step 5: Editor registration**

`nitrogen.json`:

```json
{
  "languages": [
    {
      "name": "catalog",
      "extensions": [".ncat"],
      "start": "Catalog.Record",
      "grammars": ["language/Catalog.ngr"],
      "sources": ["language/CatalogChecks.cs"],
      "usings": ["NitrogenCatalog"]
    }
  ]
}
```

Check it by opening `concepts/Concurrency.SingleFlight.ncat` in VS Code with the Nitrogen extension: no diagnostics; go to definition on `Concurrency.CoalesceInFlight` opens the capability. If the start-rule format is rejected, compare with `external/Nitrogen/Nitrogen.Tests/LanguageService/GrammarLoopTests.cs` (`Config`) and fix the field.

- [ ] **Step 6: Replace CI**

`.github/workflows/validate.yml`:

```yaml
name: Validate catalog

on:
  push:
  pull_request:

jobs:
  catalog:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v7
      - name: Fetch Nitrogen submodule
        env:
          NITROGEN_DEPLOY_KEY: ${{ secrets.NITROGEN_DEPLOY_KEY }}
        run: |
          mkdir -p ~/.ssh
          printf '%s\n' "$NITROGEN_DEPLOY_KEY" > ~/.ssh/nitrogen
          chmod 600 ~/.ssh/nitrogen
          GIT_SSH_COMMAND="ssh -i ~/.ssh/nitrogen -o StrictHostKeyChecking=accept-new" git submodule update --init
      - uses: actions/setup-dotnet@v6
        with:
          dotnet-version: 10.0.x
      - run: dotnet test Catalog.slnx
      - run: dotnet run --project tools/Catalog -- validate
      - uses: actions/checkout@v7
        if: github.event_name == 'pull_request'
        with:
          ref: ${{ github.event.pull_request.base.sha }}
          path: .catalog-base
      - run: dotnet run --project tools/Catalog -- validate --base .catalog-base
        if: github.event_name == 'pull_request'
```

The base checkout does not need submodules; it is only read as records. While the base branch still has JSON records (the migration PR itself), the `--base` step reports `base:` `CA0101` diagnostics; that one run is expected to fail and is noted in the PR.

`NITROGEN_DEPLOY_KEY` is the private half of a read-only deploy key on `TovarishN/Nitrogen` (used only for the submodule fetch, so the catalog checkout keeps its default token), added as a secret on `TovarishN/Nitrogen.Concepts`. Setting it up is the repository owner's action; list it in the PR description.

- [ ] **Step 7: Update `.gitignore` and README**

`.gitignore` becomes:

```
bin/
obj/
.catalog-base/
```

In `README.md`, replace the setup block under "Read the catalog" with:

```sh
git clone --recurse-submodules git@github.com:TovarishN/Nitrogen.Concepts.git
cd Nitrogen.Concepts
dotnet run --project tools/Catalog -- validate
```

Add after "Record lifecycle" a section:

````markdown
## Record syntax

Records are `.ncat` files in the catalog language defined by `language/Catalog.ngr`. Each file holds one record; clauses may appear in any order.

```
concept Concurrency.SingleFlight candidate
{
  name "SingleFlight";
  definition "Concurrent requests for one key share one pending computation.";
  inputs (key: K);
  outputs (value: V);
  limit "Cancellation and failure propagation need an explicit policy.";
  provides Concurrency.CoalesceInFlight;
  relation specializes Some.Concept "why the relation holds";
}
```

The validator reports Nitrogen parse (`Expected`, …), binding (`NB0001` unresolved or wrong-kind target, `NB0003` duplicate ID), and catalog (`CA0001`–`CA0201`) diagnostics as `path:line:col: CODE message`. With the Nitrogen VS Code extension, `nitrogen.json` gives the same parse and binding diagnostics, go to definition, and rename in the editor.
````

In "Propose a catalog change" step 3, replace the commands with:

```
Run `dotnet run --project tools/Catalog -- index --write`, then `dotnet test Catalog.slnx` and `dotnet run --project tools/Catalog -- validate`. Compare changed maturity against the base checkout with `validate --base PATH`.
```

Replace "the JSON Schema" wording anywhere it appears, and change "open its concept, realization, and evidence records" to refer to `.ncat` records.

- [ ] **Step 8: Final verification**

Run: `git status --short && dotnet test Catalog.slnx && dotnet run --project tools/Catalog -- validate`
Expected: only intended changes listed; all tests pass; `Catalog valid`.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "Migrate catalog records to the Nitrogen catalog language"
```

---

### Task 11: Follow-ups outside this repository (report, do not implement here)

- [ ] **Step 1:** Draft (do not publish without the user's go-ahead) a Nitrogen PR updating `.agents/skills/nitrogen/references/catalog.md`: records are `.ncat`, show the syntax example, and replace the Python commands with `dotnet run --project tools/Catalog -- validate` / `index --write`. Then copy the updated skill into the installed skill directories as the Nitrogen README describes.
- [ ] **Step 2:** Tell the user the `NITROGEN_DEPLOY_KEY` secret must exist before CI can pass.
