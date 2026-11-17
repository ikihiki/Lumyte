# Task option migration

Required CI now uses **four direct task entry points**: Compile, Test, Quality,
and Distribution (**10 → 4** distinct direct targets in composition.yml).
Across all workflow files, direct target names are **14 → 8**: the four required
CI tasks plus the unchanged three advisory trust stages and DiagnosticPreviews.
Actual target declarations are **85 → 43 → 18**. These are different metrics;
removed internal leaf declarations do not survive as hidden targets or aliases.

The installed maintained Fallout.Build 10.4.0 package is retained. Its released
Target/Executes/DependsOn model supports ordinary awaited task bodies and real
task dependencies. No new scheduler or private API is needed. Independent
named-color generation and the legacy Signals benchmark still depend on Compile.
Preparation inside a task is now plain ordered methods: validate, restore,
compile, execute; a failed preparation cannot continue to its consumer.

## Small public CI interface

| Task | Responsibility |
| --- | --- |
| Compile | Restore and build the host solution or explicit project |
| Test | Common lane/suite/feature selection; optional GPU pipeline validation output |
| Quality | All managed/native/Markdown checks on Linux; explicit managed scope on Windows |
| Distribution | Samples plus packages by default; packages/window-sample/diagnostic-gallery outputs |

The final four-RID package job requests packages with no-build and explicit
restore-metadata: metadata restoration must succeed before packaging downloaded
outputs, without rebuilding them. No-build alone still skips restore/build.
Restore-metadata rejects contradictory no-restore or non-package selections.
Gallery output retains generator/assembler/collector tests without unrelated
solution compilation. Windows window publication runs on both architectures;
Windows packaging remains limited to the original two projects on x64.

The Linux cold-cache capacity test stays before the GPU lane. Optional
pipeline-validation retains the exact DeviceCaps Vulkan sample and JSONL path;
it is not a BenchmarkDotNet launcher. It rejects non-managed/non-GPU selections.
Browser deadline/quarantine settings, native asset producers, package job
prerequisites, Windows/ARM matrices and gallery artifact consumers are preserved.

Tooling-facing Native, Setup and Verify are not extra CI job categories. Native
selects a backend without whole-solution compilation; MSBuild hooks retain
isolated Fallout state roots and the actual repository root. Setup selects a
tool; Verify selects an environment/sdk/native scope. These are separate, small
domain APIs, not a giant dispatcher. Format shares Quality scopes for deliberate
local fixes; Quality never fixes files. Advisory screening remains split into
three stages across its credential/trust boundary.

## Common interfaces

Append these examples after: dotnet run --project build -c Release --

    --target Test --lane cpu --host-only --test-concurrency 4
    --target Test --lane integration --host-only
    --target Test --lane gpu --host-only
    --target Test --suite ui --feature menus
    --target Test --suite ui --feature menus --aot
    --target Test --suite browser
    --target Benchmark --benchmark-area graphics --benchmark-list
    --target Benchmark --benchmark-area ui --benchmark-integrated --benchmark-mode screen
    --target Benchmark --benchmark-area core --benchmark-evaluate --benchmark-mode screen

Test defaults to existing whole-solution/project managed testing. The lane option
(all/cpu/integration/gpu) partitions only the managed suite. Classified lanes
retain file/directory project selection and reject unknown projects. The UI suite
requires a feature. AOT supports the seven existing strict-AOT features;
media/application/navigation/pages never had AOT gates, and requesting one fails.
Feature suites retain all unit-project and public-sample scenarios, explicit
fonts, editor connections, and both pages/transitions samples, not lane filters.

Invalid selectors and incompatible combinations fail clearly. Test validation
precedes awaited restore/build methods;
browser/Rust/AOT skip whole-solution compilation and use existing toolchain and
publish preparation. No-build/no-restore retain their meanings; native AOT
publication builds its own output with the existing strict warning policy.
Browser timeout quarantine and native MSBuild/package hook behavior are preserved.

Benchmark area selects the repository area; benchmark-category still filters
compiled BenchmarkDotNet categories. Integrated mode is graphics/ui only;
isolated evaluation is core only. BenchmarkSignals retains its distinct
functional-test/sample prerequisite and legacy measurement. The old
PipelineBenchmark workload is now optional GPU pipeline validation, not a BDN
category. The unused unconditional Linux-x64-only Ci aggregate is removed.
Privileged screening notification remains separate from measurement.

## One SDK test invocation per classified lane

Each cpu/integration/gpu selection generates artifacts/test-lanes/{lane}.slnf
against the canonical Lumyte.slnx, then issues **exactly one dotnet test command**
for all selected host test projects. The filter stores canonical-solution-relative
project paths; OS and explicit project selection are applied before generation.
No per-project dotnet test loop or copied replacement solution is used.

The adjacent {lane}.targets is imported through the existing test MSBuild hook.
It sets VSTestTestCaseFilter separately for each project, preserving mixed legacy
class lists, Execution traits, and untagged default-integration projects. A global
CLI filter cannot represent those distinct predicates and is deliberately absent.
The .slnf artifact alone cannot encode class predicates: use the common Test
entry point, which supplies the adjacent targets property to the SDK invocation.
Integration/GPU conservatively include host candidates because Execution traits
can occur in any classified project; their per-project predicates select cases.
An unselected project reaching VSTest fails instead of falling back to its full
suite. Unique per-project TRX loggers avoid collisions within the one invocation. The
MSBuild logger value escapes its embedded semicolon as %3b so the SDK does not
interpret LogFileName as a second logger.
MSBuild -m:1 retains serial project boundaries while the CPU adapter schedules
bounded methods inside each assembly. The assembly name never classifies a lane;
a CPU-only assembly named IntegrationTests is still method-parallel.

## CPU method concurrency

CPU runs on Linux x64 only: projects are serial and **methods within the same
class concurrent**, bounded per assembly by test-concurrency (default 4, range
1–64). Inherited GPU/native-test/benchmark opt-ins are cleared. The scheduler
environment variable LUMYTE_CPU_METHOD_PARALLELISM is injected only into CPU test
children. Integration/GPU explicitly disable parallel collections and never
inherit CPU scheduling. No per-method OS process is spawned.

The linked xUnit v2 adapter uses stock method/case runners: async lifecycle,
output isolation, fixtures, cancellation and sequential theory rows remain.
Explicit collections and assembly serialization remain boundaries. Three reviewed
native-device assemblies explicitly permit CPU-only method scheduling via assembly
metadata; their unfiltered/GPU executions remain serial. Mutable
EventSource/Console/static-codec tests have named serialization collections.
A deterministic two-method barrier proves actual same-class overlap. Regressions
cover suspended-async bounds, fixtures/output, theories and serial boundaries.
xUnit v3 4.0.3 parallelMode=all was considered, but existing v2 collection/assembly
API incompatibilities make it a repository-wide migration; the scoped adapter
preserves current framework contracts. See the [official xUnit concurrency modes](https://xunit.net/docs/running-tests-in-parallel) and [released v3 package](https://www.nuget.org/packages/xunit.v3/4.0.3).

## Removed names → current suffix

Append replacements after dotnet run --project build -c Release -- --target.

| Removed target | Replacement |
| --- | --- |
| BenchmarkAnimation | Benchmark --benchmark-area animation |
| BenchmarkAudio | Benchmark --benchmark-area audio |
| BenchmarkCore | Benchmark --benchmark-area core |
| BenchmarkCoreEvaluate | Benchmark --benchmark-area core --benchmark-evaluate |
| BenchmarkGraphics | Benchmark --benchmark-area graphics |
| BenchmarkGraphicsIntegration | Benchmark --benchmark-area graphics --benchmark-integrated |
| BenchmarkInput | Benchmark --benchmark-area input |
| BenchmarkUI | Benchmark --benchmark-area ui |
| BenchmarkUIIntegration | Benchmark --benchmark-area ui --benchmark-integrated |
| CiBrowser | Test --suite browser |
| CiCpu | Test --lane cpu |
| CiDistribution | Distribution |
| CiGpu | Test --lane gpu |
| CiIntegration | Test --lane integration |
| CiNative | Test --lane integration |
| CiQuality | Quality |
| TestAot | Test --suite aot |
| TestBrowserAudio | Test --suite browser-audio |
| TestBrowserContracts | Test --suite browser-contracts |
| TestCpu | Test --lane cpu |
| TestDiagnosticsProcesses | Test --suite diagnostics-processes |
| TestDiagnosticsUi | Test --suite diagnostics-ui |
| TestGpu | Test --lane gpu |
| TestIntegration | Test --lane integration |
| TestRust | Test --suite rust |
| TestSignals | Test --suite signals |
| TestText | Test --suite text |
| TestUiApplication | Test --suite ui --feature application |
| TestUiCollections | Test --suite ui --feature collections |
| TestUiCollectionsAot | Test --suite ui --feature collections --aot |
| TestUiControls | Test --suite ui --feature controls |
| TestUiControlsAot | Test --suite ui --feature controls --aot |
| TestUiLayout | Test --suite ui --feature layout |
| TestUiLayoutAot | Test --suite ui --feature layout --aot |
| TestUiMedia | Test --suite ui --feature media |
| TestUiMenus | Test --suite ui --feature menus |
| TestUiMenusAot | Test --suite ui --feature menus --aot |
| TestUiNavigation | Test --suite ui --feature navigation |
| TestUiNumeric | Test --suite ui --feature numeric |
| TestUiNumericAot | Test --suite ui --feature numeric --aot |
| TestUiPages | Test --suite ui --feature pages |
| TestUiScroll | Test --suite ui --feature scroll |
| TestUiScrollAot | Test --suite ui --feature scroll --aot |
| TestUiTabs | Test --suite ui --feature tabs |
| TestUiTabsAot | Test --suite ui --feature tabs --aot |
| TestWasm | Test --suite wasm |
| TestWindowing | Test --suite windowing |

## Internal method and tooling migration

| Removed declarations | Replacement |
| --- | --- |
| Restore, PrepareTest, ValidateTest, PreparePublish | Ordered internal methods, not executable target names |
| Pack | Distribution --distribution-output packages |
| Samples | Distribution (samples plus packages) |
| PublishWindowSample | Distribution --distribution-output window-sample |
| DiagnosticGallery | Distribution --distribution-output diagnostic-gallery |
| PipelineBenchmark | Test --lane gpu --pipeline-validation |
| CheckFormat / CheckNativeFormat / CheckMarkdown | Quality --quality-scope managed / native / markdown |
| FormatNative / FormatMarkdown | Format --quality-scope native / markdown |
| ParleyNative / ParleyWasm | Native --native-backend parley / parley-wasm |
| WgpuNative / UsvgNative / Direct3D12Native | Native --native-backend wgpu / usvg / direct3d12 |
| SetupWasm / SetupParleyNative / SetupParleyWasm | Setup --setup-tool wasm / parley / parley-wasm |
| SetupDxc / ConfigureLavapipe / PrepareWasmTools | Setup --setup-tool dxc / lavapipe / wasm-python |
| VerifyEnvironment / VerifySdk / NativeSmoke | Verify --verify-scope environment / sdk / native |
| Ci | Removed; compose platform-appropriate tasks in the workflow |

## Actual retained declarations (18)

- Required CI (4): Compile, Test, Quality, Distribution.
- Tooling/MSBuild (3): Native, Setup, Verify.
- Developer tasks (7): Clean, Publish, Format, Benchmark, BenchmarkSignals,
  GenerateNamedColors, VerifyNamedColors.
- Artifact publication (1): DiagnosticPreviews.
- Separate screening trust stages (3): ComponentScreeningPlan,
  ComponentScreeningRun, ComponentScreeningNotify.

The four direct composition entries exclude nested setup/MSBuild calls and
separate advisory/publication workflows. All are included in the 18 declarations.

Frozen ADRs and archived measurement evidence retain the target names used at their
recorded commit. Their historical names map through the table above; they are not
current executable callers. Current build/test/sample/benchmark instructions and
workflows use the new options. The hash-locked ADR baseline is not changed.
