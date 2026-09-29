#!/usr/bin/env python3
"""Validate semantic records and regenerate the capability-first index."""

import argparse
import json
from pathlib import Path

from jsonschema import Draft202012Validator, FormatChecker


DIRECTORIES = ("concepts", "capabilities", "realizations", "evidence")
DIRECTORY_KIND = {"concepts": "concept", "capabilities": "capability", "realizations": "realization", "evidence": "evidence"}
RANK = {"observation": 0, "candidate": 1, "established": 2}


def _load(root: Path):
    records = []
    errors = []
    for directory in DIRECTORIES:
        for path in sorted((root / directory).glob("*.json")):
            try:
                if path.is_symlink():
                    raise ValueError("symbolic links are not catalog records")
                record = json.loads(path.read_text(encoding="utf-8"))
                records.append((path.relative_to(root).as_posix(), record))
            except (ValueError, OSError, UnicodeError) as exc:
                errors.append(f"{path.relative_to(root)}: {exc}")
    return records, errors


def build_index(root: Path) -> dict:
    records, errors = _load(root)
    if errors:
        raise ValueError("; ".join(errors))
    capabilities = {}
    for _, record in records:
        if record.get("kind") == "capability":
            capabilities[record["id"]] = {"concepts": [], "realizations": [], "evidence": []}
    for _, record in records:
        kind = record.get("kind")
        if kind in ("concept", "realization"):
            for capability_id in record.get("provides", []):
                if capability_id in capabilities:
                    capabilities[capability_id][kind + "s"].append(record["id"])
        elif kind == "evidence":
            capability_id = record.get("requestedCapability")
            if capability_id in capabilities:
                capabilities[capability_id]["evidence"].append(record["id"])
    for entry in capabilities.values():
        for values in entry.values():
            values.sort()
    return {"schemaVersion": 1, "capabilities": dict(sorted(capabilities.items()))}


def validate(root: Path, base_root: Path = None) -> list[str]:
    root = Path(root)
    schema_path = Path(__file__).resolve().parents[1] / "schemas/record.schema.json"
    schema = json.loads(schema_path.read_text(encoding="utf-8"))
    Draft202012Validator.check_schema(schema)
    validator = Draft202012Validator(schema, format_checker=FormatChecker())
    records, errors = _load(root)
    by_id = {}
    valid_paths = set()
    for path, record in records:
        issues = sorted(validator.iter_errors(record), key=lambda e: (list(map(str, e.path)), e.message))
        for issue in issues:
            errors.append(f"{path}: schema: {issue.message}")
        directory = path.split("/", 1)[0]
        if isinstance(record, dict) and record.get("kind") != DIRECTORY_KIND[directory]:
            errors.append(f"{path}: wrong directory for {record.get('kind')}")
        elif not issues:
            valid_paths.add(path)
        if not isinstance(record, dict) or not isinstance(record.get("id"), str):
            continue
        record_id = record["id"]
        if record_id in by_id:
            errors.append(f"{path}: duplicate ID {record_id} (first at {by_id[record_id][0]})")
        else:
            by_id[record_id] = (path, record)

    for path, record in records:
        if path not in valid_paths:
            continue
        for relation in record.get("relations", []):
            if isinstance(relation, dict) and relation.get("target") not in by_id:
                errors.append(f"{path}: missing relation target {relation.get('target')}")
        for target in record.get("provides", []):
            if target not in by_id or by_id[target][1].get("kind") != "capability":
                errors.append(f"{path}: missing capability {target}")
        for target in record.get("supersedes", []):
            if target not in by_id:
                errors.append(f"{path}: missing superseded record {target}")
        if record.get("kind") == "evidence":
            target = record.get("requestedCapability")
            if target not in by_id or by_id[target][1].get("kind") != "capability":
                errors.append(f"{path}: missing requested capability {target}")
            subject = record.get("subjectId")
            if subject is not None and subject not in by_id:
                errors.append(f"{path}: missing evidence subject {subject}")

    evidence = [record for _, record in records if isinstance(record, dict) and record.get("kind") == "evidence"]
    for path, record in records:
        if path not in valid_paths or record.get("kind") != "concept" or record.get("status") != "established":
            continue
        kinds = {example.get("kind") for example in record.get("examples", []) if isinstance(example, dict)}
        if not record.get("reviewed") or not {"positive", "negative"}.issubset(kinds):
            errors.append(f"{path}: established concept requires reviewed contract and positive/negative examples")
        if not any(item.get("subjectId") == record.get("id") and item.get("independent") is True
                   and item.get("outcome") == "accepted" and item.get("verification", {}).get("status") == "passed"
                   for item in evidence):
            errors.append(f"{path}: established concept requires independent evidence")

    if base_root is not None:
        old_records, old_errors = _load(Path(base_root))
        errors.extend(f"base: {message}" for message in old_errors)
        old_by_id = {record.get("id"): record for _, record in old_records if isinstance(record, dict)}
        for path, record in records:
            if not isinstance(record, dict) or record.get("kind") != "concept":
                continue
            previous = old_by_id.get(record.get("id"))
            if previous and previous.get("kind") == "concept" and previous.get("status") in RANK and record.get("status") in RANK:
                difference = RANK[record["status"]] - RANK[previous["status"]]
                if difference < 0 or difference > 1:
                    errors.append(f"{path}: maturity skip or downgrade from {previous['status']} to {record['status']}")
        current_evidence = {record.get("id"): record for _, record in records if isinstance(record, dict) and record.get("kind") == "evidence"}
        for old_path, old_record in old_records:
            if isinstance(old_record, dict) and old_record.get("kind") == "evidence" and current_evidence.get(old_record.get("id")) != old_record:
                errors.append(f"{old_path}: evidence must be append-only")

    if errors:
        return sorted(set(errors))
    try:
        expected = build_index(root)
        index_path = root / "index.json"
        actual = json.loads(index_path.read_text(encoding="utf-8"))
        if actual != expected:
            errors.append("index.json: stale index")
    except (ValueError, OSError, UnicodeError) as exc:
        errors.append(f"index.json: {exc}")
    return sorted(set(errors))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--write-index", action="store_true")
    parser.add_argument("--base", type=Path)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    if args.write_index:
        (root / "index.json").write_text(json.dumps(build_index(root), indent=2) + "\n", encoding="utf-8")
    errors = validate(root, args.base)
    for error in errors:
        print(error)
    if not errors:
        print("Catalog valid")
    return int(bool(errors))


if __name__ == "__main__":
    raise SystemExit(main())
