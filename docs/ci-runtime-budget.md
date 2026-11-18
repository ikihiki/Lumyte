# Required CI runtime budget

## Baseline (2026-11-18)

Source: main commit `dd0831439bfcbdbfd1987404720d0a56b4b258de`,
[required CI run 44203736269](https://github.com/ikihiki/Lumyte/actions/runs/44203736269).
The preceding successful run was 44198183455 (approximately 30 minutes).
These are hosted-runner observations, not local timing estimates.

| Job / step | Observed duration | Outcome |
| --- | ---: | --- |
| Browser job | 1,815 s | Cancelled at the existing 30-minute deadline |
| Browser setup | 1,304 s | Completed |
| Browser suite | 418 s | Interrupted during application publication |
| GPU job | 1,458 s | Passed |
| GPU preparation | 378 s | Passed |
| Unique-empty-cache maximum drawing guard | 17 s | Passed |
| GPU backend / pixel contracts | 1,039 s | Passed |

Browser setup was dominated by the runner's regional Ubuntu package mirror:
the 61.2 MB CJK font download occupied approximately 936 s, and the
17.5 MB Mesa driver download approximately 273 s between log entries.
Tool installation itself took about four seconds in that cold setup.
Saving only a toolchain cache would not address that primary cause.

GPU TRX results identify real shader/pipeline costs, not a hung SDK runner:
WgpuFixedRecordsAsync took 384.942 s, WgpuNormalizedClipSamples 125.981 s,
VulkanFixedRecords 60.553 s, WgpuLayersEffects 51.627 s,
WgpuRectangleBatch 48.666 s, WgpuProgramCapacities 39.062 s, and
WgpuSharedStorageFallback 35.355 s. The complete test durations sum to
approximately 1,040 s. Therefore **cold required CI cannot currently meet
10 minutes**, even with zero GPU setup cost. Warm-cache time remains unmeasured;
a cache hit is not a timing result.

## Work reduction without weaker gates

- CI Ubuntu setup opts into a private source list using the official Ubuntu
  HTTPS archive/security endpoints and the existing Ubuntu signing keyring.
  It does not edit host source lists, disable signature/hash checks, pin stale
  packages, or change local setup defaults. Both CJK fonts and Vulkan packages
  remain required. The helper fails closed outside Ubuntu noble GitHub CI.
- Standard Actions caches retain only downloaded Debian archives; APT checks
  them against fresh signed indexes before use. SDK/workload and archive
  checkpoints are saved after successful setup, before Browser assertions,
  so a later assertion failure or timeout does not prevent warming them.
  Native helper toolchains and package downloads retain separate success-only
  caches: an early empty checkpoint cannot freeze those late-installed tools.
- Browser uses eight isolated jobs: contracts, graphics, text, application,
  navigation, pages, transitions, and audio. Each has its own checkout/output
  tree; publication stays serial within a job. No concurrent writers share
  managed/native build paths. The transitions case still executes normal,
  sequential, and same-content verification. Audio still publishes and executes
  both samples. No case selector means the complete existing Browser suite;
  invalid/incompatible selectors fail before starting any tool.
- Each Browser artifact has a case-specific name. The package job still needs
  the entire Browser matrix as well as every other existing required gate.
  Native RID and managed Parley package asset producers are unchanged. The text
  case validates its own Parley WASM publication; UI jobs independently build
  their required WASM references rather than reusing another job's mutable tree.
- Ordinary Linux GPU tests may reuse Mesa's compiled shader disk cache only for
  an exact OS/architecture, installed driver-library/package closure, pinned
  compiler/toolchain, and tracked source/input closure. No prefix restore or
  managed/source binary output cache is used. The ordinary cache is restored
  **after** the distinct maximum drawing guard, which continues using a unique
  empty cache. A source, test, native dependency, setup, or workflow change
  invalidates the ordinary cache. Mesa additionally validates its own entries.

No timeouts, assertions, quarantine scope/expiry, CPU concurrency bounds,
Windows/ARM/native/AOT/strict/distribution checks, four public CI tasks, or
18-target inventory are relaxed. GPU remains one category-wide SDK invocation
over its canonical solution filter, not per-project loops or a smaller subset.
No paid runner or external service is introduced. More Browser jobs can increase
aggregate hosted-runner minutes even when elapsed critical-path time decreases.

## Reproduce and report actual timing

Run the required `composition.yml` workflow, not a preview/advisory workflow.
Record the commit, run attempt, runner image, job conclusions and cache hit/miss
messages. Obtain timestamps and logs with the standard GitHub CLI:

```sh
run=44203736269
gh api "repos/ikihiki/Lumyte/actions/runs/$run" > run.json
gh api --paginate "repos/ikihiki/Lumyte/actions/runs/$run/jobs?filter=latest&per_page=100" > jobs.json
gh run view "$run" --log > run.log
gh run download "$run" --pattern 'test-results-*' --dir test-results
```

For each job and step, subtract `started_at` from `completed_at` (UTC).
Use the run's `run_started_at` and the latest required-job completion for the
execution critical path; separately report queue time from `created_at`.
Sum billed/job durations only as aggregate runner work, never as wall time.
Parse TRX `UnitTestResult.duration` to attribute expensive cases; distinguish
skipped tests and quarantined inconclusive captures from passing assertions.

Compare an explicitly cold run with a repeat of the **same source and driver
closure** for a warm result. A different source key or runner image is a new
cold GPU cache, not a warm regression. Do not delete or prewarm the unique cold
capacity cache. Report the terminal package gate too: successful shards alone
do not establish successful required CI. A ten-minute target is an evaluation
criterion, not a promised outcome or permission to drop remaining coverage.

## Local validation boundary

With SDK 10.0.403 and the existing native dependency environment (without running
host setup), the complete Linux x64 CPU category passed 4,192 tests in 50 projects,
with zero failures or skips, through one SDK solution-filter invocation. The
build-orchestration project passed 289 tests, including the real SDK filter
fixture and 24 added selector/workflow cases. The real Node Browser-contract
entry point passed 228 tests, including eight CI APT safety checks.

Release builds, managed formatting, YAML parsing/shell syntax, the configured
364-file Markdown lint, and diff whitespace checks passed. Actual hosted APT
mirror throughput, cache save/restore behavior, the full published Chrome
samples, and all remote Windows/ARM/AOT/package gates still require the new
required-CI run. These local results do not establish a ten-minute hosted run.

## Diagnostic browser startup follow-up (PR 183)

Required CI run `44227864237` failed the Linux x64 diagnostic UI step at the
15-second Chromium startup deadline. Its final browser artifact contains a valid
`DevTools listening` line, but that line has no receipt timestamp; it does not
prove the endpoint was available before the deadline. The regex matches the
artifact. The runner browser version was not recorded. Neither a regex defect
nor a particular Chrome-version regression has been reproduced.

The diagnostic UI now installs the same pinned Chrome for Testing
`156.0.8078.47` as the Browser matrix, records its version, suppresses first-run
and default-browser prompts, and discovers Chrome through the owned profile
`DevToolsActivePort` rendezvous file. Success requires both a page target and an
open page CDP WebSocket within the original 15-second budget, including discovery
and connection. Spawn errors and early exits fail immediately. Bounded startup
milestones use elapsed receipt times; stderr is retained separately. There is no
second browser launch, GPU fallback, timeout increase, or relaxed UI assertion.
Existing repository Node CDP verifiers already use built-in fetch/WebSocket;
this narrowly scoped helper reuses that approach without adding a second browser
automation dependency (the Python audio lane separately uses Playwright).

Local verification with SDK `10.0.403` and existing Release/native assets:

- All 236 Browser contract tests passed, including eight startup contracts and a
  real pinned full-Chrome startup/transport check; no skips.
- The build-orchestration Release project compiled with zero warnings/errors;
  all 289 orchestration tests passed without skips. YAML parsing, the configured
  364-file Markdown lint, and diff whitespace checks also passed.
- Full Chrome `156.0.8078.47` opened CDP in 162 ms, but the complete UI verifier
  hit the existing 10-second `Page.navigate` deadline both before and after this
  change. This local navigation issue is not attributed to the CI startup failure.
- An explicit local headless-shell control (`156.0.8078.4`, not the CI browser
  and not a fallback in the verifier) passed every original UI assertion: HTTP
  and MagicOnion operations, settings, animation, render-graph capture, dashboard
  isolation, Blazor authentication/logout, and zero browser errors. Its page CDP
  transport opened in 107 ms.

Raw local logs and galleries are under
`artifacts/ci-runtime-review/diagnostics-ui-{before,after,headless-control}`
(and `diagnostics-ui-headless-final-receipts` for the final receipt logging);
contract/build logs use the `diagnostics-startup-` prefix there. These results
validate the startup protocol and preserve functional coverage; only the next
required hosted CI run can establish the repair on the failing runner.

## Three additional countermeasures (PR 183)

The latest green required run, [44257429390](https://github.com/ikihiki/Lumyte/actions/runs/44257429390),
took **25:48** overall. Linux ARM took 1,499 s (setup 772, build 283,
integration 403); Windows ARM 1,239 s (setup 119, build 433, tests 487,
post-cache 164). GPU took 824 s (preparation 308, tests 466). AOT queued for
8:34, then spent 37 s preparing and 921 s publishing 14 projects serially.
These observations supersede earlier bottleneck attribution, not earlier gates.

### DXC installed-artifact checkpoint

Linux ARM setup was dominated by DXC, **not APT**: the pinned
release commit `b106a961d09221b3c5bdb37be45b679257da08b8` built 1,291 CMake/Ninja steps in 696 s; complete
DXC setup took 716 s. APT took 19 s and mise 7.6 s. The old fixed cache key
and existence-only version stamp could neither establish compiler provenance nor
preserve that work when later assertions failed.

The shared Linux setup action provisions OS packages and pinned tools first,
then restores installed DXC libraries, license and provenance only. Runtime
verification compares complete setup/flags/workaround inputs, release commit,
architecture, compiler/support binaries, package ABI and private sysroot closure,
and hashes every installed library and license. Custom external toolchain flags
fail closed to rebuilding. A verified hit invokes neither Git nor CMake.
An invalid/missing/corrupt entry follows the existing release download/source-build
path. The successful verified installation is checkpointed immediately before
remaining native setup and SDK checks, not at the end of the test job.

Actions cache discovery uses an input-specific candidate prefix and unique
run/attempt publication suffix; runtime provenance remains authoritative even
when a restored candidate was produced by a different compiler/runner ABI.
This avoids immutable rejected entries permanently blocking a replacement.
Only standard cache ref isolation applies: a PR may read its accessible trusted
default-branch cache, but never writes/promotes a PR entry to main. Normal
successful main CI naturally publishes the corresponding main cache. That
cross-ref warm behavior cannot be proven before authorized publication/main CI.
Markdown-only changes do not invalidate the DXC input closure.

### Two independent NativeAOT groups

The required AOT matrix has exactly two isolated workspaces, `balanced-1` and
`balanced-2` (9 and 5 projects). Their estimated publication sums from the
hosted per-project log are **460.5 s / 456.6 s**; those are scheduling estimates,
not measured new hosted job times. Both groups remain package dependencies.
No per-project parallel writers share a checkout. Omit `--aot-group` to execute
the original full suite. Invalid/incompatible selectors fail before tools.
The authoritative manifest retains all 14 publications, original strict warning
switches, executable arguments/font paths and every audio invocation (including
OpenAL modes). Regression tests require exact disjoint union and propagate both
publication and executable failures.

### Compiled native reuse and category-limited builds

Native compilation reuses standard Cargo target state only, never managed
outputs or an unverified staged native binary. Ordinary Native/MSBuild targets,
locked source checks, native binary/RID/provenance verification and regenerated
license notices/package gates still execute on a hit. No timestamp-only build
skip or blanket `BuildNative=false` is introduced in CI. Compiler/toolchain,
source, lockfile, patches, headers, build-script and license inputs are cache
identity boundaries; incompatible RIDs cannot share compiled state.

Classified Test compilation now generates a matching canonical solution filter
from `build/TestLanes.json` and `Lumyte.slnx`; the SDK builds its transitive
ProjectReference closure. GPU preparation explicitly uses `--build-lane gpu`.
There is no separate manually maintained project list. Unclassified new tests
fail before tools. Full host Compile remains the default for native producers,
samples and quality workspace validation. Each category still has exactly one
SDK test invocation. CPU method concurrency and CPU purity, serial GPU execution,
the unique-empty cold guard and the ordinary driver cache boundary are unchanged.

### Verification and remaining timing boundary

Local deterministic tests cover DXC changed inputs, corrupt/missing outputs,
fresh hits without Git/CMake, miss fallback, AOT partition/failure/strict modes,
Cargo cache identity and category-filter/new-project guards. Logs are retained in
`artifacts/ci-countermeasure`. Local native warm compilation is evidence only for
that host, not a hosted ARM cold/warm estimate. Full Linux CPU and relevant
integration/orchestration, Node, formatting, YAML/shell and Markdown checks are
recorded with the implementation review.

**The ten-minute goal remains unmet/unproven until terminal required CI.** Queue
time, two-group runner availability, cache transfer overhead, cold ARM compilation
and remaining Windows ARM work must be measured. No timeout, native packaging,
Windows ARM, licensing, provenance, strict-warning or required gate is relaxed;
no paid runner, new service or GPU parallelism is introduced.

Local implementation validation (SDK 10.0.403, Linux x64):

- Complete scoped CPU: **3,835 passed, zero failures/skips, 49 projects**, one
  SDK category invocation. The catalog, not the historical 50-project count,
  determines the selected roots.
- Complete build orchestration: **334 passed**, zero failures/skips. Relevant
  classified build integration: **218 passed**, zero failures/skips.
- Native Python contracts: **27 passed** (nine identity, eighteen native/source
  preparation/provenance). Node Browser contracts: 236 tests, 235 passed,
  one optional real-Chrome check skipped because Chrome was not configured.
- Real Linux x64 DXC release download/checksum/extraction/license publication
  succeeded; the matching second invocation reported a verified hit with no
  external child command. This is not an ARM source-build timing result.
- The initial truly empty DXC installation exposed and fixed a missing-parent
  manifest deletion bug; the actual release archive also exposed separate
  Microsoft/LLVM licenses, now both preserved in the hashed installed notice.
- Build/test managed formatting, configured 370-file Markdown lint, workflow
  and action YAML parsing, shell syntax and diff whitespace checks passed.

Local managed tests used the pre-existing explicitly configured native output
paths in `/tmp/lumyte-lanes-env.sh`; they do not prove a fresh native compile.
Native source/RID/binary verifier tests and the real native warm probe are
separate evidence. Hosted Windows/ARM compilation, cache transfer behavior,
all 14 real AOT publications and terminal package completion remain CI-only.

### Fresh GPU build auxiliary executable

GPU compilation also includes the DeviceCaps pipeline-validation executable.
Its single authoritative project identity is owned by the validation task and
consumed by the GPU build filter, checked against the canonical solution. The
SDK test filter remains test-only; CPU/integration build scopes exclude this
auxiliary root. This fixes a fresh-checkout gap that existing local full-workspace
outputs could conceal. A clean real-SDK fixture verifies the executable is built,
the test filter excludes it, and default full Compile still retains samples.
Neither the pipeline gate nor its `--no-build` contract is changed.
