# ADR-GRAPHICS-0042: CPU row-mask coverage on the canonical pipeline

- 状態: 提案
- 日付: 2026-11-23
- 基準コミット: `4570fed4a973bef34b6fb07b3a6fca8a52af57eb`
- 依存: [GRAPHICS-0040](GRAPHICS-0040-composable-drawing-pipelines.md) and [GRAPHICS-0041](GRAPHICS-0041-immutable-gpu-drawing-retention.md)

## 背景

The baseline canonical independent graph evaluates coverage on GPU. Renderer
authors also need an explicit host coverage producer without replacing Paint,
Clip or Compose, or changing renderer and prepared ownership APIs.

## 決定

CpuSparseCoverage2D consumes `Host<SceneV1>` and `Host<FrameV1>`, evaluates
resource-free canonical geometry using a bounded reusable row of sixteen-bit
sample masks, and publishes immutable `Host<Coverage16V1>`. It does not construct
an old renderer/service or record, submit, wait, sample images, or cache mutable
buffers. Fractional mesh coverage is a separate center-interpolation branch,
broadcast to all sixteen sample entries; it is never encoded as a binary mask.

### 利用者の変更前・変更後

The caller keeps the normalized host scene and host frame ports before uploading
them. Replace only the coverage edge in the ordinary independent pipeline:

~~~csharp
var paging = new GenericPagingLimits2D(maxWindows: 512, maxScenePages: 3);
var coverage = g.Stage(new CpuSparseCoverage2D(samples, 256L * 1024 * 1024),
    hostScene, hostFrame).Then(Transfers.Upload<Coverage16V1>());
// Instead of g.Stage(ComputeStages2D.PagedCoverage(samples, paging), scene, deviceFrame).
var paint = g.Stage(ComputeStages2D.PagedPaint(samples, paging), scene, views, deviceFrame);
var clip = g.Stage(ComputeStages2D.PagedClip(samples, paging), scene, deviceFrame);
var output = g.Stage(RasterStages2D.PagedCompose(samples, paging),
    scene, coverage, paint, clip, target);
~~~

Complete, GraphicsPipelineBuildHost.Build, direct Renderer2D construction and
its existing internal prepared-batch/Graph binding are unchanged. All eleven
canonical command routes remain available. Mask/distance-field image sampling
stays in Paint, while coverage evaluates their destination rectangle.

The opt-in paged consumers preserve these same ports and sample metadata. At a
64 KiB storage range the complete 257 × 1 × 256 fixture uses two source
pages and 257 output windows. The policy admits at most three source pages;
larger arbitrary-read scenes reject explicitly. Physical bindings, alignment,
combined descriptor limits and aggregate scratch/transfer budgets are checked
before native output allocation. Existing whole-range factories are unchanged.

### Numerical and ownership contract

Command-major, pixel-major, sample-major float32 output uses the identical
Coverage16V1 schema as ComputeCoverage2D. Rounded radius-zero membership remains
inclusive (nine boundary fixture samples), rectangle membership half-open (four),
and circle membership strict (one). Translation metadata is checked across
inputs and subtracted before evaluation, including mesh pixel center. Paint and
clip remain correlated with each individual sample; Compose retains per-command
average then painter-order SrcOver.

Both target dimensions are explicitly limited to 32768, including zero-command
scenes. Exceeding that ceiling rejects; coordinates and dimensions are not
clamped. The compiled host reservation is bounded by the explicit working limit,
not an unconditional global maximum. Minimum descriptions reserve only 258
bytes; actual descriptions charge 256 + eight bytes per output sample + two
bytes per row pixel. Small scenes can therefore use small explicit limits.

Checked admission includes the dense compatible output, immutable publication
copy and row scratch before allocation, plus an explicit finite sample-edge work
ceiling. Core cumulative host accounting and Graphics transfer/scratch accounting
remain independent additional limits. Explicit upload may use the root immutable
upload cache; the CPU stage owns no cache or native resource. No optimizer,
registry, backend-wide stage or hidden upload is added. Dense Coverage16V1 storage
is intentionally not described as compact native strip storage or a performance
improvement.

## 検討した代替案

Wrapping the old sparse service would keep a whole renderer behind a stage and
prevent coverage substitution. A binary mask for mesh would lose fractional
center coverage. Compact device strips would need a different consumer schema;
this change deliberately uses the existing Coverage16V1 layout.

## 結果と影響

### Qualification

CPU boundary, mesh and malformed-input tests accompany native direct and Graph
variants. Fresh Release CPU, actual native substitution, multi-flight retention,
source-generation and full corpus qualification must be reported separately.
A compiled test or skipped native test is not a GPU pixel result. No performance
claim is made.
