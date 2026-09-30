# Catalog language on Nitrogen — design

**Date:** 2026-09-30. **Status:** draft for review.

## Purpose

Replace the catalog's JSON records, JSON Schema, and Python validator with a small Nitrogen language. Records become source files in a `.ncat` language whose grammar enforces shape, whose binding resolves every cross-record ID, and whose checks report source-located diagnostics. A C# host tool validates the whole catalog, applies the rules that span records or versions, and generates `index.json`.

This is the catalog's first real use of Nitrogen: it replaces hand-written reference resolution with Nitrogen binding and gives authors editor support (go to definition, find references, rename, and a diagnostic at a missing target) through `nitrogen lsp`.

Out of scope: changing Nitrogen itself, mapping contract types (`K`, `V`) to `SemanticType`, checking `nitrogen-module` realizations against real module exports, and HIR lowering. Records have no executable meaning; the language stops at binding and checks.

## What stays the same

- The record model: concept, capability, realization, evidence; their fields, enums, maturity levels, and review rules as documented in the README.
- One record per file under `concepts/`, `capabilities/`, `realizations/`, and `evidence/`.
- `index.json` stays generated JSON with the same shape (`schemaVersion`, `capabilities → {concepts, realizations, requiredBy, evidence}`), so the skill's capability-first search keeps working.
- The PR-time comparison against a base checkout (`--base`): concept maturity may not skip or downgrade, and evidence is append-only.

## Record syntax

Each file holds one record. Prose fields are string literals; enums are keywords; IDs are dotted names. Example conversions of the two seed records:

```
capability Concurrency.CoalesceInFlight
{
  name "Coalesce in-flight work";
  definition "Share one pending computation among concurrent requests with an equal key.";
  contract (key: K) -> V effect host;
}
```

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

Relations and the remaining record kinds:

```
realization Concurrency.SingleFlightDotNet implementation
{
  name "...";  definition "...";
  source "https://...";  revision "abc123";
  runtime ".NET 10";  host "network";  validation locally-validated;
  provides Concurrency.CoalesceInFlight;
  relation specializes Concurrency.SingleFlight "Fixes cancellation to caller-owned tokens.";
  supersedes Concurrency.OldSingleFlight;
}

evidence EV-20260930-loader
{
  date 2026-09-30;  problem "deduplicated loading";  domain "http client";
  requests Concurrency.CoalesceInFlight;  subject Concurrency.SingleFlight;
  match adaptation;  outcome accepted;  independent;
  verification passed "concurrent caller test";  execution passed;
  adaptation "...";  reason "...";  source "PR link";
}
```

Repeated clauses (`constraint`, `invariant`, `limit`, `example positive|negative "..."`, `provides`, `host`, `relation`, `supersedes`) replace JSON arrays. Required clauses are enforced by checks, not by clause order, so clauses may appear in any order. Missing, repeated single-valued, or unknown clauses are diagnostics. `reviewed` is a bare flag on concepts; `independent` on evidence.

## Grammar and binding

One syntax module, `Catalog`, in `language/Catalog.ngr`:

- **Tokens.** `RecordId = Upper IdPart* ("." Upper IdPart*)+` enforces today's ID pattern. `EventId = "EV-" Digit{8} "-" [a-z0-9]+`. `String` with escapes (as in `Nitrogen.ngr`). `Date = Digit{4} "-" Digit{2} "-" Digit{2}`.
- **Symbols.** `symbols { concept capability realization evidence }`. Each record `declares <kind> Name export`, so every record is visible from every other file, and Nitrogen's ambiguous-export diagnostic reports a duplicate ID in two files.
- **References.**
  - `provides X` and `requires X` (clause or relation) → `references capability X`.
  - Other relation kinds, `supersedes`, and `subject` → `references (concept | capability | realization) X`.
  - `requests X` on evidence → `references capability X`.
  - An unresolved or wrong-kind target is Nitrogen's unresolved-reference diagnostic, located at the name.
- **Enums as keywords.** Status, relation kind, form, validation, effect, match, outcome, and verification status are keyword alternatives, so an invalid value is a parse error at the value.

Record IDs are single `RecordId` tokens, not qualified references; Nitrogen treats the whole dotted text as the name. The implementation plan verifies this first with a two-file binding test.

## Checks

Checks are split by what they need to see.

**Per-record checks, in the grammar** (`check CAxxxx … at …`), without C# helpers so the live language server can compile the grammar as-is:

| Code | Rule |
| --- | --- |
| `CA0001` | A required clause is missing (per record kind, matching today's schema). |
| `CA0002` | A single-valued clause is repeated. |
| `CA0003` | A realization provides no capability. |
| `CA0004` | A string field is empty. |
| `CA0005` | A duplicate entry in `provides`, `supersedes`, or `host`. |

**Catalog checks, in the host tool** (they need the whole project or the file system):

| Code | Rule |
| --- | --- |
| `CA0100` | A record's kind does not match its directory. |
| `CA0101` | A file under a record directory is a symbolic link or has another extension. |
| `CA0102` | `date` is not a real calendar date. |
| `CA0103` | An established concept lacks `reviewed`, a positive and a negative example, or accepted, independent, passed evidence with it as `subject`. Found with `Project.ReferencesTo`. |
| `CA0104` | `index.json` is stale. |
| `CA0200` | (`--base`) A concept's maturity skips a level or goes down. |
| `CA0201` | (`--base`) A base evidence record changed or disappeared. |

The tool reports Nitrogen's own parse and binding diagnostics as they are, with their codes. All diagnostics print as `path:line:col: CODE message`, sorted, and a non-empty list exits 1.

## Host tool

A .NET console project `tools/Catalog/Catalog.csproj` (net10.0), which compiles `Catalog.ngr` through the Nitrogen generator exactly as `Nitrogen.Geometry` does.

```
dotnet run --project tools/Catalog -- validate [--base PATH]
dotnet run --project tools/Catalog -- index --write
```

Units, each small enough to test alone:

- **`CatalogLoader`** — enumerates record files, rejects links and stray files, parses each, and adds it to one `Project`.
- **`CatalogModel`** — reads typed syntax views into plain records (`Concept`, `Capability`, `Realization`, `Evidence`) with resolved target IDs. Nothing else reads syntax.
- **`CatalogRules`** — the host checks above, over `CatalogModel` and `Project`.
- **`BaseComparison`** — loads a base checkout with the same loader and applies `CA0200`/`CA0201`. Evidence is compared by its model value, not by text, so reformatting is allowed.
- **`IndexBuilder`** — builds `index.json` from `CatalogModel`, with the same provider, `requiredBy`, and evidence rules as today and deterministic ordering.
- **`Program`** — argument parsing and output only.

The index is written only when there are no diagnostics other than `CA0104`, matching today's behavior.

## Consuming Nitrogen

Nitrogen has no package, so this repository adds it as a git submodule at `external/Nitrogen`, pinned to a commit. The tool project references `Nitrogen.Runtime` and `Nitrogen.Generator` through the submodule. Updating Nitrogen is an explicit submodule bump in a catalog PR.

CI (`.github/workflows/validate.yml`) installs .NET 10, checks out with submodules using a read-only deploy key or token for the private Nitrogen repository (stored as a repository secret), runs the tool's tests, runs `validate`, and on pull requests runs `validate --base` against the base checkout. Python is removed from CI.

## Editor support

A `nitrogen.json` at the repository root declares the `Catalog` language for `*.ncat` with start rule `Catalog.Record`. Opening the repository in VS Code or Rider with the Nitrogen extension gives syntax colouring, diagnostics, go to definition, find references, and rename across records. The host-only checks (`CA01xx`, `CA02xx`) appear only from the CLI and CI.

The Nitrogen roadmap notes that workspace documents bind only while open in the language service. A reference to an unopened record may show as unresolved in the editor. The CLI is the source of truth; if this is disruptive in practice, it is a Nitrogen issue, not a catalog workaround.

## Migration

1. Convert the two seed records to `.ncat` and delete their `.json` files.
2. Port each case in `tests/test_validate_catalog.py` to an xUnit test in `tools/Catalog.Tests`, using small in-memory catalogs, then add grammar-level cases (bad ID token, bad enum keyword, wrong-kind reference, missing required clause).
3. Regenerate `index.json` and check it is identical to the current one.
4. Delete `schemas/`, `tools/validate_catalog.py`, `tests/`, and `requirements-dev.txt`.
5. Update the README (setup, commands, record syntax) and, in a separate Nitrogen PR, the skill's `references/catalog.md` so agents author `.ncat` records and run the new commands.

The change lands as one catalog PR; the skill update follows once it merges.

## Verification

- `dotnet test tools/Catalog.Tests` passes, covering every former Python case plus the grammar cases above.
- `validate` reports `Catalog valid` on the migrated catalog, and `index.json` is byte-identical to the pre-migration file.
- A deliberately broken record (dangling `provides`, duplicate ID in two files, `status established` without evidence) fails with located diagnostics, both from the CLI and, for binding errors, in the editor.
- CI passes on a pull request, including the `--base` run.

## Risks

- **Dotted IDs as names.** If Nitrogen splits or qualifies dotted names, the ID token or reference clauses need adjusting. Verified first.
- **Private submodule in CI.** Needs a secret with read access to `TovarishN/Nitrogen`. Without it, CI cannot build the tool.
- **Clause checks in the grammar.** `CA0001`–`CA0005` need to inspect a record's clause list inside a `check` condition. If that cannot be written without a C# helper, those checks move to `CatalogRules`, and the editor shows only parse and binding diagnostics. Verified alongside the dotted-ID test.
- **Index formatting.** Byte-identical `index.json` means the C# writer must match Python's `json.dumps(indent=2)` output plus a trailing newline.
- **Nitrogen churn.** Grammar or API changes in Nitrogen can break the tool; the pinned submodule contains this to deliberate bumps.
- **Agent authoring.** Agents write JSON reliably; a new syntax needs clear examples in the skill. The validator's located diagnostics are the mitigation.
