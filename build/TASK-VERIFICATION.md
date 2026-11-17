# Task consolidation verification

Verified on Linux x64 using the pinned SDK/native paths from
/tmp/lumyte-lanes-env.sh; no host configuration was changed.

## Current: Windows help inventory line endings

CI run 44191811812 at fc4a298e confirms the redirected-help handle exception
is gone, and Linux integration/GPU logs contain zero unmatched-test warnings.
Windows x64 and ARM64 instead exposed the inventory test parser: unpadded
target names ending in CRLF were missed by its multiline regular expression
(18 expected, four counted). Normalize platform line endings before parsing;
keep the real help exit-zero and exact target-count assertions unchanged.

The new LF/CRLF/CR regression was executed against the old parser: LF passed
and CRLF/CR failed. With normalization, the complete orchestration suite passes
**265 tests, zero failures/skips** (/tmp/lumyte-crlf-after-full.log). The original
failing regression is retained in /tmp/lumyte-crlf-before.log. Release C# format
verification and git diff --check pass. Actual Windows and downstream package
execution remain for the next exact-head CI run.

## Historical: initial CI audit repairs

Fallout 10.5.0 release source still reads Console.BufferWidth in redirected help,
so this repair retains the locked 10.4.0 dependency. A supported initialization
extension prints the real initialized target graph and public ParameterAttribute
metadata, including inherited options, without accessing console dimensions.
The actual redirected CLI must exit zero; no schema-only substitute, permitted
nonzero exit, or exception-swallowing fallback is used.

TestLanes.json now records nonempty project lane membership. The existing xUnit
discovery stream verifies the full unfiltered inventory; fresh evidence is
required after the single SDK test invocation. Missing evidence or declaration
drift fails the common runner even if the adapter returns zero. Host-excluded
class names are not required to exist on every OS.

| Verification | Result | Evidence |
| --- | --- | --- |
| Release runner build | Zero warnings/errors | /tmp/lumyte-repair-build-final.log |
| Complete orchestration suite, no build/restore | **262 passed, zero failed/skipped** | /tmp/lumyte-repair-orchestration-final.log |
| CPU, host restore/build enabled, concurrency 4 | **50 selected projects, 4,181 passed, zero failed/skipped**; fresh discovery inventory accepted | /tmp/lumyte-repair-cpu-final.log |
| Full integration selection, isolated state root | **11 projects, 452 passed, 16 existing opt-in skips, zero failed/empty TRX**; one SDK test command, fresh discovery accepted | /tmp/lumyte-repair-integration-final.log |
| Full GPU selection, isolated state root, opt-ins unset | **9 projects, 159 skipped discovery cases, zero failed/empty TRX**; one SDK test command, fresh discovery accepted; selection proof, not GPU execution | /tmp/lumyte-repair-gpu-final.log |
| Redirected real help | **18 targets, 58 current/inherited options**, exit zero | /tmp/lumyte-repair-help-final.log |
| Runner and build-test C# format verification | Both exit zero, no changes | /tmp/lumyte-repair-format-build.log and /tmp/lumyte-repair-format-tests.log |
| Full Markdown lint | **363 files, zero issues** | /tmp/lumyte-repair-markdown-final.log |

All three managed project sets exactly match the nonempty project sets from
CI 44116989219: CPU 50, integration 11, GPU 9. The separate X11 and cold-capacity
commands are unchanged and are not .slnf members. Integration adds six new
regression cases (462 to 468). With GPU opt-ins unset, the existing four-row
DescriptorReuseGpuTests theory is one skipped discovery case rather than four
executed rows; this explains local 159 versus CI 162 managed GPU cases. No GPU
case was removed. Full GPU/native opt-in execution remains for the parent CI run.

The real SDK fixture now has four projects: mixed, integration-default, CPU-only,
and GPU-only with an integration default. Each lane executes one SDK test
command: CPU/integration/GPU **3/3/3 cases**, **2/2/3 projects**, with no empty
TRX or unmatched-test warning. A separate deliberately stale declaration run
proves omitted GPU membership fails through real discovery. The fixture keeps
the same-class CPU barrier, legacy classes, traits and default semantics.
Five additional validation cases cover malformed/empty presence metadata and
missing discovery evidence; redirected help now covers Test and Compile.
The complete orchestration count therefore increases from 256 to 262; the
CPU partition stays unchanged.

Windows x64/arm64 actual runner execution, GPU opt-in execution, browser/AOT and
four-RID package production remain remote-CI verification responsibilities.
The workflow, native hooks, strict AOT, browser timeout scope and package flow
are unchanged. Local native compilation reuses the pinned verified assets.

## Historical: task-oriented CI interface

The required composition workflow uses **10 → 4 distinct direct target names**:
Compile, Test, Quality, Distribution. Across all workflow files, direct target names are **14 → 8**: the four required
CI tasks plus the unchanged three advisory trust stages and DiagnosticPreviews.
Actual declarations/schema/help expose
**43 → 18** (original baseline 85), including developer, native/tooling,
publication and three separate advisory trust-stage tasks. Internal preparation,
format workers and output leaves were removed as targets, not hidden.

| Verification | Result | Evidence |
| --- | --- | --- |
| Build runner, Release, no restore | 0 warnings/errors | /tmp/lumyte-interface-build-final.log |
| Complete orchestration suite | **256 passed, 0 failed/skipped** | /tmp/lumyte-interface-tests-final.log |
| Common Test CPU lane, host restore/build enabled, concurrency 4 | **50 assemblies, 4,181 passed, 0 failed/skipped**; exactly one SDK test command and 50 canonical .slnf members | /tmp/lumyte-interface-cpu.log |
| Isolated-root common Test integration lane, selected build-tests project | **160 passed, 0 failed/skipped**; exactly one real SDK test command using canonical source outside invocation root | /tmp/lumyte-interface-integration.log |
| Build C# format verification | No changes | /tmp/lumyte-interface-format-final.log |
| Full repository Markdown lint | 363 files, zero issues | /tmp/lumyte-interface-markdown-final.log |
| Actual help/schema | 18 executable tasks; removed aliases absent | /tmp/lumyte-interface-help.log and .fallout/build.schema.json |
| Whitespace diff check | Clean | git diff --check |

The full orchestration run includes the real SDK solution-filter fixture
(CPU/integration/GPU), all 11 UI and seven strict-AOT selectors, native isolated-root
invocations, package checks and failure propagation. New tests bound the required
workflow to its exact four-task interface and preserve platform/artifact gates,
reject removed target names and invalid selectors, exercise all three Quality
checks and failure boundaries, verify metadata-before-package ordering, and
retain diagnostic generator/assembler/collector and GPU pipeline output checks.

The current CPU count adds two CPU-only workflow/hook contract tests to 4,179;
no prior CPU tests were removed. CPU method scheduling and the lane generator
are unchanged. Native MSBuild calls now use a common backend selector while
retaining hook ordering, package staging, isolated roots and no solution recursion.
The earlier statement that native leaf names were unchanged applies only to the
historical checkpoints below, not this interface follow-up.

Windows/ARM, real GPU/browser publication, every AOT executable, and four-RID
NuGet production were not rerun locally. Their contracts and workflow gates
remain; fake-process assertions are orchestration evidence, not native execution
claims. Local host compilation reuses the already verified native assets.

## Historical: original consolidation checks

The following commands were run from the repository root after sourcing that
local environment. Logs are retained on the verification host.

| Command suffix | Result | Log |
| --- | --- | --- |
| dotnet build build -c Release --no-restore | 0 warnings, 0 errors | /tmp/lumyte-build-final.log |
| dotnet run --project build -c Release --no-build --no-restore -- --target Test --lane cpu --host-only --test-concurrency 4 | 50 assemblies, **4,179 passed, 0 failed, 0 skipped**; restore/build and all lane tests succeeded | /tmp/lumyte-cpu-final.log |
| dotnet test tests/Tools/Lumyte.Build.Tests -c Release --nologo | **208 passed**, including real Fallout task option/invalid selection/feature/AOT/inventory and native orchestration regressions | /tmp/lumyte-orchestration-final.log |
| dotnet build/bin/Release/net10.0/_build.dll --target Test --suite ui --feature menus --no-build --no-restore | **151 passed** (77 Controls, 74 Windowing) and the real explicit-font menu/tooltip/editor-preedit sample succeeded | /tmp/lumyte-ui-menus-final.log |
| LUMYTE_CPU_METHOD_PARALLELISM=4 dotnet test tests/Tools/Lumyte.TestRunner.Tests -c Release --nologo | **9 passed**, including deterministic same-class barrier overlap with reviewed CPU-only assembly override, async concurrency cap, fixtures/output, theories, cancellation and serial collection boundaries | /tmp/lumyte-method-overlap-final.log |
| dotnet build/bin/Release/net10.0/_build.dll --target Test --suite browser-contracts | **220 passed**, 0 failed/skipped across four Node contract groups (186/8/17/9), including narrow timeout quarantine | /tmp/lumyte-browser-contracts-final.log |
| dotnet build/bin/Release/net10.0/_build.dll --target Benchmark --benchmark-area core --benchmark-project Lumyte.Signals --benchmark-list --output artifacts/task-options-signals-catalog | completed, 1 project, **60 compiled catalog cases**, no timing claims | /tmp/lumyte-benchmark-catalog-final.log |
| dotnet build/bin/Release/net10.0/_build.dll --help | actual help and regenerated schema expose **43 targets**, matching declarations (previously 85) | /tmp/lumyte-target-help-final.log |
| mise exec -- markdownlint-cli2 | full repository Markdown check, zero issues | /tmp/lumyte-markdown-final.log |
| git diff --check | clean | command output |

Runner regressions also passed with its scheduling environment unset and set to
1 during development. The CPU lane clears ambient test/GPU/benchmark opt-ins and
injects the selected bound; fake-process regression asserts that integration/GPU
clear the CPU bound and explicitly force serial test collections.

## Preserved contracts and limits

All 11 prior managed UI feature scenarios and all seven strict-AOT feature
publication scenarios have orchestration coverage; child publication failure is
propagated. Existing native leaf/MSBuild invocation, isolated state roots,
repository paths, package asset validation and Windows/ARM job matrices remain.
No native MSBuild leaf target was renamed.

Windows/ARM execution, four-RID package publication, full real-device GPU execution,
complete browser/WASM publication and every native AOT executable were not rerun
locally. Their CI gates and existing strict warning/opt-in/timeout contracts are
preserved, not bypassed. Native local builds reused the supplied verified assets.
Benchmark verification was a catalog run, not a performance measurement.

Frozen ADRs and archived performance evidence retain historical target names and
hashes; current callers and current documentation use the migration table in
[TASK-MIGRATION.md](TASK-MIGRATION.md). No ADR lint exceptions were added or updated.

## Historical: solution-filter follow-up

The classified lanes now issue exactly one SDK test command, replacing the
original per-project loop. These checks supersede the original CPU/orchestration
execution records above; target inventory remains 43.

| Command | Result | Log |
| --- | --- | --- |
| dotnet run --project build -c Release --no-build --no-restore -- --target Test --lane cpu --host-only --test-concurrency 4 | **One dotnet test**, canonical CPU .slnf with 50 projects; **4,179 passed, zero failed/skipped**; full host restore/compile also succeeded | /tmp/lumyte-slnf-cpu-full.log |
| dotnet test tests/Tools/Lumyte.Build.Tests -c Release --nologo | **209 passed**, including solution-filter membership, one-command assertions, all original feature/native regressions and real SDK fixture | /tmp/lumyte-slnf-orchestration.log |
| dotnet build/bin/Release/net10.0/_build.dll --root "$PWD/artifacts/slnf-integration-state" --repository-root "$PWD" --target Test --lane integration --project tests/Tools/Lumyte.Build.Tests --no-build --no-restore | **One dotnet test**, one selected project, **115 integration tests passed**; canonical source outside isolated invocation root | /tmp/lumyte-slnf-integration.log |
| dotnet test tests/Tools/Lumyte.Build.Tests -c Release --nologo --filter FullyQualifiedName~SolutionFilterSdkTests --logger "console;verbosity=detailed" | **SDK 10.0.403**, canonical two-project .slnx; one real .slnf test invocation per lane: CPU 2 / integration 3 / GPU 2 cases; expected membership 1 / 2 / 2 | /tmp/lumyte-slnf-sdk-fixture.log |
| dotnet build build -c Release --no-restore | 0 warnings/errors | /tmp/lumyte-slnf-build.log |

The SDK fixture lives in a Unicode/spaced temporary checkout, imports the actual
repository test hook, and verifies both legacy mixed-class and untagged
integration-default predicates. Its CPU assembly is deliberately named
Fixture.Fake.IntegrationTests; the same-class barrier proves that project naming
cannot silently disable CPU method scheduling. Integration/GPU still use actual
traits and the explicit serial environment boundary.

Concurrent ordering exposed an existing malformed shader snapshot test fixture:
clearing DiagnosticFields alone retained DiagnosticType, whose process cache
could substitute valid metadata. The test now explicitly clears both deferred
providers and asserts that the malformed Fields array is the one being validated.
Its exception assertions remain unchanged; no test is skipped or serialized to
mask the issue. SDK fixture build workers disable node reuse/shared compilation
only for their own child process, avoiding inherited redirected-stream lifetimes;
no host settings or production build policy is changed.
