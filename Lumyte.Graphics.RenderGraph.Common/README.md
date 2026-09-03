# Lumyte.Graphics.RenderGraph.Common

`Lumyte.Graphics.RenderGraph.Common` contains reusable draw and effect contributions built on the render graph in
`Lumyte.Graphics`. Its first feature is a bufferless drawing layer: it receives a material, procedural draw
arguments, transforms, and a color target, then contributes one stateful pass to a `GpuRenderGraph` or
`GpuRenderGraphFrameBuilder`. Future full-screen and post-processing effects belong in this package as well.

It intentionally does **not** introduce or bind vertex/index buffers. The vertex shader derives geometry from the
vertex ID, as the existing Lumyte triangle and lighting samples do. Vertex tables or index lookup needed by a
particular shader can stay in shader code or in sampled textures listed by the material. Set a
`DrawSampledTexture` stage to `GpuStage.VertexShader` when vertex lookup reads it; the default is `PixelShader`.

The component does not own the pipeline, resource table, sampled textures, or target. Imported resources must stay
alive through graph execution. Graph-created transient resources remain owned and asynchronously retired by
`GpuRenderGraphExecution`.

## Public data

- `DrawMaterial` contains the raster pipeline, an optional `GpuResourceTable`, and sampled texture handles plus
  their vertex/pixel stages so they are visible to RenderGraph dependency planning. Every texture descriptor index must
  have one corresponding `DrawSampledTexture`; sampler-only tables may use an empty texture list. `DrawShaderBuffer`
  performs the same mapping for a shader-data buffer and an explicit buffer-array index.
- `ProceduralGeometry` contains `VertexCount` and `InstanceCount` for the existing non-indexed `Draw` command.
- `DrawTransforms` contains `World` and `ViewProjection`.
- `DrawRenderTarget` contains the target view, description, load/store operations, and clear color.
- `AddDraw` is an extension method on `GpuRenderGraph` and `GpuRenderGraphContributionContext`. It adds the
  stateful pass and returns the graph resources it declared.

The two matrices occupy the existing 128-byte root-data contract in `World`, `ViewProjection` order, using the
in-memory `System.Numerics.Matrix4x4` layout.

## Shader binding convention

Render-graph shaders see one logical descriptor array per resource kind: textures, samplers, and shader-data
buffers. The array bindings are fixed by `GpuShaderBindingConvention`; individual resources are selected by indices
stored in root data. `GpuRenderGraphShaderBindings` maps those indices to typed graph textures and buffers so pass
reads, stages, ordering, and descriptor hazards remain visible to graph compilation. Descriptor counts such as the
current native limit of 64 per kind are backend limits, not part of the common shader ABI.

## Add a draw

```csharp
var material = new DrawMaterial(pipeline, resourceTable, sampledTextures);
var draw = new DrawData(
    material,
    new ProceduralGeometry(vertexCount: 36),
    new DrawTransforms(world, viewProjection));
var target = new DrawRenderTarget(
    colorView,
    colorDescription,
    GpuAttachmentLoadOperation.Clear,
    GpuAttachmentStoreOperation.Store,
    new(0.02f, 0.02f, 0.03f, 1));

var frame = new GpuRenderGraphFrameBuilder();
frame.AddContributor(
    "main-view",
    (Draw: draw, Target: target),
    static (context, state) =>
        context.AddDraw("opaque", state.Draw, state.Target));

GpuRenderGraphPlan plan = frame.Compile(planCache);
using GpuRenderGraphExecution execution =
    plan.ExecuteAsync(backend, transientArena, retirementQueue);
```

`AddTo` imports and borrows the supplied target and material textures. It declares each sampled texture at its
vertex/pixel stage, declares the target as write-only for `Clear`/`Discard` or read/write for `Load`, and marks the target
as a graph output by default. The callback uses `AddPass<TState>` with explicit state and a static function; no
legacy capture API is involved.

When another contributor owns the target declaration, publish it and use the overload accepting an existing graph
resource:

```csharp
frame.AddContributor("target", target, static (context, value) =>
    context.PublishTexture(
        "main-color",
        context.ImportTexture("color", value.View.Texture, value.Description)), order: 0);

frame.AddContributor("scene", (Draw: draw, Target: target), static (context, state) =>
    context.AddDraw(
        "opaque",
        state.Draw,
        state.Target,
        context.GetTexture("main-color")), order: 1);
```

For passes that need ordering without sharing a GPU texture or buffer, publish a virtual dependency. It participates
in scheduling and pass culling, but never allocates GPU memory or emits a barrier:

```csharp
frame.AddContributor("scene", 0, static (context, _) =>
{
    GpuRenderGraphDependency complete = context.CreateDependency("complete");
    context.PublishDependency("scene-complete", complete);
    context.AddPass("draw-3d", 0, static (_, _) => { }).Write(complete);
});

frame.AddContributor("ui", 0, static (context, _) =>
{
    GpuRenderGraphDependency sceneComplete = context.GetDependency("scene-complete");
    context.AddPass(
            "draw-ui",
            0,
            static (_, _) => { },
            GpuRenderGraphPassFlags.NeverCull)
        .Read(sceneComplete);
}, order: 1);
```

Rebuild the frame and call `Compile(planCache)` each frame. The cache retains graph structure but rebinds that
frame's pipeline, target view, resource table, vertex count, transforms, and callback state.

For shutdown or resource replacement, keep imported objects alive until `execution.Completion` finishes. Call
`retirementQueue.Collect()` once per frame and `retirementQueue.WaitIdle()` before destroying borrowed material or
target resources. Transient allocations created elsewhere in the same graph may use the caller-owned arena shown
above and are retired automatically by the execution.
