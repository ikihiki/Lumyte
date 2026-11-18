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
