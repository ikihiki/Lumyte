# Task option migration

The Fallout inventory is reduced from **85 to 43 actual target declarations**.
Removed test, benchmark and CI aliases are not retained. The installed Fallout
10.4 public target-definition API was inspected: this does not hide targets or
alter listing metadata. Preparation remains in the real dependency graph.

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
precedes restore/build. PrepareTest keeps Compile/Restore dependencies;
browser/Rust/AOT skip whole-solution compilation and use existing toolchain and
publish preparation. No-build/no-restore retain their meanings; native AOT
publication builds its own output with the existing strict warning policy.
Browser timeout quarantine and native MSBuild/package hooks are unchanged.

Benchmark area selects the repository area; benchmark-category still filters
compiled BenchmarkDotNet categories. Integrated mode is graphics/ui only;
isolated evaluation is core only. BenchmarkSignals retains its distinct
functional-test/sample prerequisite and legacy measurement. PipelineBenchmark
retains the Vulkan sample workload, not a BDN category. Distribution and Quality
are real compositions. Ci remains the aggregate without per-lane/browser aliases.
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

## Retained inventory

Core lifecycle, package/sample compositions, native MSBuild leaves, toolchain and
environment setup, artifact/report tasks and quality workers remain independently
callable because they are genuine tasks or dependency nodes, not selectors.

- Build.Benchmarks.cs: Benchmark.
- Build.Browser.cs: SetupWasm.
- Build.Ci.cs: Distribution, Quality, Ci.
- Build.ComponentScreening.cs: ComponentScreeningPlan, ComponentScreeningRun.
- Build.ComponentScreeningNotification.cs: ComponentScreeningNotify.
- Build.Diagnostics.cs: DiagnosticGallery, DiagnosticPreviews.
- Build.Direct3D12.cs: Direct3D12Native.
- Build.Dxc.cs: SetupDxc.
- Build.Environment.cs: VerifyEnvironment, VerifySdk, ConfigureLavapipe, NativeSmoke.
- Build.NamedColors.cs: GenerateNamedColors, VerifyNamedColors.
- Build.Parley.cs: ParleyNative, ParleyWasm, SetupParleyNative, SetupParleyWasm, PrepareWasmTools.
- Build.Quality.cs: CheckFormat, CheckMarkdown, FormatMarkdown, CheckNativeFormat, FormatNative.
- Build.Samples.cs: Samples, PublishWindowSample.
- Build.Testing.cs: Test, PrepareTest, ValidateTest.
- Build.Usvg.cs: UsvgNative.
- Build.Validation.cs: BenchmarkSignals, PipelineBenchmark.
- Build.Wgpu.cs: WgpuNative.
- Build.cs: Restore, Compile, Clean, Pack, PreparePublish, Publish.

Frozen ADRs and archived measurement evidence retain the target names used at their
recorded commit. Their historical names map through the table above; they are not
current executable callers. Current build/test/sample/benchmark instructions and
workflows use the new options. The hash-locked ADR baseline is not changed.
