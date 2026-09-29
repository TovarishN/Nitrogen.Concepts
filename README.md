# Nitrogen Semantic Catalog

This private repository records semantic knowledge learned while solving tasks. A **concept** says what something means; a **capability** says what a task needs or a realization provides. **Realizations** identify concrete implementations or Nitrogen modules. **Evidence** records actual reuse attempts, including failures. A catalog match suggests an approach; it does not grant execution authority or prove that an implementation works in a new host.

## Read the catalog

Use `index.json` to find a capability, then open its concept, realization, and evidence records. The index is generated from records and lists failed evidence alongside successes. Search by capability first, inspect constraints and counterexamples, then confirm any realization's exact contract, revision, host requirements, and current validation state.

```sh
git clone git@github.com:TovarishN/Nitrogen.Concepts.git
cd Nitrogen.Concepts
python3 -m venv .venv
.venv/bin/python -m pip install -r requirements-dev.txt
.venv/bin/python tools/validate_catalog.py
```

Set `NITROGEN_CONCEPT_CATALOG` to the absolute checkout path when using the cross-project Nitrogen skill. Without this setting, the skill may search a known sibling checkout or fetch through the user's configured GitHub access. Do not infer that a missing or inaccessible catalog is empty.

## Record lifecycle

`observation` records one task; `candidate` states a reusable hypothesis and its limits; `established` requires review, positive and negative examples, and independent accepted reuse or validation evidence. Promotion is an explicit review decision. Routine task PRs add observations, candidates, missing capabilities, and evidence; they do not promote or merge concepts automatically.

`concepts/`, `capabilities/`, `realizations/`, and `evidence/` contain one JSON record per file. IDs are stable and qualified, such as `Concurrency.SingleFlight`. Relationships name existing IDs and explain why they hold. Evidence uses `EV-YYYYMMDD-slug` IDs and records problem class, requested capability, match kind, outcome, verification, and a concise reason. A failed attempt remains visible; do not replace it with a later successful result. Keep task references minimal and remove credentials, private transcripts, and customer data.

The initial `SingleFlight` entry is an illustrative **candidate** with no realization and no claimed task evidence. It is not a built-in Nitrogen type or an executable package.

## Propose a catalog change

1. Finish and verify the main task. Record an actual reuse attempt or observation, with its result and limits.
2. Search existing IDs and relations. Add the smallest new record or append a distinct evidence event. Explain duplicate candidates and any proposed relation in the PR.
3. Run `.venv/bin/python tools/validate_catalog.py --write-index`, then `.venv/bin/python -m unittest discover -s tests -v` and `.venv/bin/python tools/validate_catalog.py`. Compare changed maturity against the base checkout with `--base PATH`.
4. Open a pull request for review. Include the validation result and task artifact reference. Keep the PR unmerged until reviewed. If GitHub is unavailable, keep a local draft and report that publication is pending.

For a Nitrogen module realization, include its source revision or content hash, required runtime and host capabilities, examples and counterexamples, and validation evidence. Re-admit it in the receiving environment before execution. The present Nitrogen admission path assumes reviewed local input; catalog publication does not bypass that boundary.
