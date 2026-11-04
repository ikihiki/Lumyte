"""Validate and summarize the frozen-source Radeon comparison."""
from collections import defaultdict
from pathlib import Path
import gzip
import hashlib
import json
import statistics

root = Path(__file__).resolve().parents[3]
source = root / "artifacts/architecture-comparison/hardware-results"
out = root / "docs/performance/data/drawing2d-hardware-20261102"
(out / "raw").mkdir(parents=True, exist_ok=True)
stage = defaultdict(list)
pipeline = defaultdict(list)
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
        assert identity["check"] == "active-device" and identity["gpuTimestamp"]
        assert "1002" in identity["device"] and "1586" in identity["device"] and "driver=radv" in identity["device"]
        identities.append({"variant": variant, "repeat": repeat, **identity})
        checks = [r for r in rows if r.get("check") == "hardware-pixels"]
        warm = [r for r in rows if r.get("scope") == "stages" and r.get("phase") == "warm"]
        throughput = [r for r in rows if r.get("scope") == "pipeline" and "frames" in r]
        assert len(checks) == 24 and len(warm) == 2880 and len(throughput) == 24
        for r in checks:
            assert r["status"] == "passed"
            pixels[(r["scope"], r["count"], r["pattern"], r["depth"])].add(r["sha256"])
        for r in warm:
            assert r["backend"] == "hardware-radv" and 0 <= r["gpuRecordedSpanMs"] <= r["frameWall"] + .1
            stage[(variant, r["count"], r["pattern"], r["mode"])].append(r)
        for r in throughput:
            assert len(r["gpuRecordedSpanMs"]) == 120 and r["frames"] == 120 and r["includesFinalDrain"] and not r["presentation"]
            pipeline[(variant, r["count"], r["pattern"], r["mode"], r["framesInFlight"])].append({"repeat": repeat, **r})
assert all(len(v) == 1 for v in pixels.values())

stages = []
for (v, count, pattern, mode), values in sorted(stage.items()):
    assert len(values) == 240
    fields = ("update", "prepare", "record", "submit", "submitCompletion", "dispose", "frameWall", "gpuRecordedSpanMs")
    stages.append({"variant": v, "count": count, "pattern": pattern, "mode": mode, "milliseconds": {f: distribution([r[f] for r in values]) for f in fields}, "allocated_process_wide_bytes": distribution([r["allocatedProcessWide"] for r in values])})
throughputs = []
for (v, count, pattern, mode, depth), values in sorted(pipeline.items()):
    assert len(values) == 2
    throughputs.append({"variant": v, "count": count, "pattern": pattern, "mode": mode, "depth": depth, "run_headless_fps": [r["headlessFramesPerSecond"] for r in values], "run_elapsed_ms": [r["elapsedMs"] for r in values], "gpu_span_ms": distribution([x for r in values for x in r["gpuRecordedSpanMs"]]), "includes_final_drain": True})
for path in sorted(source.iterdir()):
    if path.is_file():
        data = path.read_bytes()
        name = "raw/" + path.name + ".gz"
        (out / name).write_bytes(gzip.compress(data, mtime=0))
        manifest.append({"source": path.name, "archive": name, "bytes": len(data), "sha256": hashlib.sha256(data).hexdigest()})
summary = {"source_commit": "bc185cf", "main_commit": "fbc8105", "pr105_commit": "ffe075c", "adapter": identities, "warm_stage_frames": 11520, "pipeline_measured_frames": 11520, "pixel_pairs_passed": 96, "stages": stages, "pipeline": throughputs, "pixel_hashes": [{"scope": k[0], "count": k[1], "pattern": k[2], "depth": k[3], "sha256": next(iter(v))} for k, v in sorted(pixels.items())]}
(out / "summary.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n")
(out / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n")
print(json.dumps({"stage_groups": len(stages), "pipeline_groups": len(throughputs), "pixel_pairs_passed": 96, "archives": len(manifest)}))
