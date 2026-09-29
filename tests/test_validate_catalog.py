import json
import tempfile
import unittest
from pathlib import Path

from tools.validate_catalog import build_index, validate


CAPABILITY = {
    "schemaVersion": 1,
    "kind": "capability",
    "id": "Concurrency.CoalesceInFlight",
    "name": "Coalesce in-flight work",
    "definition": "Share one pending computation per key.",
    "contract": {
        "inputs": [{"name": "key", "type": "K"}],
        "result": "V",
        "effect": "host",
    },
    "relations": [],
}
CONCEPT = {
    "schemaVersion": 1,
    "kind": "concept",
    "id": "Concurrency.SingleFlight",
    "name": "SingleFlight",
    "definition": "Concurrent requests for one key share one pending computation.",
    "status": "candidate",
    "inputs": [{"name": "key", "type": "K"}],
    "outputs": [{"name": "value", "type": "V"}],
    "constraints": [],
    "invariants": [],
    "applicabilityLimits": ["Cancellation policy must be explicit."],
    "provides": ["Concurrency.CoalesceInFlight"],
    "examples": [],
    "relations": [],
}


class CatalogValidationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.write("capabilities/capability.json", CAPABILITY)
        self.write("concepts/concept.json", CONCEPT)

    def write(self, name, data):
        path = self.root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(data), encoding="utf-8")

    def test_valid_candidate_and_discovery(self):
        self.write("index.json", build_index(self.root))
        self.assertEqual([], validate(self.root))
        self.assertEqual(
            ["Concurrency.SingleFlight"],
            build_index(self.root)["capabilities"]["Concurrency.CoalesceInFlight"]["concepts"],
        )

    def test_duplicate_id_rejected(self):
        self.write("concepts/duplicate.json", CONCEPT)
        self.assertTrue(any("duplicate ID Concurrency.SingleFlight" in error for error in validate(self.root)))

    def test_dangling_relation_rejected(self):
        concept = dict(CONCEPT, relations=[{"kind": "requires", "target": "Missing.Type", "rationale": "needed"}])
        self.write("concepts/concept.json", concept)
        self.assertTrue(any("missing relation target Missing.Type" in error for error in validate(self.root)))

    def test_established_requires_independent_evidence(self):
        concept = dict(CONCEPT, status="established", examples=[{"kind": "positive", "description": "works"}, {"kind": "negative", "description": "fails"}])
        self.write("concepts/concept.json", concept)
        self.assertTrue(any("independent evidence" in error for error in validate(self.root)))

    def test_stale_index_rejected(self):
        self.write("index.json", {"schemaVersion": 1, "capabilities": {}})
        self.assertTrue(any("stale index" in error for error in validate(self.root)))

    def test_wrong_record_directory_rejected(self):
        (self.root / "concepts/concept.json").unlink()
        self.write("capabilities/concept.json", CONCEPT)
        self.assertTrue(any("wrong directory" in error for error in validate(self.root)))

    def test_invalid_record_does_not_crash_validator(self):
        self.write("concepts/invalid.json", {"kind": "concept"})
        self.assertTrue(any("schema" in error for error in validate(self.root)))

    def test_invalid_field_type_does_not_crash_validator(self):
        self.write("concepts/concept.json", dict(CONCEPT, provides=None))
        self.assertTrue(any("schema" in error for error in validate(self.root)))

    def test_maturity_skip_rejected_against_base(self):
        with tempfile.TemporaryDirectory() as base_dir:
            base = Path(base_dir)
            (base / "concepts").mkdir()
            (base / "concepts/concept.json").write_text(json.dumps(dict(CONCEPT, status="observation")), encoding="utf-8")
            self.write("concepts/concept.json", dict(CONCEPT, status="established"))
            self.assertTrue(any("maturity skip" in error for error in validate(self.root, base)))

    def test_failed_reuse_remains_in_capability_index(self):
        self.write("evidence/failure.json", {
            "schemaVersion": 1, "kind": "evidence", "id": "EV-20260929-failure",
            "date": "2026-09-29", "problemClass": "async cache", "domain": "software",
            "requestedCapability": "Concurrency.CoalesceInFlight",
            "subjectId": "Concurrency.SingleFlight", "match": "adaptation",
            "outcome": "failed", "verification": {"status": "failed", "method": "concurrency test"},
            "reason": "Cancellation policy did not match.", "source": "local test"
        })
        entry = build_index(self.root)["capabilities"]["Concurrency.CoalesceInFlight"]
        self.assertEqual(["EV-20260929-failure"], entry["evidence"])

    def test_repository_seed_is_discoverable_without_execution_claim(self):
        repository = Path(__file__).resolve().parents[1]
        entry = build_index(repository)["capabilities"]["Concurrency.CoalesceInFlight"]
        self.assertEqual(["Concurrency.SingleFlight"], entry["concepts"])
        self.assertEqual([], entry["realizations"])


if __name__ == "__main__":
    unittest.main()
