"""Aggregate workload JSONL or BDN full JSON; keep CPU/GPU timing scopes separate."""
import argparse
from collections import defaultdict
import json
from pathlib import Path
import statistics

parser = argparse.ArgumentParser()
parser.add_argument("files", nargs="+", type=Path)
args = parser.parse_args()
groups = defaultdict(list)
checks = []
cpu = []


def distribution(values):
    a = sorted(values)
    p = .95 * (len(a) - 1)
    i = int(p)
    return {"n": len(a), "median": statistics.median(a), "p95": a[i] + (a[min(i + 1, len(a) - 1)] - a[i]) * (p - i), "min": a[0], "max": a[-1]}


for path in args.files:
    if path.suffix == ".json":
        data = json.loads(path.read_text())
        for b in data["Benchmarks"]:
            assert b["Statistics"] is not None and b["Memory"]["TotalOperations"] > 0, b["FullName"]
            cpu.append({"source": str(path), "method": b["Method"], "parameters": b["Parameters"], "iteration_estimates_ns": distribution(b["Statistics"]["OriginalValues"]), "allocated_bytes_per_operation": b["Memory"]["BytesAllocatedPerOperation"], "scope": ("CPU update+layout+record throughput" if b["Method"] == "CpuFrame" else "CPU isolated single-operation attribution diagnostic; IterationTime ignored") + "; percentile is BDN iteration estimate, not per-frame latency"})
    else:
        for line in path.read_text().splitlines():
            if not line.startswith("{"):
                continue
            row = json.loads(line)
            if "check" in row:
                if "status" in row:
                    assert row["status"] == "passed", row
                checks.append(row)
            elif row.get("scope") in ("stages", "pipeline") and row.get("phase", "measured") == "measured":
                groups[(row["suite"], row["workload"], row["count"], row["scope"], row.get("framesInFlight", 1))].append(row)
results = []
for (suite, name, count, scope, depth), rows in sorted(groups.items()):
    entry = {"suite": suite, "workload": name, "count": count, "scope": scope, "depth": depth}
    if scope == "stages":
        names = ("hostFrameMs", "retireMs", "updateMs", "layoutInclusiveMs", "textLayoutNestedMs", "recordMs", "prepareMs", "encodeMs", "submitMs", "submitCompletionCpuMs", "disposeMs", "frameCpuWallMs", "gpuRecordedSpanMs", "allocatedProcessWide", "commandCount", "layerCount")
        entry["distributions"] = {key: distribution([r[key] for r in rows if r.get(key) is not None]) for key in names if any(r.get(key) is not None for r in rows)}
        entry["text_timing_note"] = "Text layout is nested inside layout/update; do not sum it again."
    else:
        assert all(len(r["gpuRecordedSpanMs"]) in (0, r["frames"]) and r["includesFinalDrain"] for r in rows)
        entry["run_headless_frames_per_second"] = [r["headlessFramesPerSecond"] for r in rows]
        entry["run_elapsed_ms"] = [r["elapsedMs"] for r in rows]
        gpu = [x for r in rows for x in r["gpuRecordedSpanMs"]]
        entry["gpu_span_ms"] = distribution(gpu) if gpu else None
    results.append(entry)
print(json.dumps({"workloads": results, "cpu_bdn": cpu, "checks": checks, "note": "Different suites/scales are not equivalent work; no cross-suite speedup ratio. Headless throughput is not presentation FPS."}, ensure_ascii=False, indent=2))
