#!/usr/bin/env python3
"""Keep mandatory quick checks separate from the warmed production matrix."""
import argparse
import gzip
import hashlib
import tempfile
import json
import math
import statistics
from collections import defaultdict
from pathlib import Path


def distribution(values):
    ordered = sorted(values)
    assert ordered and all(math.isfinite(v) and v >= 0 for v in ordered)
    position = (len(ordered) - 1) * .95
    index = int(position)
    p95 = ordered[index] + (ordered[min(index + 1, len(ordered) - 1)] - ordered[index]) * (position - index)
    return dict(n=len(ordered), median=statistics.median(ordered), p95=p95, min=min(ordered), max=max(ordered))


def analyze(root):
    serial = defaultdict(list)
    throughput = defaultdict(list)
    hashes = defaultdict(set)
    validation = []
    source = set()
    for repeat in [1, 2]:
        records = [json.loads(line) for line in (root / f"run-{repeat}.jsonl").read_text().splitlines() if line.strip()]
        pending = []
        observed = set()
        ordered_modes = []
        quick_cases = 0
        pixel_checks = 0
        for row in records:
            scope = row.get("scope")
            if scope == "production-retained-metadata":
                assert "Radeon 8060S" in row["device"] and row["timestampSupported"]
                assert row["tieredCompilation"] == "0" and row["tieredPGO"] == "0" and not row["serverGC"]
                source.add(row["sourceRevision"])
            elif row.get("check") == "production-retained-exact-pixels":
                assert row["status"] == "passed"
                pixel_checks += 1
            elif scope in ["serial-frame", "pipeline-host-frame"]:
                pending.append(row)
            elif scope == "production-retained-throughput":
                assert len(pending) == row["frames"] and row["includesFinalDrain"]
                assert row["includesFrameRetirement"] and row["includesInputRecording"] and not row["presentation"]
                assert len(row["gpuRecordedSpanMs"]) == row["frames"]
                assert all(math.isfinite(v) and v >= 0 for v in row["gpuRecordedSpanMs"])
                key = (row["name"], row["count"], row["mode"], row["depth"])
                assert all((v["name"], v["count"], v["mode"], v["depth"]) == key for v in pending)
                assert [v["iteration"] for v in pending] == list(range(row["frames"]))
                if row["frames"] == 40 and row["warmup"] == 21:
                    assert key not in observed
                    observed.add(key)
                    ordered_modes.append(row["mode"])
                    row["repeat"] = repeat
                    row["headlessFpsIncludingCacheDisposal"] = row["frames"] * 1000 / (row["elapsedMs"] + row["cacheOwnerDisposeOutsideWindow"])
                    assert row["estimatedIdleBytes"] <= 64 * 1024 * 1024 and row["idleAssets"] <= 512
                    throughput[key].append(row)
                    hashes[(row["name"], row["count"])].add(row["sha256"])
                    if row["depth"] == 1:
                        for value in pending:
                            value["repeat"] = repeat
                        serial[key[:-1]].extend(pending)
                else:
                    assert row["frames"] == 4 and row["warmup"] == 3
                    quick_cases += 1
                pending = []
        assert not pending and len(observed) == 56 and quick_cases == 36 and pixel_checks == 101
        assert ordered_modes[:2] == (["legacy-canvas", "production-retained"] if repeat == 1 else ["production-retained", "legacy-canvas"])
        expected_rect = {(name, count, mode, depth) for name in ["rect-static", "rect-page", "rect-whole", "rect-group-move", "rect-order"]
                         for count in [1024, 10240] for mode in ["legacy-canvas", "production-retained"] for depth in [1, 3]}
        expected_ui = {(name, 64, mode, depth) for name in ["game-hud-static-overlay", "game-hud-values", "editor-idle", "editor-line-edit"]
                       for mode in ["legacy-canvas", "production-retained"] for depth in [1, 3]}
        assert observed == expected_rect | expected_ui
        validation.append(dict(repeat=repeat, formalCases=len(observed), formalFrames=56 * 40,
                               serialFrames=28 * 40, mandatoryQuickCases=quick_cases, pixelComparisons=pixel_checks))
    assert len(source) == 1 and all(len(v) == 1 for v in hashes.values())
    fields = ["updateAndInputRecording", "referenceRecording", "prepare", "encode", "submit", "submitCompletion", "dispose", "frameWall", "allocatedProcessWide"]
    stage_result = []
    for key, values in sorted(serial.items()):
        assert len(values) == 80
        stage_result.append(dict(name=key[0], count=key[1], mode=key[2],
                                 metrics={field: distribution([v[field] for v in values]) for field in fields},
                                 runMedians={field: [statistics.median(v[field] for v in values if v["repeat"] == r) for r in [1, 2]] for field in fields}))
    pipeline_result = []
    for key, values in sorted(throughput.items()):
        assert len(values) == 2
        pipeline_result.append(dict(name=key[0], count=key[1], mode=key[2], depth=key[3], independentRuns=values,
                                    gpuRecordedSpanMs=distribution([v for row in values for v in row["gpuRecordedSpanMs"]])))
    return dict(sourceRevision=next(iter(source)), validation=validation, stages=stage_result, profiles=pipeline_result,
                pixels={str(key): next(iter(value)) for key, value in hashes.items()},
                notes=["Serial wall/stage percentiles describe 80 individual frames per case; pipeline FPS has two independent observations, not a p95.",
                       "Formal 21/40 warm/sample populations exclude mandatory 3/4 quick checks despite shared UI case names.",
                       "Both variants perform the same input/scene updates; this isolates drawing preparation, not complete legacy application CPU cost.",
                       "rect-page changes one canonical 64-command page; production can recompile its containing <=1024-command run.",
                       "UI timings adapt existing fixture layers; separate exact native checks exercise UiDrawingScene2D itself.",
                       "Target sizes: rectangles 256x256, real UI 1280x720. Do not compare absolute times with older 1024x1024 spikes.",
                       "Elapsed throughput includes final drain and frame retirement; cache-owner disposal is outside that window and also reported in an adjusted FPS.",
                       "Cached bytes are renderer estimates of idle private assets, not driver-wide VRAM."])


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("input", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    if (args.input / "manifest.json").exists():
        with tempfile.TemporaryDirectory(prefix="retained-production-replay-") as temporary:
            base = Path(temporary)
            for entry in json.loads((args.input / "manifest.json").read_text()):
                if entry["phase"] != "accepted":
                    continue
                data = gzip.decompress((args.input / entry["archive"]).read_bytes())
                assert len(data) == entry["bytes"] and hashlib.sha256(data).hexdigest() == entry["sha256"]
                name = Path(entry["source"]).name
                assert name in ["run-1.jsonl", "run-2.jsonl", "environment.txt"]
                (base / name).write_bytes(data)
            result = analyze(base)
    else:
        result = analyze(args.input)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n")
    print(json.dumps(dict(status="passed", source=result["sourceRevision"], validation=result["validation"])))
