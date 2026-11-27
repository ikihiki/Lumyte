# Common graphics planning backend

Planning depends on Core Execution/Pipelines and Graphics.Abstractions, never RenderGraph. Frontends declare individually visible typed GPU operations into a shared frame; there is no opaque Graph runner or native callback stage.

## Host and lifetime

Register GraphicsExecutionModule in the caller-owned ExecutionRuntime. Use its runtime-managed fixed owner or bind module.HostPhase to the host pump; this module creates no scheduler/thread. Keep the runtime running through explicit session drain and retirement. The device is borrowed. Import raster/compute/mesh programs and samplers with independent session use leases. Stages receive immutable handles and finite, expiring declaration views, not devices, allocators, root builders or submission authority.

1. Create one frame, explicit immutable versions, ranges and stage/access/layout uses. Declare observable exports, external writes, KeepAlive and explicit dependencies.
2. Compile without native allocation: initialization, culling, cycles and RAW/WAR/WAW ordering. A writing pass is one structured operation. Multiple writing operations or internal layout transitions require separate passes, rather than incorrectly placing all barriers before the pass.
3. Prepare on the fixed owner: admit logical byte/count budgets, allocate only live resources and actual native views, collect ArgumentTablePlan recipes, materialize references/tables, and prepare typed range barriers and final export barriers.
4. Explicit shader-data upload tasks start mapping on the owner, await actual mapping outside that owner, copy captured typed values and unmap on the owner. Cancellation never releases a mapping still in progress. Preparation then calls command-buffer native PrepareRender/PrepareCompute/PrepareMesh to cache roots, descriptors and pipeline variants. It does not emit draws or dispatches.
5. Record/Finish/Submit/Wait/Retire are explicit, with ExecuteAsync as the explicit convenience sequence. Record uses ordinary generic SetArguments with the same prepared wrapper identity, and complete captured state/index bindings. It does not issue device allocation/registration, mapping, upload I/O or fallback root serialization. Native preparation support is mandatory for shader commands; unsupported native implementations fail Prepare explicitly.
6. Successful retirement requires both submission and command completion. Retire native commands before tables/views/allocations. Export leases pin the whole preparation and become readable only after successful completion and command retirement. A preparation can be claimed by exactly one execution forever, even while exports remain retained.

Synchronous native Dispose is owner-only and never waits. Async retirement dispatches cleanup to the fixed owner. Failed cleanup entries remain in retryable ledgers; successful entries are never released twice. Cancellation is not GPU cancellation. Unknown submit acceptance and incomplete work stay quarantined; ordinary Drain retries known submissions. Unknown acceptance requires a non-ready device generation and successful backend WaitForRetirementAsync. Without that proof, reclamation is refused.

## Typed operations

- Ordinary and indexed draws (ushort and uint), direct and indexed indirect draws, mesh tasks and indirect compute dispatch.
- Explicit color and depth/stencil attachments, independent load/store operations, complete render state, viewport/scissor, blend constants and stencil reference.
- Typed raw-buffer copies, image copies, buffer/image transfers and attachment clears.
- Typed shader-data storage/import, synchronous declaration capture, deferred typed upload staging, native shader-data copies and shader-data export scopes.
- Buffer/shader-data exports with receiving access scopes (including HostRead), and texture exports with explicit final layouts (Sampled/CopySource/Present, etc.). Present requires an explicitly imported acquired surface image. Exporting Present does not implicitly submit, signal, present, resize or wait; those surface/queue interop obligations remain explicit host operations.
- Attachment Store.Discard invalidates the selected subresource for future reads/exports. Discarding a stencil aspect conservatively invalidates the selected depth/stencil subresource because aspect-selective logical values are not exposed yet.

## Arguments and shader-data reference closure

Declaration captures immutable WriteValue bytes and typed WriteReference recipes. Each writer call checks remaining bytes/references before allocating/copying a piece; empty writes are rejected. Completion must match the original static ABI. Raw native references and foreign frame/operation references are not accepted as ordinary argument bindings.

`PreparedArguments<T>` preserves the original static wire contract and owns a fresh, stable non-null native preparation identity. The exact same wrapper is used for native preflight and Record. Native commands own immutable cache entries until retirement; unknown/foreign/expired identities do not fall back to allocation. No reflection, type-name switch, new snapshot setter or generator change is required.

Shader-data upload uses the original native `IGraphicsShaderDataBuffer<T>` allocation. The alternate typed codec `CopyFrom<PreparedArguments<T>>` encodes frozen numeric bytes and genuine resolved references before Record. ShaderData bindings require every transitive reachable resource to have an explicit matching consumer range/access/stage/layout declaration. Shader-data copies join coherence domains without rewriting numeric payloads or simulating GPU work.

Tables are normally operation-local (shareTables:false). Already encoded shader-data references necessarily join their producer/consumer table domains through the existing ShaderResources.JoinTable seam. Planning retains and exposes the whole physical descriptor union for every operation in such a domain, not merely its local root fields. Physical buffer barriers and sampled layouts are tracked separately from semantic content versions. Conflicting whole-table attachment/sample exposure is rejected; physical exposure never manufactures semantic writes or revives a dead producer. Exporting shader data requires its referenced read targets to have initialized contents from explicitly live producers.

Imported reference-free shader data may use existing initialized contents. Already patched reference-bearing imports from an unknown foreign table domain are explicitly rejected; import undefined storage and provide canonical UploadShaderData recipes to repatch it. Such external bytes are never silently rebound to newly numbered slots.

## Core typed stages

Supply GraphicsPipelineBackend(frame) to PipelineBuildHost. Factories declare finite GraphicsStageRequirement reservations. GraphicsStageCapabilities gives expiring GraphicsDeclarationScope/GraphicsPassScope views; every mutation checks the open invocation and quota before declaration. Saved views fail after Complete. Failed invocations poison the frame rather than commit partial declarations.

Mandatory Core IStageBackendValueValidation hooks check generic scope Read/Publish as well as facade paths. Every opaque leaf of supported recursive typed bundles must belong to the common frame, and reads register all producer dependencies. Bundle traversal is bounded and validation cannot reenter the declaration facade.

`Device<TSchema>` exposes a static descriptor even when default, but default has no initialized resource/extent. CPU content reads remain forbidden. BufferValue validates canonical element type/stride/count; ShaderDataValue validates the static shader wire stride rather than managed reference layout. Image semantic schema/extent is explicitly authored. Core Visit and Graph.Invoke keep individual factories and typed edges visible. Typed preparation only declares into the common frame; it does not recursively prepare or independently submit GPU work.

## Conservative limits

- One live frame and one in-flight execution per session; MaximumInFlight other than 1 is rejected. This is explicit exclusive interop, not a concurrency guarantee. Kernel/native allocation owners remain borrowed; the host must serialize outside-session use and retain its independent use authorities through real completion.
- One backend instance per pipeline build, one Prepare per compiled plan and one execution per preparation. Prepare cancellation is observed at safe ownership boundaries; an already started native map is awaited to its actual result before release.
- Budgets bound logical allocation bytes, captured wire bytes, elements, resources, immutable versions, uses, descriptor references, physical exposures, barriers, commands, artifact/import/export leases and in-flight owners. Driver-private alignment/allocation overhead is not observable as exact physical bytes through this interface. Depth24Stencil8 logical accounting conservatively reserves eight bytes per texel.
- Buffer/shader-data ranges are elements; texture ranges flatten mip/layer subresources. View aliases normalize physical identity, version and range and prepare their exact requested descriptors. Non-contiguous mip/layer selections are explicitly unsupported.
- Writes consume the latest explicit version. Partial discard leaves other contents undefined. Preserve requires initialized input and retains explicit read/modify/write semantics. Old readers precede overwrites; cycles and exporting overwritten old versions fail. Explicit non-overlapping merge and automatic preservation copies remain unsupported.
- No transient physical alias reuse, cache publication/eviction transaction or multi-frame export detachment yet. Non-aliased allocations and independent export retention preserve safety.
- This is a single GPU epoch. GPU→Download→CPU→Upload→GPU continuation, dynamic extent expansion and persistent payload relocation are not implemented and are not replaced by CPU effects or hidden readback.

Managed consumer tests use independent fake devices for deterministic ownership, command, cache and synchronization assertions. Native GPU correctness and backend-native preflight implementations need separate hardware/NativeAOT proof; managed tests are not pixel evidence.
