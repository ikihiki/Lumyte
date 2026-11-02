"""Audit formal ABBA exports; retain raw data and separate timing contracts."""
from collections import defaultdict
import csv
import gzip
import hashlib
import json
import re
from pathlib import Path
import statistics

root = Path(__file__).resolve().parents[3]
source = root / "artifacts/formal-workloads-20261102"
out = root / "docs/performance/data/formal-workloads-20261102"
(out / "raw").mkdir(parents=True, exist_ok=True)
cpu = defaultdict(list)
stages = defaultdict(list)
pipeline = defaultdict(list)
hashes = defaultdict(set)
identities = []
manifest = []


def dist(values):
    a = sorted(values)
    p = .95 * (len(a) - 1)
    i = int(p)
    return {"n": len(a), "median": statistics.median(a), "p95": a[i] + (a[min(i + 1, len(a) - 1)] - a[i]) * (p - i), "min": a[0], "max": a[-1]}


expected = {json.loads(l)["name"] for l in (source / "main-cases.jsonl").read_text().splitlines() if l.startswith("{")}
assert len(expected) == 12
assert expected == {json.loads(l)["name"] for l in (source / "pr105-cases.jsonl").read_text().splitlines() if l.startswith("{")}
for variant in ("main", "pr105"):
    checks = [json.loads(l) for l in (source / f"{variant}-checks.jsonl").read_text().splitlines() if l.startswith("{")]
    assert len(checks) == 12 and all(r["status"] == "passed" and r["count"] == 200 for r in checks)
    for repeat in (1, 2):
        paths = list((source / f"{variant}-run{repeat}-cpu/results").glob("*full.json"))
        assert len(paths) == 1
        export = json.loads(paths[0].read_text())
        assert len(export["Benchmarks"]) == 12
        names = set()
        for b in export["Benchmarks"]:
            params = dict(p.split("=", 1) for p in b["Parameters"].split("&"))
            match = re.search(r'CaseName: "([^"\n]+)"', b["FullName"])
            assert match is not None
            name = match.group(1)
            assert name in expected and params["Count"] == "200" and b["Method"] == "CpuFrame"
            names.add(name)
            assert b["Statistics"] and len(b["Statistics"]["OriginalValues"]) == 8 and b["Memory"]["TotalOperations"] > 0
            cpu[(variant, name)].append({"repeat": repeat, "values_ns": b["Statistics"]["OriginalValues"], "bytes_per_op": b["Memory"]["BytesAllocatedPerOperation"]})
        assert names == expected
        for mode in ("stages", "depth1", "depth3"):
            path = source / f"{variant}-run{repeat}-{mode}.jsonl"
            rows = [json.loads(l) for l in path.read_text().splitlines() if l.startswith("{")]
            identity = rows[0]
            assert identity["check"] == "active-device" and identity["gpuTimestamp"] and "Radeon 8060S" in identity["device"] and "driver=radv" in identity["device"]
            assert identity["warmup"] == 41 and identity["samples"] == 120 and identity["count"] == 200
            identities.append({"variant": variant, "repeat": repeat, "mode": mode, **identity})
            checked = [r for r in rows if r.get("check") == "workload-hardware-readback"]
            assert len(checked) == 12 and {r["workload"] for r in checked} == expected
            for r in checked:
                assert r["status"] == "passed" and r["warmup"] == 41 and r["samples"] == 120
                hashes[r["workload"]].add(r["sha256"])
            if mode == "stages":
                measurements = [r for r in rows if "frameCpuWallMs" in r]
                assert len(measurements) == 1440
                for name in expected:
                    group = [r for r in measurements if r["workload"] == name]
                    assert len(group) == 120 and {r["iteration"] for r in group} == set(range(120))
                    assert all(r["count"] == 200 and r["gpuRecordedSpanMs"] >= 0 for r in group)
                    stages[(variant, name)].extend(group)
            else:
                measurements = [r for r in rows if r.get("scope") == "pipeline" and "frames" in r]
                assert len(measurements) == 12 and {r["workload"] for r in measurements} == expected
                depth = int(mode[-1])
                for r in measurements:
                    assert r["frames"] == 120 and len(r["gpuRecordedSpanMs"]) == 120 and r["framesInFlight"] == depth and r["includesFinalDrain"] and not r["presentation"]
                    pipeline[(variant, r["workload"], depth)].append({"repeat": repeat, **r})
assert all(len(v) == 1 for v in hashes.values()), "Corresponding workload pixels differ across variants/runs/depths"
cs_hashes = []
for variant in ("main", "pr105"):
    lines = (source / f"{variant}-provenance.txt").read_text().splitlines()
    cs_hashes.append([l for l in lines if "  tools/benchmarks/" in l])
assert cs_hashes[0] == cs_hashes[1] and len(cs_hashes[0]) >= 20
cpu_summary = []
for (variant, name), runs in sorted(cpu.items()):
    assert len(runs) == 2
    cpu_summary.append({"variant": variant, "workload": name, "iteration_estimates_ns": dist([x for run in runs for x in run["values_ns"]]), "run_medians_ns": [statistics.median(r["values_ns"]) for r in runs], "run_bytes_per_op": [r["bytes_per_op"] for r in runs]})
stage_summary = []
for (variant, name), values in sorted(stages.items()):
    assert len(values) == 240
    fields = ("updateMs", "layoutInclusiveMs", "textLayoutNestedMs", "recordMs", "prepareMs", "encodeMs", "submitMs", "submitCompletionCpuMs", "disposeMs", "frameCpuWallMs", "gpuRecordedSpanMs", "allocatedProcessWide", "commandCount", "layerCount")
    stage_summary.append({"variant": variant, "workload": name, "suite": values[0]["suite"], "metrics": {f: dist([r[f] for r in values]) for f in fields}})
pipeline_summary = []
for (variant, name, depth), runs in sorted(pipeline.items()):
    assert len(runs) == 2
    pipeline_summary.append({"variant": variant, "workload": name, "depth": depth, "run_headless_fps": [r["headlessFramesPerSecond"] for r in runs], "run_elapsed_ms": [r["elapsedMs"] for r in runs], "gpu_span_ms": dist([x for r in runs for x in r["gpuRecordedSpanMs"]])})
for path in sorted(source.rglob("*")):
    if path.is_file() and (path.parent == source or path.name.endswith("full.json")):
        data = path.read_bytes()
        name = "raw/" + str(path.relative_to(source)).replace("/", "__") + ".gz"
        (out / name).write_bytes(gzip.compress(data, mtime=0))
        manifest.append({"source": str(path.relative_to(source)), "archive": name, "bytes": len(data), "sha256": hashlib.sha256(data).hexdigest()})
failed = root / "artifacts/formal-workloads-20261102-preflight-metadata-failure"
for path in sorted(failed.glob("*")):
    data = path.read_bytes()
    name = "raw/preflight-failure__" + path.name + ".gz"
    (out / name).write_bytes(gzip.compress(data, mtime=0))
    manifest.append({"source": "preflight-failure/" + path.name, "archive": name, "bytes": len(data), "sha256": hashlib.sha256(data).hexdigest()})
summary = {"source_commit": identities[0]["source"], "baselines": {"main": "fbc8105", "pr105": "ffe075c"}, "case_count": 12, "count": 200, "cpu_executions": 48, "warm_stage_frames": 5760, "pipeline_measured_frames": 11520, "matching_readback_observations": 144, "active_devices": identities, "cpu": cpu_summary, "stages": stage_summary, "pipeline": pipeline_summary, "pixel_hashes": {k: next(iter(v)) for k, v in hashes.items()}, "source_hash_count": len(cs_hashes[0])}
(out / "summary.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n")
(out / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n")
with (out / "comparison.csv").open("w", newline="") as f:
    w = csv.writer(f, lineterminator="\n")
    w.writerow(["workload", "cpu_main_median_us", "cpu_pr105_median_us", "cpu_pr105_over_main", "stage_main_wall_ms", "stage_pr105_wall_ms", "gpu_main_ms", "gpu_pr105_ms"])
    for name in sorted(expected):
        a = next(r for r in cpu_summary if r["variant"] == "main" and r["workload"] == name)
        b = next(r for r in cpu_summary if r["variant"] == "pr105" and r["workload"] == name)
        sa = next(r for r in stage_summary if r["variant"] == "main" and r["workload"] == name)["metrics"]
        sb = next(r for r in stage_summary if r["variant"] == "pr105" and r["workload"] == name)["metrics"]
        am = a["iteration_estimates_ns"]["median"]
        bm = b["iteration_estimates_ns"]["median"]
        w.writerow([name, am / 1000, bm / 1000, bm / am, sa["frameCpuWallMs"]["median"], sb["frameCpuWallMs"]["median"], sa["gpuRecordedSpanMs"]["median"], sb["gpuRecordedSpanMs"]["median"]])
print(json.dumps({"cpu_executions": 48, "stage_frames": 5760, "pipeline_frames": 11520, "matching_readbacks": 144, "archives": len(manifest)}))
