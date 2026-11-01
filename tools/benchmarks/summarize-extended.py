#!/usr/bin/env python3
"""Summarize extended wall timings without treating completion as GPU timestamps."""
import collections
import json
import math
import statistics
import sys

rows = [json.loads(line) for line in open(sys.argv[1], encoding="utf-8")]
groups = collections.defaultdict(list)
for row in rows:
    if row.get("phase") == "warm":
        groups[(row["kind"], row.get("workload", ""), row.get("size", 0), row.get("depth", 0))].append(row)
for key, samples in groups.items():
    result = {"group": key, "n": len(samples)}
    for field in ("build", "prepare", "compile", "record", "submit", "completion", "allocated", "elapsed"):
        if field not in samples[0]:
            continue
        values = sorted(row[field] for row in samples)
        position = (len(values) - 1) * .95
        low, high = math.floor(position), math.ceil(position)
        result[field] = {"median": statistics.median(values), "p95": values[low] + (values[high] - values[low]) * (position - low), "cv": statistics.stdev(values) / statistics.mean(values)}
    print(json.dumps(result))
