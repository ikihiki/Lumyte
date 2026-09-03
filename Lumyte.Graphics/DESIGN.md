# Lumyte GPU design

This design follows Sebastian Aaltonen's [No Graphics API](https://www.sebastianaaltonen.com/blog/no-graphics-api)
as a direction for modern bindless hardware. Luxel is an implementation reference, not the API contract.

## Audit of the Luxel design

### Critical

1. `GpuDevice.CreateRenderTarget` and `CreateDepthTarget` combine texture description, native object creation,
   memory allocation, view creation, ownership, and destruction. Vulkan and WebGPU repeat this coupling in
   backend-specific factories. This prevents explicit heap suballocation, transient aliasing, and RenderGraph
   lifetime planning.
2. `GpuGraphicsPipelineVariantKey` includes rasterizer, depth-stencil, and blend state. Luxel backends cache a
   native pipeline for each combination, recreating the PSO-permutation problem the design is meant to avoid.
   Depth-stencil must be independently created and bound. Blend should be independent when hardware supports it,
   or explicitly embedded when required by a target architecture.
3. `GpuTexture` is both an owning lifetime object and the value passed as an attachment or shader resource.
   AssetsGpu caches and disposes graphs of these objects by reference identity. Ownership is therefore spread
   across asset caches, command buffers, devices, and render graphs instead of being assigned to memory arenas.

### High

1. `IGpuBackend` exposes separate `CreateBuffer`, `CreateRenderTarget`, `CreateDepthTarget`, and
   `CreateSampledTexture` paths. This is a resource-object API rather than `gpuMalloc` plus explicit texture
   placement and views; it also starts a new buffer/texture zoo.
2. `GpuShaderCode` is a fat package containing SPIR-V, compute/vertex/pixel DXIL, and WGSL. Backend selection,
   file loading, and shader-asset concerns leak into the small GPU command API. The GPU layer should consume one
   backend-ready IR byte span; compilation and multi-target packaging belong above it.
3. WebGPU rebuilds resource bind groups from arrays and tracks referenced texture/sampler objects per command
   buffer. This is a compatibility backend, not the core model. It must translate a bindless heap/root-data model
   without defining that model.
4. RenderGraph directly creates and disposes transient `GpuTexture` objects. Its compile step should instead
   calculate first/last use, query texture size/alignment, alias non-overlapping ranges, then create placed textures
   and views for one execution generation.

### Medium

1. `GpuBlendMode` only represents none and alpha blend. It is neither a useful hardware description nor a path to
   programmable blending. A blend equation description is required, with optional PSO embedding made explicit.
2. `GpuAttachmentLayout` supports one color target and combines depth and stencil into one nullable format. A
   raster pipeline needs a list of color formats/write masks and distinct depth/stencil capabilities.
3. `GpuCommandBuffer.SetRootArguments` uploads inline bytes up to a fixed limit. The primary contract should pass a
   single 64-bit GPU root-data address to draw/dispatch; compatibility backends may emulate this internally.
4. Resource-by-resource state tracking in RenderGraph is more detailed than the proposed stage-to-stage barrier
   model. Dependencies are still valuable for scheduling and lifetime calculation, but should not force a large
   universal state enum or automatic barrier after every pass.

## Lumyte data and lifetime model

```text
CPU asset/compiler layer
  ├─ backend-ready GpuShaderBinary
  └─ linear upload bytes
             │
             v
Gpu memory allocator / arenas
  ├─ CPU-write-combined allocation ──> { CPU address, 64-bit GPU address }
  ├─ GPU-only allocation ────────────> { 64-bit GPU address }
  └─ CPU-cached readback allocation ─> { CPU address, 64-bit GPU address }
             │ placement
             v
GpuTextureHandle (small native texture metadata; does not own allocation)
             │ describes subresources
             v
GpuTextureView ──────> bindless texture descriptor index/heap entry
             └──────> GpuColorAttachment / GpuDepthStencilAttachment

RenderGraph execution generation
  request -> first/last pass -> alias plan -> arena allocations -> placed textures/views
          -> record commands -> submit/fence -> retire generation -> recycle arena ranges
```

Opaque pipeline, texture, queue, and command-buffer handles may remain because current raster hardware needs CPU
driver metadata. Bulk data and relationships remain addresses, small handles, and plain descriptions.

## Render targets

There is no `CreateRenderTarget` ownership shortcut in the core API.

1. The caller or RenderGraph declares `GpuTextureDescription` with attachment/copy/sample usage.
2. The backend reports `GpuTextureMemoryRequirements` before placement.
3. A persistent arena or transient RenderGraph arena returns a GPU-only `GpuMemoryAllocation`.
4. The backend creates a non-owning `GpuTextureHandle` placed in that allocation.
5. `GpuTextureView` selects format, mip range, and layer range. Its device-issued `TextureId` is stored in a fixed
   logical `GpuResourceTable` slot; attachment views remain CPU command metadata where hardware requires it.
6. A render pass uses plain `GpuColorAttachment`/`GpuDepthStencilAttachment` values containing view, load/store,
   and clear values. Beginning or ending a pass does not imply a barrier.
7. Persistent texture owners retire the allocation after the last GPU fence. Transient textures are retired as an
   execution generation, and non-overlapping `GpuTransientLifetime` intervals may alias one allocation.

## Migration order

1. Establish plain memory, texture, view, attachment, shader-binary, and minimal PSO descriptions (current step).
2. Add allocator contracts: malloc/free, CPU-to-GPU address pair, size/alignment query, and placed texture creation.
3. Add a minimal command contract around copy, stage barrier, pipeline/state binding, and draw/dispatch using GPU
   root-data addresses.
4. Implement one headless backend first. Vulkan is the closest semantic fit; WebGPU must be treated as an explicit
   compatibility translation with capability limitations.
5. Rebuild RenderGraph around scheduling, lifetime intervals, alias allocation, and explicit stage barriers.
6. Integrate AssetsGpu through upload arenas and bindless indices, without reference-identity ownership caches.
7. Add platform presentation as a separate surface adapter, then RenderSystem, 2D, typography, and gallery layers.

## Manual path without RenderGraph

`IGpuBackend` is the single backend contract. `GpuBackendCapabilities` declares whether an implementation supports
explicit placement and raster pipelines. Vulkan and DirectX 12 expose explicit placement; WebGPU does not emulate
independent allocations. Consumers must select the capability required by their path before invoking it.

Shader resources use one public contract on every backend. Devices issue opaque `TextureId` and `SamplerId` values;
materials place them into fixed texture and sampler slots in `GpuResourceTable`, and command recording activates the
whole table with `SetResourceTable`. Native descriptor heaps, Vulkan descriptor sets, and WebGPU bind groups are
backend implementation details. DirectX 12 and Vulkan materialize transient native tables for a command buffer.
WebGPU translates a logical table to a bind group and caches it by table identity, native layout, and `Revision`;
writing the same logical ID preserves the cache, while a changed slot or destroyed registered resource invalidates it.

`GpuPersistentArena` owns long-lived native memory blocks and returns aligned `GpuMemoryAllocation` regions carrying
the backing allocation ID, byte offset, size, memory kind, and mapped CPU address when available. Requirements expose
backend memory compatibility data. Direct3D 12 treats heap classes as exact keys. Vulkan intersects native memory-type
bit sets when planning aliases, then records the selected physical memory type as a one-bit arena block key. A block
is reused only when its memory kind and selected key satisfy the new request, its own alignment guarantee is at least
as strong as the request, and one free region has enough bytes after alignment padding. Otherwise the arena allocates
a separate native block. This permits a smaller compatible resource to reuse a released larger region without
mixing incompatible heap classes or memory types. `Release` returns a known-idle region immediately, fence-based
`Retire`/`Collect` delays reuse, and `Trim` releases completely unused native blocks.

```csharp
var arena = new GpuPersistentArena(backend);
var textures = new GpuManualTextureAllocator(backend, arena);
var description = new GpuTextureDescription(
    width, height, GpuFormat.Rgba8Unorm,
    GpuTextureUsage.ColorAttachment | GpuTextureUsage.Sampled);

GpuTextureMemoryRequirements requirements = textures.GetMemoryRequirements(description);
GpuMemoryAllocation memory = textures.AllocateMemory(description);
GpuTextureHandle texture = textures.CreatePlacedTexture(description, memory);
GpuTextureView view = textures.CreateView(texture, new(GpuFormat.Rgba8Unorm));
GpuColorAttachment color = textures.ColorAttachment(
    view, GpuAttachmentLoadOperation.Clear, GpuAttachmentStoreOperation.Store);

// Record and submit commands that use color, then retire at the submission fence.
textures.Retire(texture, memory, submissionFence);

// Called after querying the queue/device completed fence. This destroys native texture metadata first,
// then releases the separate allocation.
textures.Collect(completedFence);
```

There is intentionally no `CreateRenderTarget` owning shortcut. The application chooses the memory arena and can
retain the texture, view, and attachment descriptions independently. `VerifyEmpty` is a shutdown assertion; it
prevents silently freeing resources whose GPU fence has not completed.

## Render graph planning layer

`GpuRenderGraph` is a backend-independent planner for imported resources and graph-local transient textures and
buffers. `CreateTexture` and `CreateBuffer` declare resources whose physical allocation is deferred until execution.
Pass callbacks use `AddPass(name, state, static (context, state) => ...)`. Explicit state is stored directly in the
pass declaration, and the stack-only `GpuRenderGraphPassContextView` resolves only resources declared in that pass
and records ordinary commands through its `Commands` property. This avoids closure and per-record context
allocations while preserving declared-resource checks. Imported resources are borrowed and are never destroyed by
the graph.

`GpuRenderGraphFrameBuilder` is the multi-system registration layer above this low-level graph. Systems register a
named contribution with an explicit integer order. Enabled contributions run serially by `(order, ordinal name)`, so
the resulting declaration order is independent of system update order. Each contribution receives a
`GpuRenderGraphContributionContext`; local resource and pass names are qualified as `contributor::local`, allowing
multiple cameras or views to use the same local names. `PublishResource` and `GetResource` form the intentional
cross-contributor boundary, and duplicate contributor or shared-resource names fail during registration.

`GpuRenderGraphPlanCache` keys plans by exact graph structure: qualified names, declaration order, resource kinds and
descriptions, output/export state, enabled passes, access modes, stages, and hazards. A hit reuses dependency order,
culling, barriers, transient lifetimes, and reuse slots, then rebinds the current frame's native imports, exported
owners, resource IDs, and pass callbacks. Consequently camera constants and other callback-captured frame data are
never retained by the cache. Resolution, format, usage, pass enablement, or any other structural change produces a
miss and recompilation. Structure lookup computes an allocation-free 64-bit hash, then compares every structural
field against the retained snapshot before accepting a hit, so a hash collision cannot reuse the wrong plan. The
cache is bounded and evicts the oldest structural entry at capacity. Frame contributors likewise use explicit state,
and frame building sorts its snapshot in place without LINQ iterator chains.

Passes declare each used resource once with `Read`, `Write`, or `ReadWrite`, plus its `GpuStage` and exceptional
`GpuBarrierHazards`. Declaration order defines successive versions of a resource. `Write` means a complete overwrite
and therefore does not keep an earlier producer alive; `ReadWrite` consumes the previous version before producing the
next one. A pass may be retained by a marked output or by `NeverCull`. Compilation walks backward from those roots,
removes unused passes, preserves read-after-write/write-after-read/write-after-write ordering among live passes, and
emits an immutable execution plan.

The common command contract has global stage barriers rather than resource-specific state enums. Accordingly, the
planner tracks the last live access per logical resource, omits read-to-read barriers, and merges all required
transitions before a pass into one stage-to-stage barrier. Imported-only plans can still be recorded for caller-owned
submission. Plans with transient resources use `Execute(IGpuBackend)`, which allocates only resources referenced by
live passes, records and submits one command buffer, waits for completion, and releases non-exported resources.

Transient handles are intentionally unavailable before execution and are valid only through the pass context.
`ExportTexture` and `ExportBuffer` make a graph-created result a compilation root and retain its physical allocation
in `GpuRenderGraphExecution`. The resulting exported object carries the public handle, description, owning backend,
and execution lifetime needed for a later graph to import it safely. Importing is borrowing: the producer execution
remains the sole owner and must outlive every consumer execution. This makes export/import the explicit boundary for
cross-graph resource sharing.
An asynchronous consumer acquires an internal import lease while preparing its submission and releases that lease
only when the consumer token completes. Disposing the producer execution immediately invalidates new imports, but
native export destruction waits for all already-submitted consumers. This preserves the same producer-outlives-
consumer rule that synchronous execution previously obtained from its blocking wait.

Compilation reports the first and last live pass for every live transient resource. Exported resource lifetimes are
extended to the end of the plan because their contents survive execution. The compiler also assigns conservative
logical reuse slots: resources may share a slot only when their lifetimes do not overlap and their resource kind,
memory kind, and complete description match. This backend-independent result is also the cacheable structural plan.
On a backend advertising `MemoryAliasing`, execution specializes those lifetimes into a physical memory plan by
querying every live resource's native size, alignment, and compatibility requirement. Non-overlapping resources with
different descriptions may share one physical slot when their resource kind and memory kind match and the backend can
combine their compatibility requirements. The slot takes the maximum required size and alignment and the combined
compatibility value, so every occupant is created from a region that satisfies its own requirements. Overlapping
lifetimes, texture/buffer mixtures, different memory kinds, disjoint Vulkan memory-type masks, and distinct Direct3D
12 heap classes always produce separate physical slots. Execution allocates one `GpuPersistentArena` region per
physical slot, creates every occupant at that allocation ID and offset, and records an alias barrier between
consecutive occupants. Direct3D 12 uses a native aliasing resource barrier; Vulkan uses alias-capable images and a
stage memory barrier. Backends without the capability, including WebGPU, retain per-resource device-owned allocation.
The current synchronous execution waits before returning regions to the arena; asynchronous generation uses
`ExecuteAsync(backend, retirementQueue)` or its caller-owned-arena overload. A `GpuRetirementQueue` owns one monotonic
submission semaphore, exposes `GpuSubmissionToken` completion, supports non-blocking `Collect`, and limits the number
of submissions in flight. The backend reports completion and releases only its internal command-recording resources.
RenderGraph owns the deferred destruction of transient native resources and texture views; the resource system owns
exported-resource lifetime and arena-region recycling. Multi-queue scheduling remains future work.

`Execute(backend, arena)` accepts a caller-owned arena so backing GPU heaps can be reused across graph executions.
The compatibility overload `Execute(backend)` owns a temporary arena for that execution. Exported resources retain
their arena regions through `GpuRenderGraphExecution`; disposing the execution destroys placed resources before
returning those regions, while a caller-owned arena remains alive until its owner trims or disposes it.
For asynchronous execution, disposing an export relinquishes its logical ownership immediately but schedules native
destruction and region recycling after the submission token. If GPU work completes first, the export remains alive
until its execution is disposed. `WaitForCompletion` and `GpuRetirementQueue.WaitIdle` provide explicit blocking
boundaries for readback and shutdown.

## Vulkan device and offscreen conformance slice

`Lumyte.Graphics.Vulkan.VulkanDevice` implements the narrow resource port directly. It creates a Vulkan 1.3 device
with dynamic rendering, synchronization2, and timeline semaphores, queries native memory requirements, allocates and
binds memory, and creates separate resource views. Offscreen target dimensions, shader fixtures, and readback
assertions belong to the conformance tests rather than the device. Surface/swapchain orchestration remains a separate
presentation boundary and is not part of the common Graphics device contract.

`StartCommandRecording` allocates and begins the backend's transient one-shot command buffer. Public command calls
write to that native recorder immediately; `Submit(ReadOnlySpan<GpuCommandBuffer>, semaphore, value)` only ends the
recorders and submits one or more buffers. A queue semaphore is a real Vulkan timeline semaphore with a monotonically
increasing 64-bit value. Waiting for an already reached value remains valid. Completed native command buffers are
reclaimed without creating a fence per submission.

The common barrier contract contains only producer/consumer stages and exceptional hazard flags. Vulkan image layouts
remain backend state: attachment use transitions an image to its attachment layout, and a texture copy transitions it
to transfer source. Resource handles, old layouts, and new layouts do not leak into the common barrier API.

Buffer copy operands use `GpuMemoryAddress`, an allocation identity plus byte offset with range validation. It is a
logical address, not a claim that the backend exposes shader pointers. Vulkan resolves it through its live placed-buffer
registry to a `VkBuffer` and offset. A future WebGPU backend can perform the same resolution to a WebGPU buffer and
offset. `GpuDeviceAddress` is deliberately separate and may only be returned by backends with genuine shader-visible
virtual addresses. Copy footprints carry dimensions, bytes per pixel, and row pitch rather than relying on a naked
integer. Readback buffers remain placed buffers backed by `HostCached` allocations; CPU access uses mapped bytes.
Presentation and shader pipelines are layered onto this allocation slice below.

## Raster and presentation vertical slice

The raster command surface adds only backend-ready vertex/pixel shader binaries, attachment-format pipeline state,
dynamic viewport/scissor, pipeline binding, and non-indexed draw. The triangle sample uses `gl_VertexIndex`, so no
vertex-buffer binding abstraction is introduced. Pipeline state remains limited to state that can affect shader
microcode or render-target compatibility.

The raster-state conformance suite uses the same observable cases on Direct3D 12, Vulkan, and WebGPU:

| Contract state | Observable conformance |
| --- | --- |
| Embedded blend equation and factors | Alpha source is composited over a blue clear value and the resulting RGBA pixel is read back. |
| Color-target and blend write masks | Disabled green, blue, and alpha channels retain their clear values. |
| Triangle list/strip topology | A four-vertex strip covers the target center. |
| Front/back culling and front-face winding | Reversing front-face winding reverses back-cull output; front-cull output is its complement. |
| Depth format | The fixed `LessEqual` test with depth writes keeps the near triangle for both draw orders. |
| Stencil format | A cleared combined depth-stencil attachment is bound while color output is rendered and read back. |
| Multiple color targets | Direct3D 12 writes distinct fragment outputs to two targets; Vulkan and WebGPU reject the option. |

Depth compare, depth-write enable, stencil compare/reference, and stencil operations are not configurable through
`GpuRasterPipelineDescription`. Format presence currently selects fixed depth (`LessEqual`, write enabled) and
stencil (`Always`, `Keep`) behavior. `GpuDepthStencilState` remains a separately validated future state contract;
the core tests assert that it is not silently exposed as raster-pipeline state. Stencil contents therefore cannot be
made color-observable beyond attachment participation until a stencil-reference/state command is added.

Vulkan and WebGPU explicitly reject multisampling, alpha-to-coverage, dual-source blending, and multiple color
targets in the current slice. Direct3D 12 rejects dual-source blending and pixel-tests multiple color targets. It can
create multisample pipelines, but the common command contract has no resolve operation yet, so multisample and
alpha-to-coverage readback remain an explicit contract-only case rather than a misleading pipeline-creation test.

`Lumyte.Graphics.Vulkan.VulkanPresentation` owns the surface-facing objects: swapchain, swapchain image views, acquire
semaphore, and render-finished semaphore. Swapchain images are registered as external, non-owned texture records only
while their swapchain is live. The placed-resource allocator never owns or frees them. Per-frame flow is acquire,
start native recording, stage barrier, dynamic rendering clear, bind pipeline, set viewport/scissor, draw three
vertices, end rendering, backend-internal present-layout transition, submit with the queue timeline plus adapter-local
binary semaphores, and present. Resize or out-of-date recreates views and the swapchain after the device is idle.

## Shader package runtime

`Lumyte.Graphics` owns the read-only, versioned MessagePack schema and treats every package as untrusted data. All
contract members use explicit numeric keys; contractless and typeless resolvers are forbidden. Envelope keys are
`0=magic (LSHP)`, `1=version`, and `2=entries`. Entry keys are `0=format`, `1=stage`, `2=entryPoint`, `3=target`,
`4=profile`, `5=capability`, `6=ABI SHA-256`, `7=payload SHA-256`, and `8=payload bytes`.

Version 1 supports SPIR-V, DXIL, and WGSL artifacts. Shader code is always a MessagePack binary payload: SPIR-V and
DXIL are opaque binary, while WGSL bytes must also be valid UTF-8. The runtime limits a package to 64 MiB, an artifact
to 32 MiB, metadata strings to 4096 UTF-8 bytes, entries to 1024, collection elements to 10240, and nesting to 16.
It pre-scans tokens before deserialization, uses `MessagePackSecurity.UntrustedData`, rejects maps/extensions,
malformed or truncated input, trailing data, unknown versions/enums, duplicate full entry keys, empty payloads, and
hash mismatches. Parsed artifacts clone payload/hash storage; selection returns an immutable backend-ready view.

Schema evolution appends numeric keys only. Old keys never change meaning or type. Readers reject unknown envelope
versions, so a semantic change requires a new version; optional trailing fields may be added within a version only
when older readers can safely ignore them. MessagePack integer encoding defines byte order, so the package has no
host-endianness dependency. The SHA-256 payload hash covers the exact stored bytes. `PackageHash` covers the complete
serialized MessagePack byte sequence. Entry identity is format + stage + entry point + target + profile + capability.

Each backend selects its own format. Vulkan requests exactly one SPIR-V artifact for each requested stage/entry point
and checks the expected ABI hash; no backend-kind switch exists in core. A Slang multi-target build produces parallel
entries (for example SPIR-V, DXIL, and WGSL) sharing stage, logical entry point, and ABI hash while target/profile/
capability describe the concrete output. `Lumyte.Shaders` owns deterministic sorting/normalization and serialization;
future Slang invocation belongs in `Lumyte.Shaders.Slang`. Runtime Graphics never depends on tooling.

MessagePack-CSharp 3.1.8 is pinned centrally. It is MIT-licensed and its runtime dependencies are limited to the
MessagePack analyzer/annotations package family and standard Microsoft BCL support packages.
