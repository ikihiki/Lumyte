"""Compare independent stage processes; do not pool frames across processes."""
import argparse
import json
import statistics
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("directory", type=Path)
parser.add_argument("--samples", type=int, default=2000)
args = parser.parse_args()
metrics = ("updateMs", "layoutInclusiveMs", "textLayoutNestedMs", "recordMs", "prepareMs", "encodeMs", "submitMs", "submitCompletionCpuMs", "disposeMs", "frameCpuWallMs", "gpuRecordedSpanMs", "allocatedProcessWide")


def distribution(values):
    values = sorted(values)
    index = .95 * (len(values) - 1)
    low = int(index)
    return {"n": len(values), "median": statistics.median(values), "p95": values[low] + (values[min(low + 1, len(values) - 1)] - values[low]) * (index - low), "max": max(values)}


runs = []
hashes = set()
for pair in range(1, 6):
    for variant in ("baseline", "candidate"):
        path = args.directory / f"pair-{pair}-{variant}.jsonl"
        rows = [json.loads(line) for line in path.read_text().splitlines() if line.startswith("{")]
        identity = [row for row in rows if row.get("check") == "active-device"]
        assert len(identity) == 1 and "device=0x1586" in identity[0]["device"]
        frames = [row for row in rows if "iteration" in row]
        assert len(frames) == args.samples and [row["iteration"] for row in frames] == list(range(args.samples))
        assert all(row["commandCount"] == 619 and row["layerCount"] == 1 for row in frames)
        assert sum(row["frameCpuWallMs"] for row in frames) >= 10000
        assert all(row["gpuRecordedSpanMs"] >= 0 for row in frames)
        checks = [row for row in rows if row.get("check") == "workload-hardware-readback"]
        assert len(checks) == 1 and checks[0]["status"] == "passed"
        hashes.add(checks[0]["sha256"])
        runs.append({"pair": pair, "variant": variant, "source": identity[0]["source"], "metrics": {key: distribution([row[key] for row in frames]) for key in metrics}})
assert len(hashes) == 1, hashes
comparison = {}
for metric in metrics:
    before = [run["metrics"][metric]["median"] for run in runs if run["variant"] == "baseline"]
    after = [run["metrics"][metric]["median"] for run in runs if run["variant"] == "candidate"]
    comparison[metric] = {"baseline_process_medians": before, "candidate_process_medians": after, "paired_percent_change": [(b / a - 1) * 100 for a, b in zip(before, after)], "baseline_median": statistics.median(before), "candidate_median": statistics.median(after)}
print(json.dumps({"runs": runs, "comparison": comparison, "pixel_sha256": hashes.pop(), "note": "Per-process frame distributions; 5 alternating pairs. GPU span is elapsed query time, not exclusive occupancy. No Present."}, indent=2))
