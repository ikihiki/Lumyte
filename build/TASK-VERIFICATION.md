# Task consolidation verification

Verified on Linux x64 using the pinned SDK/native paths from
/tmp/lumyte-lanes-env.sh; no host configuration was changed.

## Commands and results

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
