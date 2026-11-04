# ADR-0027 paired GPU measurements

This driver runs the public Canvas/Scene GPU executable in independent processes. Build and finish correctness gates before starting it. Do not run other builds, tests or benchmarks concurrently. It does not implement or claim the other ADR scenario families.

```sh
source tools/setup/activate.sh
python3 -m unittest discover -s tools/benchmarks/graphics-scenarios -v
unset DOTNET_TieredCompilation DOTNET_TieredPGO COMPlus_TieredCompilation COMPlus_TieredPGO
VK_DRIVER_FILES=/usr/share/vulkan/icd.d/radeon_icd.json python3 tools/benchmarks/graphics-scenarios/run-pairs.py --update one --locality dispersed --granularity 64 --depth 1 --output artifacts/graphics-benchmarks/local-one-depth1
```

After building the extension projects, `python3 tools/benchmarks/graphics-scenarios/check-extension-cli.py` verifies invalid CLI rejection before GPU initialization, catalog counts and Text BenchmarkDotNet passthrough. It intentionally selects a nonexistent ICD and is not a GPU correctness or performance test.

Use `--depth 3` for the separate three-slot profile. Use `--aa` for independent Canvas/Canvas noise observations, not an optimization comparison. Output directories must not already exist; the driver will not overwrite previous observations.

Each variant first receives a 2,000-frame pilot. Its four most recent 50-frame host/GPU median windows must fit the runner's 1.05 stability criterion. The larger recommended warmup is fixed for both variants. Unstable pilots remain explicitly unstable, rather than silently relaxing thresholds. The measured frame count is fixed before formal processes to at least 2,000 and 120% of the larger pilot-derived ten-second budget. Any formal duration shortfall is retained and invalidates formal qualification; there is no favorable-value stopping rule.

Five independent pairs run in alternating A/B and B/A order. Each process produces its own manifest, raw frames, exact/analytic checks and summary. The comparison reports process-level ratios without pooling all frames as independent samples. All measured durations include final GPU drain/retirement. These are headless measurements, not displayed FPS, input-to-display latency or exclusive GPU occupancy.

The default profile is a starting point, not a Cartesian sweep. Select static/one/1%/10%/all, locality, commands per content node and slot depth explicitly. Preserve source hashes, cache budgets, profile and runtime settings when comparing runs. Generated measurements remain under ignored `artifacts/`; do not add them to Git.

## Extension independent-process campaigns

`extension_pairs.py` adds standard-library-only adapters for the actual Text, Effects, UI
(stdout JSONL with frame/completion joins), and Primitives artifact schemas. Build and
finish correctness gates first, then stop other builds/tests/benchmarks. The default
for **each family is selfsame independent A/A noise**, never a quality-changing A/B.
The CLI intentionally has no second method/profile option: vector/MSDF/MTSDF are
separate Text quality profiles and cannot be compared as equal-quality implementations.

```sh
VK_DRIVER_FILES=/usr/share/vulkan/icd.d/radeon_icd.json python3 tools/benchmarks/graphics-scenarios/extension_pairs.py --family Text --output artifacts/graphics-benchmarks/text-aa
VK_DRIVER_FILES=/usr/share/vulkan/icd.d/radeon_icd.json python3 tools/benchmarks/graphics-scenarios/extension_pairs.py --family Effects --profile isolation-1-1080p --pilot-only --output artifacts/graphics-benchmarks/effects-pilot
VK_DRIVER_FILES=/usr/share/vulkan/icd.d/radeon_icd.json python3 tools/benchmarks/graphics-scenarios/extension_pairs.py --family Ui --output artifacts/graphics-benchmarks/ui-aa
VK_DRIVER_FILES=/usr/share/vulkan/icd.d/radeon_icd.json python3 tools/benchmarks/graphics-scenarios/extension_pairs.py --family Primitives --pilot-only --output artifacts/graphics-benchmarks/primitives-pilot
python3 tools/benchmarks/graphics-scenarios/extension_pairs.py --verify-archive artifacts/graphics-benchmarks/text-aa/evidence.tar.gz
```

The actual device manifests must identify non-CPU Radeon 8060S (1002:1586), not
merely an ICD environment variable. The driver records source hashes, raw dirty
patch and untracked source hashes, complete runner-directory binary/native/font/
shader payload hashes, CPU/OS/runtime and selected environment before and after
each subprocess, outside its measured interval. A mutation or incompatible
source/device/profile/runtime invalidates the campaign. Commit intended source
changes before running; output belongs under ignored `artifacts/`, not the source tree.

Two independent pilots use the fixed `--pilot-frames` budget (200..2000; default
2000). Four nonoverlapping 50-frame host/GPU median windows must have max/min
<=1.05. Window starts are phase-matched for Text's two-state trace and U01's
6/12-frame update cadence. Other UI traces have monotonic/one-time events or
no declared phase-equivalent pilot contract and are explicitly rejected for formal
use, rather than pretending arbitrary adjacent windows prove stability.

Before any formal subprocess, the complete command plan freezes the larger
pilot warmup, a common >=2000 measured-frame budget with 20% headroom over the
largest pilot-derived ten-second estimate, and five alternating AB/BA pairs.
`--max-frames` (2000..100000) and `--timeout` (1..86400 seconds per process) are
predeclared hard limits, not reasons to shorten the formal duration requirement.
Duration shortfalls are retained as not-qualified, never rerun to a favorable value.
Per-process host/GPU median, p95, max and `--budget-ms` exceedance counts and
process-level pair ratios are reported; frames are never pooled across processes.

Primitives measurementVersion primitive-queue-timestamps-v2 uses native queue
start/end timestamp pairs across the ordered submission sequence, read after all
submissions complete and before slot reuse. The span includes barriers, uploads,
draws and potential queue starvation between multiple submits; it is **not GPU
busy time**. The adapter validates GPU query status against actual device timestamp
instrumentation. Unsupported and legacy observations remain null and cannot pass
the joint host/GPU stability gate. The executable runs both depths and all subcases
of the selected G01/G02/G05/G06 family, each retained separately.
Ordinary/image-transfer runners do not currently expose the needed warmup
contract and have **no formal adapter**. No completion wall time is substituted
for missing GPU timing.

Process failures/timeouts, invalid/incomplete raw frame IDs, completion/query
evidence, reference failures, unstable pilots and duration shortfalls remain on
disk. A deterministic sorted tar+gzip archive (zero timestamps/ownership, fixed
mode, no gzip filename) and SHA-256 membership manifest include all evidence;
archive creation immediately verifies every member hash. `--verify-archive` is
read-only and does not execute GPU work. This validates archive integrity, not
an authenticated claim about benchmark correctness.

Unit tests use synthetic artifacts and mocked subprocesses only:
`python3 -m unittest discover -s tools/benchmarks/graphics-scenarios -v`.

Core run-pairs.py now rejects incomplete logical frame sequences, absent GPU/query or
completion evidence, missing exact checks and incompatible manifests/content provenance,
and cross-variant input IDs. Unstable pilots do not start formal processes; a formal
duration shortfall writes its report then returns failure. Prior raw evidence is not
rewritten or relabeled.

Validator regression coverage also reads retained real artifacts when available
(test_retained_evidence.py, otherwise explicit skips). Smoke evidence is accepted
only through the Python validation API's explicit allow_smoke=True testing option,
never by the campaign CLI. Core frame sequences use the native warmup+1 offset;
startup/initialization check rows remain separate from exact pixel checks. Process
identity excludes measured initialization timings. Core reports now include per-stage
median/p95/max/budget distributions and paired absolute B-minus-A stage deltas.

Extension non-pilot campaigns return failure after retaining comparison, failure log
and verified archive when stability, execution limits or formal duration disqualify
measurement. Pilot-only may return success with explicit pilot/stability status.
Unsupported/missing GPU timestamp capability is excluded from formal qualification,
not filled with a CPU proxy. Every observed native frame-stage timing (including UI
joined completion/retirement) has a separate per-process distribution. Pair reports
include absolute host/GPU median deltas and five-pair median/min/max, sample standard
deviation and median absolute deviation; these dispersion measures use process pairs,
never pooled frames. Core reports similarly summarize pair ratios and absolute stage
metric deltas across independent processes.

## Reproducible campaign inventory

After qualification and evidence archive creation, run `summarize-campaigns.py`
with an artifact root and ignored JSON output path. It retains rejected campaigns
in the inventory, distinguishes A/A noise from A/B implementation comparison,
checks minimum duration/frame/warmup qualification, and verifies archive membership
and SHA256. Compressed archive bytes, per-member uncompressed bytes and per-process
dispersion remain auditable. Generated numerical reports do not belong in Git.

```sh
python3 tools/benchmarks/graphics-scenarios/summarize-campaigns.py --root artifacts/graphics-benchmarks/adr-completion --output artifacts/graphics-benchmarks/adr-completion/campaign-inventory.json
```
