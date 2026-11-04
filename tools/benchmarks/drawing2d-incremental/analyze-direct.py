"""Validate three-mode direct-page measurements and archive every observation."""
from collections import defaultdict
from pathlib import Path
import gzip
import hashlib
import json
import statistics

root = Path(__file__).resolve().parents[3]
source = root / "artifacts/architecture-comparison/direct-results"
out = root / "docs/performance/data/drawing2d-direct-pages-20261102"
(out / "raw").mkdir(parents=True, exist_ok=True)
stages = defaultdict(list)
pipelines = defaultdict(list)
pixels = defaultdict(set)
identities = []
manifest = []


def distribution(a):
    a = sorted(a)
    x = .95 * (len(a) - 1)
    i = int(x)
    return {"n": len(a), "median": statistics.median(a), "p95": a[i] + (a[min(i + 1, len(a) - 1)] - a[i]) * (x - i), "min": a[0], "max": a[-1]}


for variant in ("main", "pr105"):
    for repeat in (1, 2):
        path = source / f"{variant}-run{repeat}.jsonl"
        rows = [json.loads(l) for l in path.read_text().splitlines() if l.startswith("{")]
        identity = rows[0]
        assert identity["check"] == "active-device" and identity["gpuTimestamp"] and "1002" in identity["device"] and "1586" in identity["device"]
        identities.append({"repeat": repeat, **identity})
        assert any(r.get("check") == "direct-residency-lifetime-catchup" and r["status"] == "passed" for r in rows)
        checks = [r for r in rows if r.get("check") == "direct-hardware-pixels"]
        warm = [r for r in rows if r.get("scope") == "stages" and r.get("phase") == "warm"]
        throughput = [r for r in rows if r.get("scope") == "pipeline" and "frames" in r]
        assert len(checks) == 60 and len(warm) == 7200 and len(throughput) == 120
        for r in checks:
            assert r["status"] == "passed"
            pixels[(r["scope"], r["count"], r["pattern"], r["framesInFlight"])].add(r["sha256"])
        for r in warm:
            assert 0 <= r["gpuRecordedSpanMs"] <= r["frameWall"] + .1
            if r["mode"] == "direct":
                assert r["gpuAllocations"] == 0 and r["drawCount"] == (r["count"] + 1023) // 1024
                if r["pattern"] == "static":assert r["uploadedBytes"] == 0 and r["directPackedCommands"] == 0
                if r["pattern"] == "single":assert 0 < r["uploadedBytes"] < r["count"] * 96 and r["directPackedCommands"] == 1
                if r["pattern"] == "all":assert r["uploadedBytes"] == r["count"] * 96 and r["directPackedCommands"] == r["count"]
            stages[(variant, r["count"], r["pattern"], r["mode"])].append(r)
        for r in throughput:
            assert r["frames"] == 120 and len(r["gpuRecordedSpanMs"]) == 120 and r["includesFinalDrain"] and not r["presentation"]
            if r["mode"] == "direct":assert r["gpuAllocations"] == 0
            pipelines[(variant, r["count"], r["pattern"], r["mode"], r["framesInFlight"])].append({"repeat": repeat, **r})
assert len(stages) == 120 and len(pipelines) == 240
assert all(len(v) == 1 for v in pixels.values())
summary_stages = []
for (variant, count, pattern, mode), values in sorted(stages.items()):
    assert len(values) == 240
    fields = ("update", "prepare", "record", "submit", "submitCompletion", "dispose", "frameWall", "gpuRecordedSpanMs", "allocatedProcessWide")
    entry = {"variant": variant, "count": count, "pattern": pattern, "mode": mode, "metrics": {f: distribution([r[f] for r in values]) for f in fields}}
    if mode == "direct":
        entry["counters"] = {f: distribution([r[f] for r in values]) for f in ("directPackedCommands", "copiedCpuBytes", "uploadedBytes", "dirtyChunks", "drawCount", "gpuAllocations")}
    summary_stages.append(entry)
summary_pipeline = []
for (variant, count, pattern, mode, depth), values in sorted(pipelines.items()):
    assert len(values) == 2
    entry = {"variant": variant, "count": count, "pattern": pattern, "mode": mode, "depth": depth, "run_headless_fps": [r["headlessFramesPerSecond"] for r in values], "gpu_span_ms": distribution([x for r in values for x in r["gpuRecordedSpanMs"]]), "run_elapsed_ms": [r["elapsedMs"] for r in values]}
    if mode == "direct":entry["run_uploaded_bytes"] = [r["uploadedBytes"] for r in values]
    summary_pipeline.append(entry)
for path in sorted(source.iterdir()):
    if path.is_file():
        data = path.read_bytes()
        name = "raw/" + path.name + ".gz"
        (out / name).write_bytes(gzip.compress(data, mtime=0))
        manifest.append({"source": path.name, "archive": name, "bytes": len(data), "sha256": hashlib.sha256(data).hexdigest()})
summary = {"source_commit": "fcd6e7b", "main_commit": "fbc8105", "pr105_commit": "ffe075c", "active_device": identities, "warm_stage_frames": 28800, "pipeline_measured_frames": 57600, "three_mode_pixel_groups": 240, "stages": summary_stages, "pipeline": summary_pipeline}
(out / "summary.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n")
(out / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n")
print(json.dumps({"stage_groups": len(stages), "pipeline_groups": len(pipelines), "pixel_groups": 240, "archives": len(manifest)}))
