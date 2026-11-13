#!/usr/bin/env python3
"""Advisory, bounded component screening. Python standard library only."""
import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import platform
import shutil
import signal
import subprocess
import sys
import time

VERSION = "component-screening-v1"
HARNESS = Path("benchmarks/Lumyte.Component.Screening")
ROOT = Path(__file__).resolve().parents[3]
CATALOG = [
    {"id": f"Lumyte.Signals.Benchmarks.{group}.{method}", "min_delta_ns": floor}
    for group, methods in [
        ("ScalarBenchmarks", [("ReadValue", 1), ("ReadPeek", 1), ("WriteSame", 1), ("WriteUnobserved", 2)]),
        ("AsyncBenchmarks", [("CreateCompleted", 20), ("ReloadCompleted", 20),
                             ("ReloadCompletedObserved", 30), ("SupersedeAndCancelPending", 100)]),
        ("GraphBenchmarks", [("DiamondUpdate", 20), ("SwitchBranch", 20),
                             ("CaptureEightReads", 20), ("CheckReadSet", 5)]),
    ] for method, floor in methods
]


def dump(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(data, indent=2, allow_nan=False) + "\n")
    temporary.replace(path)


def git(repo, *args):
    return subprocess.check_output(["git", "-C", str(repo), *args], text=True).strip()


def tree_hash(path):
    digest = hashlib.sha256()
    for file in sorted(path.rglob("*")):
        if file.is_file() and not {"bin", "obj", "__pycache__"}.intersection(file.relative_to(path).parts):
            digest.update(str(file.relative_to(path)).encode())
            digest.update(file.read_bytes())
    return digest.hexdigest()


def select(paths, force_all=False):
    """Conservatively select; unknown changes run a sentinel, never imply coverage."""
    relevant = [p for p in paths if not (p.startswith("docs/") or p.endswith(".md"))]
    if not relevant and not force_all:
        return [], [], []
    signals = any(p.startswith(("src/Core/Lumyte.Signals/", "benchmarks/Lumyte.Signals.Benchmarks/")) for p in relevant)
    common = any(p in {"global.json", "NuGet.config", "mise.toml", "mise.lock", ".editorconfig"}
                 or p.startswith(("Directory.", "build/", "tools/", ".github/", "benchmarks/Lumyte.Component.Screening/"))
                 or p.endswith((".props", ".targets", ".slnx")) for p in relevant)
    uncovered = [p for p in relevant if not p.startswith(("src/Core/Lumyte.Signals/", "benchmarks/Lumyte.Signals.Benchmarks/", "benchmarks/Lumyte.Component.Screening/", "tools/", "build/", ".github/"))
                 and p not in {"global.json", "NuGet.config", "mise.toml", "mise.lock", ".editorconfig"}
                 and not p.startswith("Directory.") and not p.endswith((".props", ".targets", ".slnx"))]
    cases = CATALOG if signals or common or force_all else [CATALOG[0]]
    omissions = ["Signals computed/fanout/batch/scheduler/shared-DAG/reuse/validation workloads are not covered.",
                 "Mathematics, Execution and Graphics are not covered; sentinel timing does not measure them."]
    return cases[:12], uncovered, omissions


def plan(args):
    repo = Path(args.repo).resolve()
    base = git(repo, "rev-parse", "--verify", args.base + "^{commit}")
    head = git(repo, "rev-parse", "--verify", args.head + "^{commit}")
    # --no-renames includes both deleted and added paths, including changed dependencies.
    paths = subprocess.check_output(["git", "-C", str(repo), "diff", "--no-renames", "--name-only", "-z", base, head], text=True).split("\0")
    cases, uncovered, omissions = select([p for p in paths if p], args.all)
    data = {"schema_version": 1, "harness_version": VERSION, "case_version": 1,
            "harness_sha256": tree_hash(ROOT / HARNESS), "base_sha": base, "head_sha": head,
            "selected": bool(cases), "changed_paths": [p for p in paths if p], "cases": cases,
            "uncovered_paths": uncovered, "omissions": omissions,
            "selection_reason": "manual --all" if args.all else "known Signals/common dependency" if len(cases) > 1 else "unknown-change sentinel" if cases else "documentation-only or empty diff",
            "advisory": True, "case_cap": 12}
    dump(Path(args.output) / "plan.json", data)
    if not cases:
        report(Path(args.output), {"status": "skipped", "plan": data, "advisory": True})
    if os.environ.get("GITHUB_OUTPUT"):
        with open(os.environ["GITHUB_OUTPUT"], "a") as output:
            output.write(f"selected={str(bool(cases)).lower()}\nbase_sha={base}\nhead_sha={head}\n")
    print(json.dumps(data))
    return 0


class Failure(Exception):
    def __init__(self, status, message):
        self.status = status
        super().__init__(message)


class Runner:
    def __init__(self, output, seconds):
        self.output = output
        self.deadline = time.monotonic() + min(seconds, 900)
        self.commands = []

    def call(self, command, cwd, label):
        remaining = self.deadline - time.monotonic()
        if remaining <= 0:
            raise Failure("timeout", "Total execution deadline exhausted")
        log = self.output / (label + ".log")
        self.commands.append({"command": list(map(str, command)), "cwd": str(cwd), "log": log.name})
        with log.open("w") as stream:
            process = subprocess.Popen(list(map(str, command)), cwd=cwd, stdout=stream,
                                       stderr=subprocess.STDOUT, start_new_session=True)
            try:
                code = process.wait(timeout=remaining)
            except subprocess.TimeoutExpired:
                os.killpg(process.pid, signal.SIGKILL)
                process.wait()
                raise Failure("timeout", f"Deadline exceeded in {label}; partial artifacts retained")
        result = log.read_text(errors="replace")
        if code:
            raise Failure("execution_error", f"{label} exited {code}; see {log.name}")
        return result.strip()


def finite(value, name, positive=False):
    if isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value) or value < 0 or (positive and value == 0):
        raise Failure("invalid_data", f"Missing/invalid {name}: {value!r}")
    return value


def read_bdn(directory, cases):
    expected = {case["id"] for case in cases}
    rows = {}
    files = sorted(directory.rglob("*-full.json"))
    if not files:
        raise Failure("missing_data", f"No full BDN JSON under {directory}")
    host_identity = None
    for file in files:
        document = json.loads(file.read_text())
        host = document.get("HostEnvironmentInfo") or {}
        identity_fields = ("BenchmarkDotNetVersion", "OsVersion", "ProcessorName", "Architecture",
                           "RuntimeVersion", "Configuration", "DotNetCliVersion")
        if any(not host.get(key) for key in identity_fields):
            raise Failure("missing_data", "Missing BDN host identity")
        current_host = {key: host[key] for key in identity_fields}
        if host_identity is not None and host_identity != current_host:
            raise Failure("incomparable", "BDN host identities differ between reports")
        host_identity = current_host
        if host["BenchmarkDotNetVersion"] != "0.15.8" or host["Configuration"] != "RELEASE":
            raise Failure("incomparable", "Unsupported BDN version/build configuration")
        for row in document.get("Benchmarks", []):
            identity = row.get("FullName")
            if identity not in expected or identity in rows:
                raise Failure("incomparable", f"Unexpected/duplicate benchmark identity: {identity}")
            required_job = ("ComponentScreening-v1", "OutlierMode=DontRemove", "IterationCount=8",
                            "IterationTime=150ms", "LaunchCount=1", "WarmupCount=3")
            if row.get("Parameters") != "" or any(part not in row.get("DisplayInfo", "") for part in required_job):
                raise Failure("incomparable", f"{identity}: unexpected parameters or job configuration")
            statistics = row.get("Statistics") or {}
            median = finite(statistics.get("Median"), "median ns", positive=True)
            mean = finite(statistics.get("Mean"), "mean ns", positive=True)
            stddev = finite(statistics.get("StandardDeviation"), "standard deviation ns")
            memory = row.get("Memory") or {}
            allocated = finite(memory.get("BytesAllocatedPerOperation"), "allocated bytes")
            actual = [m for m in row.get("Measurements", []) if m.get("IterationStage") == "Result" and m.get("IterationMode") == "Workload"]
            if len(actual) != 8 or statistics.get("N") != 8:
                raise Failure("invalid_data", f"{identity}: expected 8 retained workload results")
            for measurement in actual:
                finite(measurement.get("Nanoseconds"), "measurement nanoseconds", positive=True)
                finite(measurement.get("Operations"), "measurement operations", positive=True)
            rows[identity] = {"median_ns": median, "mean_ns": mean, "stddev_ns": stddev,
                              "allocated_bytes": allocated, "samples": len(actual), "raw_file": str(file),
                              "host_identity": current_host, "hardware_intrinsics": row.get("HardwareIntrinsics")}
    if set(rows) != expected:
        raise Failure("missing_data", f"Missing benchmark identities: {sorted(expected - set(rows))}")
    return rows


def compare(base, head, cases):
    expected = {c["id"] for c in cases}
    if set(base) != expected or set(head) != expected:
        raise Failure("incomparable", "Base/head case identities differ from frozen plan")
    results = []
    for case in cases:
        key = case["id"]
        before, after = base[key], head[key]
        if before.get("host_identity") != after.get("host_identity") or before.get("hardware_intrinsics") != after.get("hardware_intrinsics"):
            raise Failure("incomparable", f"{key}: runtime/CPU/SDK identities differ")
        delta = after["median_ns"] - before["median_ns"]
        percent = 100 * delta / before["median_ns"]
        alloc_delta = after["allocated_bytes"] - before["allocated_bytes"]
        results.append({"id": key, "base": before, "head": after, "delta_ns": delta,
                        "delta_percent": percent, "min_delta_ns": case["min_delta_ns"],
                        "latency_candidate": percent >= 10 - 1e-9 and delta >= case["min_delta_ns"],
                        "allocation_increase": alloc_delta > 0, "allocation_delta_bytes": alloc_delta,
                        "allocation_delta_percent": (100 * alloc_delta / before["allocated_bytes"]) if before["allocated_bytes"] else None})
    return results


def snapshot(source, dest, runner, label):
    # Copy tracked product files only; do not mutate prepared worktrees or copy their dirty/untracked harness.
    archive = runner.output / (label + "-source.tar")
    runner.call(["git", "-C", source, "archive", "--format=tar", "-o", archive, "HEAD"], source, label + "-archive")
    dest.mkdir(parents=True)
    runner.call(["tar", "-xf", archive, "-C", dest], dest, label + "-extract")
    archive.unlink()
    target = dest / HARNESS
    if target.exists():
        shutil.rmtree(target)
    shutil.copytree(ROOT / HARNESS, target, ignore=shutil.ignore_patterns("bin", "obj"))
    (dest / "artifacts/nuget").mkdir(parents=True, exist_ok=True)
    return target / "Lumyte.Component.Screening.csproj"


def report(output, result):
    dump(output / "result.json", result)
    lines = ["# Component screening (advisory)", "", f"Status: **{result['status']}**", "",
             "Single short BDN job, serial base then head. No statistical significance or performance guarantee.",
             "Candidates need dedicated follow-up; allocation increases are independent of latency.", ""]
    if result.get("error"):
        lines += ["Error: " + result["error"], ""]
    for row in result.get("comparisons", []):
        flag = "latency candidate" if row["latency_candidate"] else "no latency candidate"
        if row["allocation_increase"]:
            flag += "; allocation increase"
        lines += [f"- {row['id']}: {row['base']['median_ns']:.3f} → {row['head']['median_ns']:.3f} ns "
                  f"({row['delta_percent']:+.1f}%, {row['delta_ns']:+.3f} ns); "
                  f"{row['base']['allocated_bytes']:g} → {row['head']['allocated_bytes']:g} B/op — {flag}."]
    plan_data = result.get("plan", {})
    lines += ["", "## Compared revisions",
              "- Base: " + plan_data.get("base_sha", "unavailable"),
              "- Head: " + plan_data.get("head_sha", "unavailable"),
              "- Selection: " + plan_data.get("selection_reason", "unavailable")]
    lines += ["", "## Coverage limitations", *["- " + p for p in plan_data.get("omissions", [])]]
    lines += ["- Uncovered changed path: " + p for p in plan_data.get("uncovered_paths", [])]
    if plan_data.get("uncovered_paths"):
        lines += ["- Sentinel results do not qualify uncovered components."]
    lines += ["", "See result.json, command logs, metadata and raw/ for complete or partial evidence."]
    (output / "summary.md").write_text("\n".join(lines) + "\n")


def run(args):
    output = Path(args.output).resolve()
    output.mkdir(parents=True, exist_ok=True)
    result = {"schema_version": 1, "status": "execution_error", "advisory": True, "harness_version": VERSION}
    runner = Runner(output, args.timeout_seconds)
    try:
        if not math.isfinite(args.timeout_seconds) or args.timeout_seconds <= 0:
            raise Failure("execution_error", "Timeout must be finite and positive")
        data = json.loads(Path(args.plan).read_text())
        result["plan"] = data
        if data.get("harness_version") != VERSION or data.get("harness_sha256") != tree_hash(ROOT / HARNESS):
            raise Failure("incomparable", "Plan and runner harness versions/hashes differ")
        if not data["selected"]:
            result["status"] = "skipped"
            return 0
        cases = data["cases"]
        if not cases or len(cases) > 12 or any(c not in CATALOG for c in cases) or len({c['id'] for c in cases}) != len(cases):
            raise Failure("incomparable", "Invalid case catalog in plan")
        locations = {"base": Path(args.base_dir).resolve(), "head": Path(args.head_dir).resolve()}
        sdk_configs = []
        for label, location in locations.items():
            sha = runner.call(["git", "rev-parse", "HEAD"], location, label + "-sha")
            if sha != data[label + "_sha"]:
                raise Failure("incomparable", f"{label} checkout SHA differs from plan")
            config = json.loads(runner.call(["git", "show", "HEAD:global.json"], location, label + "-global"))
            sdk_configs.append(config)
        if sdk_configs[0] != sdk_configs[1]:
            raise Failure("incomparable", "global.json SDK policies differ; cross-SDK comparison unsupported")
        dotnet = shutil.which(os.environ.get("DOTNET", "dotnet"))
        if not dotnet:
            raise Failure("unsupported", "dotnet SDK not available; set DOTNET or PATH")
        # BDN launches dotnet children through PATH, including when DOTNET is absolute.
        os.environ["PATH"] = str(Path(dotnet).parent) + os.pathsep + os.environ.get("PATH", "")
        result["metadata"] = {"cpu": platform.processor(), "machine": platform.machine(), "os": platform.platform(),
                              "cpuinfo": Path("/proc/cpuinfo").read_text() if Path("/proc/cpuinfo").exists() else "unavailable",
                              "environment": {k: v for k, v in os.environ.items() if k.startswith(("DOTNET_", "COMPlus_")) or k in {"LANG", "TZ"}},
                              "dotnet": dotnet, "sdk_policy": sdk_configs[0], "harness_sha256": tree_hash(ROOT / HARNESS),
                              "runner_sha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(), "case_version": 1}
        scratch = output / "scratch"
        if scratch.exists() or (output / "raw").exists():
            raise Failure("execution_error", "Output scratch/raw already exists; use a fresh output directory")
        projects = {}
        for label, location in locations.items():
            projects[label] = snapshot(location, scratch / label, runner, label)
            try:
                sdk = runner.call([dotnet, "--version"], scratch / label, label + "-sdk")
            except Failure as error:
                if error.status == "timeout":
                    raise
                raise Failure("incomparable", f"{label}: global.json SDK cannot be resolved; see SDK log") from error
            result["metadata"][label + "_sdk"] = sdk
            if sdk != sdk_configs[0].get("sdk", {}).get("version"):
                raise Failure("incomparable", "Resolved SDK is not the exact global.json version")
            result["metadata"][label + "_dotnet_info"] = runner.call([dotnet, "--info"], scratch / label, label + "-dotnet-info")
        binaries = {}
        # BOTH build and semantic smoke complete before the first timed job.
        for label, project in projects.items():
            runner.call([dotnet, "build", project, "-c", "Release", "--nologo"], project.parent, label + "-build")
            binary = project.parent / "bin/Release/net10.0/Lumyte.Component.Screening.dll"
            smoke = runner.call([dotnet, binary, "--smoke"], project.parent, label + "-smoke")
            if smoke != "screening-smoke-ok:v1:12":
                raise Failure("execution_error", f"{label} smoke marker absent")
            binaries[label] = binary
            result["metadata"][label + "_binary_hashes"] = {f.name: hashlib.sha256(f.read_bytes()).hexdigest()
                                                              for f in sorted(binary.parent.glob("*.dll"))}
        measurements = {}
        for label, binary in binaries.items():
            raw = output / "raw" / label
            runner.call([dotnet, binary, "--filter", *[c["id"] for c in cases], "--artifacts", raw], binary.parent, label + "-timed")
            measurements[label] = read_bdn(raw, cases)
            result[label + "_measurements"] = measurements[label]
        result["comparisons"] = compare(measurements["base"], measurements["head"], cases)
        result["status"] = "candidates" if any(r["latency_candidate"] or r["allocation_increase"] for r in result["comparisons"]) else "no_candidates"
        if data.get("uncovered_paths"):
            result["status"] += "_partial_coverage"
        return 0
    except Failure as error:
        result.update(status=error.status, error=str(error))
        return 1
    except (OSError, ValueError, KeyError, TypeError, subprocess.SubprocessError) as error:
        result.update(status="execution_error", error=str(error))
        return 1
    finally:
        result["commands"] = runner.commands
        result["elapsed_seconds"] = max(0, min(args.timeout_seconds, 900) - (runner.deadline - time.monotonic()))
        report(output, result)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    planner = commands.add_parser("plan")
    planner.add_argument("--base", required=True)
    planner.add_argument("--head", required=True)
    planner.add_argument("--output", required=True)
    planner.add_argument("--repo", default=".")
    planner.add_argument("--all", action="store_true", help="Force all 12 representative cases (including A/A)")
    execution = commands.add_parser("run")
    execution.add_argument("--plan", required=True)
    execution.add_argument("--base-dir", required=True)
    execution.add_argument("--head-dir", required=True)
    execution.add_argument("--output", required=True)
    execution.add_argument("--timeout-seconds", type=float, default=900)
    args = parser.parse_args()
    return plan(args) if args.command == "plan" else run(args)


if __name__ == "__main__":
    sys.exit(main())
