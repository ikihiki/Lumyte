#!/usr/bin/env python3
"""Analyze every frozen ABBA process; timings are BDN iteration estimates, not frame tails."""
import csv
import gzip
import json
from pathlib import Path
from statistics import median

root = Path(__file__).resolve().parent
runs = ("A1", "B1", "B2", "A2")
rows = []
by_run = {}
for run in runs:
    cases = {}
    directory = root / "raw" / run / "results"
    files = sorted(directory.glob("*-report-full.json")) or sorted(directory.glob("*-report-full.json.gz"))
    if len(files) != 2:
        raise RuntimeError(f"{run}: expected two completed suite exports, got {len(files)}")
    for path in files:
        data = json.loads(gzip.decompress(path.read_bytes()).decode() if path.suffix == ".gz" else path.read_text())
        for benchmark in data["Benchmarks"]:
            stats = benchmark["Statistics"]
            if stats is None or stats["N"] != 10:
                raise RuntimeError(f"Incomplete observation: {run} {benchmark['FullName']}")
            name = benchmark["FullName"].split(".Retained.", 1)[1]
            row = dict(run=run, case=name, median_us=stats["Median"] / 1000,
                       p95_us=stats["Percentiles"]["P95"] / 1000,
                       min_us=stats["Min"] / 1000, max_us=stats["Max"] / 1000,
                       allocated_bytes=benchmark["Memory"]["BytesAllocatedPerOperation"],
                       iteration_estimates_ns=stats["OriginalValues"])
            cases[name] = row
            rows.append(row)
    if len(cases) != 8:
        raise RuntimeError(f"{run}: expected 8 cases, got {len(cases)}")
    by_run[run] = cases
if any(set(by_run[run]) != set(by_run["A1"]) for run in runs):
    raise RuntimeError("Case identity differs between processes")

summary = []
for name in by_run["A1"]:
    values = {run: by_run[run][name] for run in runs}
    a = median(values[run]["median_us"] for run in ("A1", "A2"))
    b = median(values[run]["median_us"] for run in ("B1", "B2"))
    pairs = [values["B1"]["median_us"] - values["A1"]["median_us"],
             values["B2"]["median_us"] - values["A2"]["median_us"]]
    summary.append(dict(case=name, baseline_median_of_process_medians_us=a,
                        candidate_median_of_process_medians_us=b,
                        delta_us=b-a, change_percent=(b/a-1)*100,
                        paired_deltas_us=pairs,
                        baseline_process_spread_us=abs(values["A2"]["median_us"]-values["A1"]["median_us"]),
                        candidate_process_spread_us=abs(values["B2"]["median_us"]-values["B1"]["median_us"]),
                        processes=values))
(root / "summary.json").write_text(json.dumps(summary, indent=2)+"\n")
with (root / "process-results.csv").open("w", newline="") as output:
    fields = [key for key in rows[0] if key != "iteration_estimates_ns"]
    writer = csv.DictWriter(output, fieldnames=fields)
    writer.writeheader()
    writer.writerows({key: row[key] for key in fields} for row in rows)
for case in summary:
    print(f"{case['case']}: {case['baseline_median_of_process_medians_us']:.5f} -> "
          f"{case['candidate_median_of_process_medians_us']:.5f} us "
          f"({case['change_percent']:+.2f}%), pairs {case['paired_deltas_us']}")
