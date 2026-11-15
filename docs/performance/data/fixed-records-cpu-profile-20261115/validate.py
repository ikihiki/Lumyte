#!/usr/bin/env python3
"""Bounded evidence checks; no wall-time thresholds."""
import json
from pathlib import Path

ROOT = Path(__file__).parent

def valid_join(begin, end):
    return all(begin.get(k) == end.get(k) for k in
               ("provider", "run", "frame", "span", "parent", "stage"))

def associated(row):
    return row["run"] != "00000000-0000-0000-0000-000000000000" and row["frame"] > 0

row = dict(provider="Drawing2D", run="run", frame=1, span=2, parent=1, stage=6)
assert valid_join(row, row.copy())
for key in row:
    changed = row.copy()
    changed[key] = "different"
    assert not valid_join(row, changed)
assert not associated(dict(run="00000000-0000-0000-0000-000000000000", frame=1))
assert not associated(dict(run="run", frame=0))
assert not associated(dict(run="run", frame=-1))
rows = json.loads((ROOT / "summary.json").read_text())
assert len(rows) == 8
assert len({(x["variant"], x["case"]) for x in rows}) == 8
for r in rows:
    d = json.loads((ROOT / (r["variant"] + "-" + r["case"] + ".json")).read_text())
    assert d["EventsLost"] == 0
    assert r["managedSamples"] + r["externalSamples"] == r["samples"]
    assert sum(d["ManagedGroups"].values()) == r["managedSamples"]
    assert r["frames"] > 0
    for key in ("UnmatchedBegins", "unmatchedEnds", "mismatches", "duplicateBegins",
                "missing", "counterMismatch", "parentMissing"):
        assert d["Joins"][key] == 0
    windows = d["Windows"]
    assert len(windows) == 30
    assert all(w["Kind"] == "WorkloadActual/Start" and w["End"] > w["Start"] for w in windows)
    assert all(a["End"] < b["Start"] for a, b in zip(windows, windows[1:]))
print("PASS: bounded identity/association checks and eight real trace consistency checks")
