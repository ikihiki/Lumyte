#!/usr/bin/env python3
"""Summarize raw native JSONL with median, interpolated p95 and CV; no GPU-time inference."""
import json
import math
import statistics
import sys

rows = [json.loads(line) for line in open(sys.argv[1], encoding="utf-8") if line.startswith("{")]
for count in sorted({row["count"] for row in rows}):
    selected = [row for row in rows if row["count"] == count and row["phase"] == "warm"]
    result = {"count": count, "samples": len(selected)}
    for name in ("build", "prepare", "record", "submit", "submitCompletion", "allocated"):
        values = sorted(row[name] for row in selected)
        position = (len(values) - 1) * .95
        lower = math.floor(position)
        upper = math.ceil(position)
        p95 = values[lower] + (values[upper] - values[lower]) * (position - lower)
        result[name] = {"median": statistics.median(values), "p95": p95, "cv": statistics.stdev(values) / statistics.mean(values)}
    print(json.dumps(result))
