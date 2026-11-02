"""Summarize completed BDN exports without discarding outliers or failed cases."""
import csv
import gzip
import hashlib
import json
from collections import defaultdict
from pathlib import Path
import statistics

root = Path(__file__).resolve().parents[3]
source = root / "artifacts/architecture-comparison/results"
out = root / "docs/performance/data/drawing2d-incremental-20261102"
out.mkdir(parents=True, exist_ok=True)
raw = out / "raw"
raw.mkdir(exist_ok=True)
manifest = []


def archive(path):
    name = str(path.relative_to(source)).replace("/", "__") + ".gz"
    data = path.read_bytes()
    (raw / name).write_bytes(gzip.compress(data, mtime=0))
    manifest.append({"source": str(path.relative_to(source)), "archive": "raw/" + name, "bytes": len(data), "sha256": hashlib.sha256(data).hexdigest()})


def percentile(values, q):
    a = sorted(values)
    x = (len(a) - 1) * q
    i = int(x)
    return a[i] + ((a[min(i + 1, len(a) - 1)] - a[i]) * (x - i))


def stats(values):
    return {"n": len(values), "median": statistics.median(values), "p95": percentile(values, .95), "min": min(values), "max": max(values)}


pool = defaultdict(lambda: {"values": [], "allocated": [], "replicate_medians_ns": []})
secondary = []
for variant in ("main", "pr105"):
    for repeat in (1, 2):
        path = next((source / f"{variant}-run{repeat}/results").glob("*full.json"))
        benchmarks = json.loads(path.read_text())["Benchmarks"]
        assert len(benchmarks) == 30
        seen = set()
        for b in benchmarks:
            p = dict(x.split("=") for x in b["Parameters"].split("&"))
            key = (variant, int(p["Count"]), p["Pattern"], b["Method"])
            assert key not in seen
            seen.add(key)
            values = b["Statistics"]["OriginalValues"]
            assert len(values) == 8 and b["Memory"]["TotalOperations"] > 0
            entry = pool[key]
            entry["values"].extend(values)
            entry["allocated"].append(b["Memory"]["BytesAllocatedPerOperation"])
            entry["replicate_medians_ns"].append(b["Statistics"]["Median"])
        archive(path)
    for path in (source / f"{variant}-secondary/results").glob("*full.json"):
        benchmarks = json.loads(path.read_text())["Benchmarks"]
        assert len(benchmarks) == (8 if "Chunk" in path.name else 4)
        for b in benchmarks:
            values = b["Statistics"]["OriginalValues"]
            assert len(values) == 8
            secondary.append({"variant": variant, "type": b["Type"], "method": b["Method"], "parameters": b["Parameters"], "ns": stats(values), "allocated_bytes": b["Memory"]["BytesAllocatedPerOperation"]})
        archive(path)

rows = []
for (variant, count, pattern, method), entry in sorted(pool.items()):
    assert len(entry["values"]) == 16
    rows.append({"variant": variant, "count": count, "pattern": pattern, "method": method, "ns": stats(entry["values"]), "allocated_bytes": statistics.median(entry["allocated"]), "replicate_medians_ns": entry["replicate_medians_ns"]})

retention = defaultdict(list)
software = defaultdict(list)
pixel_checks = []
for variant in ("main", "pr105"):
    for kind in ("smoke", "retention", "software"):
        path = source / f"{variant}-{kind}.jsonl"
        records = [json.loads(line) for line in path.read_text().splitlines() if line.startswith("{")]
        archive(path)
        if kind == "smoke":
            assert records[-1]["status"] == "passed" and records[-1]["cases"] == 90
        elif kind == "retention":
            assert len(records) == 60
            for record in records:
                retention[(variant, record["pattern"], record["chunk"], record["mode"])].append(record)
        else:
            checks = [x for x in records if x.get("check") == "software-gpu-pixels"]
            assert len(checks) == 6 and all(x["status"] == "passed" for x in checks)
            pixel_checks.extend({"variant": variant, **x} for x in checks)
            for record in records:
                if record.get("phase") == "warm":
                    assert record["backend"] == "software-vulkan" and not record["gpuTimestamp"]
                    software[(variant, record["count"], record["pattern"], record["mode"])].append(record)
    archive(source / f"{variant}-provenance.txt")
    archive(source / f"{variant}-build.log")

heap = []
for (variant, pattern, chunk, mode), values in sorted(retention.items()):
    assert len(values) == 3
    heap.append({"variant": variant, "pattern": pattern, "chunk": chunk, "mode": mode, "live_heap_bytes": stats([x["retainedHeapBytes"] for x in values]), "initial_allocated_bytes": stats([x["initialAllocated"] for x in values])})

stages = []
for (variant, count, pattern, mode), values in sorted(software.items()):
    assert len(values) == 18
    stages.append({"variant": variant, "count": count, "pattern": pattern, "mode": mode, "stages_ms": {stage: stats([x[stage] for x in values]) for stage in ("update", "prepare", "record", "submit", "submitCompletion", "dispose")}, "allocated_process_wide_bytes": stats([x["allocatedProcessWide"] for x in values])})
for count in (1000, 10000):
    for pattern in ("static", "single", "all"):
        hashes = {x["sha256"] for x in pixel_checks if x["count"] == count and x["pattern"] == pattern}
        assert len(hashes) == 1, (count, pattern, hashes)

archive(source / "environment.txt")
hardware = root / "artifacts/architecture-comparison-hardware.log"
if hardware.exists():
    data = hardware.read_bytes()
    name = "raw/hardware-access.log.gz"
    (out / name).write_bytes(gzip.compress(data, mtime=0))
    manifest.append({"source": str(hardware.relative_to(root)), "archive": name, "bytes": len(data), "sha256": hashlib.sha256(data).hexdigest()})
summary = {"benchmark_source_commit": "9e402e9", "main_commit": "fbc8105", "pr105_commit": "ffe075c", "primary_executions": 120, "secondary_executions": 24, "primary_statistic": "pooled 2 independent launches x 8 iteration estimates, nanoseconds; not individual frame latency", "hardware_gpu": "blocked: renderD128 permission denied", "updates": rows, "secondary": secondary, "retention": heap, "software_integration": stages, "pixel_checks": pixel_checks}
(out / "summary.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n")
(out / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n")
with (out / "updates.csv").open("w", newline="") as f:
    writer = csv.writer(f)
    writer.writerow(["variant", "count", "pattern", "method", "median_ns", "p95_ns", "allocated_bytes", "run1_median_ns", "run2_median_ns"])
    for r in rows:
        writer.writerow([r["variant"], r["count"], r["pattern"], r["method"], r["ns"]["median"], r["ns"]["p95"], r["allocated_bytes"], *r["replicate_medians_ns"]])
print(json.dumps({"primary_rows": len(rows), "secondary_rows": len(secondary), "retention_groups": len(heap), "software_groups": len(stages), "pixel_checks": len(pixel_checks), "raw_files": len(manifest)}))
