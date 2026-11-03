#!/usr/bin/env python3
"""Validate public-only scene/UI samples before summarizing their stage costs."""
import argparse
import gzip
import hashlib
import tempfile
import json
import math
import statistics
from collections import defaultdict
from pathlib import Path


def spread(values):
    values = sorted(values)
    assert values and all(math.isfinite(x) and x >= 0 for x in values)
    p = (len(values) - 1) * .95
    i = int(p)
    return dict(n=len(values), median=statistics.median(values),
                p95=values[i] + (values[min(i + 1, len(values) - 1)] - values[i]) * (p - i),
                min=min(values), max=max(values))


def parse(path, baseline, samples, warmup):
    rows = [json.loads(line) for line in path.read_text().splitlines() if line.strip()]
    meta = [r for r in rows if r.get("scope") == "public-retained-api-metadata"]
    assert len(meta) == 1 and meta[0]["samples"] == samples and meta[0]["warmup"] == warmup
    assert meta[0]["tieredCompilation"] == "0" and meta[0]["tieredPGO"] == "0" and not meta[0]["serverGC"]
    assert meta[0]["cpuOnly"] == baseline
    cpu = defaultdict(list)
    gpu = defaultdict(list)
    for r in rows:
        if r.get("scope") == "public-retained-ui-cpu-frame":
            key = r["rectangleCount"], r["state"]
            cpu[key].append(r)
            assert r["layoutKind"] == "flat-panel-overlapping-siblings" and r["uiElementCount"] == r["rectangleCount"] + 1
            assert not r["gpuMeasured"] and r["lastRecordingCount"] == (0 if r["state"] == "static" else 2)
        elif r.get("scope") == "public-retained-api-gpu-frame":
            key = r["fixture"], r["rectangleCount"], r["contentNodeCount"], r["mode"], r["state"]
            gpu[key].append(r)
            count = r["rectangleCount"]
            assert r["commandCount"] == count
            if r["fixture"] == "constant-command-partitions":
                assert count == 1024 and r["contentNodeCount"] in [1, 16, 256] and r["sceneNodeCount"] == r["contentNodeCount"] + 1
                expected_draws = r["contentNodeCount"]
                assert r["lastRecordingCount"] is None
            else:
                assert count in [100, 1000] and r["layoutKind"] == "grid-stacks"
                assert r["sceneNodeCount"] is None and r["contentNodeCount"] is None
                assert r["lastRecordingCount"] == (0 if r["state"] == "static" else 3)
                expected_draws = count
            assert r["actualIssuedDrawCalls"] == (expected_draws if r["mode"] == "public-retained" else 1)
            for name in ["inputMs", "updateMs", "referenceMs", "prepareMs", "recordFinishMs", "encodeMs", "submitMs", "submitCompletionMs", "retireMs"]:
                assert math.isfinite(r[name]) and r[name] >= 0
            assert r["gpuRecordedSpanMs"] is not None and math.isfinite(r["gpuRecordedSpanMs"]) and r["gpuRecordedSpanMs"] >= 0
            assert r["gpuRecordedSpanMs"] <= r["submitCompletionMs"] + 1.0
    expected_cpu = {(n, state) for n in [100, 1000, 10000] for state in ["static", "one-leaf-fill-change"]}
    assert set(cpu) == expected_cpu
    expected_gpu = {(fixture, n, parts, mode, state)
                    for fixture, n, parts in [("constant-command-partitions", 1024, p) for p in [1, 16, 256]] + [("actual-ui-adapter", n, None) for n in [100, 1000]]
                    for mode in ["public-retained", "independent-canvas"] for state in ["static", "one-command-change"]}
    assert set(gpu) == (set() if baseline else expected_gpu)
    for group in [cpu, gpu]:
        for values in group.values():
            assert len(values) == samples and [v["frame"] for v in values] == list(range(samples))
    checks = [r for r in rows if r.get("check") == "production-retained-exact-pixels"]
    ends = [r for r in rows if r.get("scope") == "public-retained-api-series-end"]
    assert len(checks) == (0 if baseline else 60) and all(r["status"] == "passed" for r in checks)
    assert len(ends) == (0 if baseline else 20)
    if not baseline:
        devices = [r for r in rows if r.get("scope") == "public-retained-api-device"]
        assert len(devices) == 1 and "Radeon 8060S" in devices[0]["device"] and devices[0]["timestampSupported"]
        hashes = defaultdict(set)
        for r in ends:
            assert r["samples"] == samples and r["warmup"] == warmup
            hashes[r["fixture"]].add(r["sha256"])
        assert all(len(v) == 1 for v in hashes.values())
        assert len({r["sha256"] for r in ends if r["fixture"].startswith("partitions/")}) == 1
    return meta[0], cpu, gpu


def analyze(root, samples, warmup):
    cpu_all = defaultdict(list)
    gpu_all = defaultdict(list)
    metadata = []
    for repeat in [1, 2]:
        for variant, baseline in [("baseline", True), ("current", False)]:
            meta, cpu, gpu = parse(root / f"{variant}-{repeat}.jsonl", baseline, samples, warmup)
            metadata.append(dict(variant=variant, repeat=repeat, metadata=meta))
            assert meta["reverse"] == (repeat == 2)
            for key, values in cpu.items():
                cpu_all[(variant,) + key].extend([dict(v, repeat=repeat) for v in values])
            for key, values in gpu.items():
                gpu_all[key].extend([dict(v, repeat=repeat) for v in values])
    def summarize(groups, fields):
        result = []
        for key, rows in sorted(groups.items(), key=lambda item: str(item[0])):
            assert len(rows) == samples * 2
            result.append(dict(key=key, metrics={field: spread([r[field] for r in rows]) for field in fields},
                               runMedians={field: [statistics.median(r[field] for r in rows if r["repeat"] == repeat) for repeat in [1, 2]] for field in fields}))
        return result
    return dict(metadata=metadata, samplesPerCase=samples * 2,
                cpu=summarize(cpu_all, ["inputMs", "updateMs", "lastRecordingCount"]),
                gpu=summarize(gpu_all, ["inputMs", "updateMs", "referenceMs", "prepareMs", "recordFinishMs", "encodeMs", "submitCompletionMs", "retireMs", "actualIssuedDrawCalls", "compiledRuns", "reusedRuns", "gpuRecordedSpanMs"]),
                validation=dict(cpuFrames=4 * 6 * samples, gpuFrames=2 * 20 * samples, exactPixelComparisons=120),
                caveats=["CPU UI is a flat Panel; GPU UI is a Stack grid, not the same layout workload.",
                         "Actual issued draw calls are forwarded public encoder calls, not GPU primitive/occupancy counters.",
                         "UI1000 uses the default 512-asset budget; its eviction misses are included.",
                         "One changed leaf also re-records empty ancestors: flat 2, grid 3.",
                         "Legacy cases perform the same retained input/update for preparation isolation; do not sum into a full application comparison.",
                         "Per-frame p95 pools two independent processes; no claim of an independent-run p95 or displayed FPS."])


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("input", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--samples", type=int, default=20)
    parser.add_argument("--warmup", type=int, default=10)
    args = parser.parse_args()
    if (args.input / "manifest.json").exists():
        with tempfile.TemporaryDirectory(prefix="public-retained-replay-") as temporary:
            base = Path(temporary)
            for entry in json.loads((args.input / "manifest.json").read_text()):
                if entry["phase"] != "measure":
                    continue
                data = gzip.decompress((args.input / entry["archive"]).read_bytes())
                assert len(data) == entry["bytes"] and hashlib.sha256(data).hexdigest() == entry["sha256"]
                name = Path(entry["source"]).name
                assert name in ["current-1.jsonl", "current-2.jsonl", "baseline-1.jsonl", "baseline-2.jsonl", "environment.txt"]
                (base / name).write_bytes(data)
            summary = analyze(base, args.samples, args.warmup)
    else:
        summary = analyze(args.input, args.samples, args.warmup)
    args.output.write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n")
    print(json.dumps(dict(status="passed", **summary["validation"])))
