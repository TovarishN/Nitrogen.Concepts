# Nitrogen Semantic Catalog

This private repository records semantic knowledge learned while solving tasks. A **concept** says what something means; a **capability** says what a task needs or a realization provides. **Realizations** identify concrete implementations or Nitrogen modules. **Evidence** records actual reuse attempts, including failures. A catalog match suggests an approach; it does not grant execution authority or prove that an implementation works in a new host.

## Read the catalog

Use `index.json` to find a capability, then open its concept, realization, and evidence records (`.ncat` files). The index is generated from both top-level `provides` fields and `provides` relations. Its `requiredBy` list names concepts or realizations that depend on the capability; those are not providers. Failed evidence remains listed alongside successes. Search by capability first, inspect constraints and counterexamples, then confirm any realization's exact contract, revision, host requirements, and current validation state.

```sh
git clone --recurse-submodules git@github.com:TovarishN/Nitrogen.Concepts.git
cd Nitrogen.Concepts
dotnet run --project tools/Catalog -- validate
```

The validator needs the .NET 10 SDK and the pinned Nitrogen checkout in `external/Nitrogen`.

Set `NITROGEN_CONCEPT_CATALOG` to the absolute checkout path when using the cross-project Nitrogen skill. Without this setting, the skill may search a known sibling checkout or fetch through the user's configured GitHub access. Do not infer that a missing or inaccessible catalog is empty.

## Record lifecycle

`observation` records one task; `candidate` states a reusable hypothesis and its limits; `established` requires review, positive and negative examples, and independent accepted reuse or validation evidence. Promotion is an explicit review decision. Routine task PRs add observations, candidates, missing capabilities, and evidence; they do not promote or merge concepts automatically.

`concepts/`, `capabilities/`, `realizations/`, and `evidence/` contain one `.ncat` record per file. IDs are stable and qualified, such as `Concurrency.SingleFlight`. Relationships name existing IDs and explain why they hold. Evidence uses `EV-YYYYMMDD-slug` IDs and records problem class, requested capability, match kind, outcome, verification, and a concise reason. A failed attempt remains visible; do not replace it with a later successful result. Keep task references minimal and remove credentials, private transcripts, and customer data.

The initial `SingleFlight` entry is an illustrative **candidate** with no realization and no claimed task evidence. It is not a built-in Nitrogen type or an executable package.

## Record syntax

Records are written in the catalog language defined by `language/Catalog.ngr`. Each file holds one record; clauses may appear in any order.

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

The validator reports Nitrogen parse diagnostics (`Expected`, …), binding diagnostics (`NB0001` for an unresolved or wrong-kind target, `NB0003` for a duplicate ID), and catalog diagnostics (`CA0001`–`CA0201`), each as `path:line:col: CODE message`.

## Editor support

`tools/editors/build.sh` builds editor support from the pinned Nitrogen submodule into `artifacts/`. Both editors run `nitrogen lsp`, which reads `nitrogen.json` and serves parse and binding diagnostics, go to definition, references, and rename for `.ncat` records. The clause checks (`CA0001`–`CA0005`) and catalog rules appear only from the command line and CI.

- **VS Code:** run `tools/editors/build.sh --vscode`, then install `artifacts/nitrogen-0.1.0.vsix` (Extensions → … → Install from VSIX). `.vscode/settings.json` points the extension at `tools/editors/nitrogen`, which runs the server built in `artifacts/nitrogen/`.
- **Rider:** run `tools/editors/build.sh --rider` with Gradle and JDK 25 (`JAVA_HOME`), then install `artifacts/catalog-rider.zip` (Settings → Plugins → ⚙ → Install Plugin from Disk). The plugin registers `.ncat` and bundles a server for the machine that built it.

The Rider generator needs the submodule checked out with LF line endings; the script stops with the fix if `core.autocrlf` or `core.eol=crlf` produced CRLF.

## Propose a catalog change

1. Finish and verify the main task. Record an actual reuse attempt or observation, with its result and limits.
2. Search existing IDs and relations. Add the smallest new record or append a distinct evidence event. Explain duplicate candidates and any proposed relation in the PR.
3. Run `dotnet run --project tools/Catalog -- index --write`, then `dotnet test Catalog.slnx` and `dotnet run --project tools/Catalog -- validate`. Compare changed maturity against the base checkout with `validate --base PATH`.
4. Open a pull request for review. Include the validation result and task artifact reference. Keep the PR unmerged until reviewed. If GitHub is unavailable, keep a local draft and report that publication is pending.

For a Nitrogen module realization, include its source revision or content hash, required runtime and host capabilities, examples and counterexamples, and validation evidence. Re-admit it in the receiving environment before execution. The present Nitrogen admission path assumes reviewed local input; catalog publication does not bypass that boundary.
