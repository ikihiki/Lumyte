# Typed graphics pipelines

`GraphicsPipelineBuildHost` compiles the immutable generic Core topology against
an explicit borrowed device, completed-artifact provider and finite policy.
It does not compile shader source, fuse stages, select fallback implementations
or wrap an existing drawing backend. CPU-only consumers use `PipelineBuildHost`
without referencing this assembly.

## Preparation and execution

- CPU stages publish `Host<S>` values; GPU stages consume and produce `Device<S>`.
  `Transfers.Upload<S>()` and `Transfers.Download<S>()` are explicit scheduler-owned
  placement changes. Native view/dependency schemas are projections, not buffers
  that may be uploaded or downloaded.
- `PipelineResult<T>` owns preparation state. A Device result is not GPU completion
  or a CPU-readable payload. Acquire one exclusive record plan or segmented
  execution from the exact originating pipeline.
- A record plan records once without Finish, Submit, wait or mapping. Keep its
  independent lease through actual completion and recorded command disposal.
- `GraphicsPipelineExecutionHost` owns each segmented recording/submission epoch.
  Download copies remain private until every mapping and epoch retirement succeeds;
  only then are CPU continuations released. Cancellation or mapping/cleanup failure
  latches failure and stops future intervals. `DrainAsync` retries known accepted
  work/cleanup; uncertain native acceptance remains quarantined.
- Drawing2D may attach an internal retained source-generation validator. It runs
  before every recording interval, including resumed intervals after readback.
- Resource manifests expose external native allocation/view/range dependencies.
  The separate `Lumyte.Graphics.Pipelines.RenderGraph` adapter imports dependencies
  transactionally; it is not a scheduling or completion shortcut.

## Explicit source versus derived uploads

The existing `new Upload<S>()` and `Transfers.Upload<S>()` declarations retain
source eligibility for compatibility. `UploadRetention.Source` explicitly selects
the same contract: only an enabled compiled `ImmutableUploads` policy may retain
exact completed source chunks. The transfer edge, not the schema type, declares
this provenance; the same schema may also contain invocation-derived data.

Declare derived CPU output uploads as `Transfers.Upload<S>(UploadRetention.Fresh)`
(or `new Upload<S>(UploadRetention.Fresh)`). Fresh bypasses immutable lookup,
admission, chunk ceilings and publication entirely, even with a zero-capacity
source cache. It still pays the ordinary per-run scratch/transfer budget and owns
new staging/destination allocations through the unchanged cancellation, recording,
completion and cleanup path. Writable-consumer preflight uses this same captured
edge declaration; Source remains read-only when retention is enabled, including
through passthrough. No schema-name inference or runtime backend selection occurs.

## Trust and ownership

Graphics.Abstractions issues local generation identities through restricted friend
access. Local generations cannot issue retirement proofs. Actual backend devices
may supply constructor-fixed trusted retirement authorities; arbitrary public
interface implementations and wrapped authorities do not grant issuance. Recovery
requires the exact retained authority, device and generation proof. Validation
wrappers intentionally do not certify a raw backend retirement domain.

Artifact leases transfer dependency ownership only after constructor validation.
Active uses block cleanup. Failed cleanup retains ownership for explicit retry;
build failures and prepared results likewise retain failed cleanup owners.
Borrowed devices, external resources and caller pipelines are not implicitly
disposed. No API promises a finite native drain or forces device loss.

## Program identity encoding

BackendTarget is the exact case-sensitive ShaderTarget name. ProgramHash and
ReflectedAbiHash are uppercase SHA-256. Each ordered module contributes its
little-endian Int32 byte length followed by bytes: complete ShaderArtifact binary
for ProgramHash, UTF-8 target reflection JSON for ReflectedAbiHash. Module order is
vertex/optional fragment, compute, or optional task/mesh/optional fragment.
Actual descriptors, shader stages, target availability and native device origins
are checked rather than trusting hash declarations. Canonical schema/extent and
concrete stage ABI preflight remain separate from binary identity.

## Verification scope

The test project `tests/Graphics/Lumyte.Graphics.Pipelines.Tests` contains managed
contract tests and opt-in actual Vulkan upload/readback, failure, quarantine and
retirement tests. `LUMYTE_GRAPHICS_GPU_TESTS=1` enables native cases. Use the pinned
.NET 10.0.403 SDK and a real Vulkan driver. Compilation on another backend is not
runtime qualification, and shader artifact compilation is not pixel correctness.
