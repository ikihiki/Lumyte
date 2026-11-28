# Lumyte.Pipelines — typed graph and backend contracts

Implements the Graphics-independent typed DAG boundary in
[ADR-GRAPHICS-0046](../../../docs/adr/graphics/GRAPHICS-0046-unified-graphics-execution.md).
`PipelineGraph<TInput>.Stage` supports one through six typed inputs; `Port<T>.Then`
is the unary spelling. Each addition is a new producer; reusing a port shares
its producer. Append-only edges make cycles unrepresentable through public API.
Complete freezes the graph. DescribeDataflow returns captured read-only topology
and canonical schema metadata. All declared nodes execute, including unselected
branches; this slice does not perform liveness culling.

## Extension and execution contract

External consumers implement IStage, IStageFactory and IStageProcessor using only
public API. Factories/declarations must be immutable, Describe must be pure, and
Create must transfer a fresh independent processor owner. Processors are shared
across runs and **must be reentrant**, including across awaits. MaxInFlight bounds
running and retained results together. The host borrows ExecutionRuntime; its
caller starts, pumps and stops that existing runtime. No second scheduler or
thread pool is created. PipelineJobBridge lowers to existing graph/workflow queues. At run admission,
Cooperative mode lowers the typed pipeline's compiled dependency program into one
existing runtime workflow. A finite ready list runs synchronous processors inline;
uncompleted processors are retained while other ready branches start. Their
completion notifications return to the same workflow context, which also owns
actual yields after ShouldYield checkpoints. The interpreter does not dispatch to
another runtime, scheduler or thread pool. The first failure suppresses unstarted
work; all already started operations and their contexts drain before it is thrown.
As with Execution JobRun, the first observed handler exception (including its
original instance) is retained. A handler-thrown OperationCanceledException is a
handler fault and is never demoted by its type; actual graph/token cancellation
is recorded separately by the logical-stage checkpoint.

ThreadPool mode instead uses strict single-consumer chain segments and independent
existing-runtime fork/join jobs, preserving independent CPU branch parallelism.
Both plans are built without requiring runtime preparation; the backend is selected
only at run admission. Every declared external processor still executes. Each
logical stage start is checked under the runtime lock against graph failure, user
cancellation and CancelPending shutdown. Already started asynchronous stages drain
before any run ownership is released. Contexts, producer identities and leases are
never pooled or reopened. Bridge checkpoint scopes also close at workflow terminal
completion and reject subsequent starts.

Inputs, arbitrary published values and custom metadata marked immutable are a
**caller/extension immutability contract**, not reflective deep-cloned objects.
Never mutate them while retained. Dictionary containers are snapshotted; mutable
arrays as schema metadata are rejected. Host schema data uses an owned byte copy;
ReadElements exposes only a read-only span. CPU contexts reject non-CPU placement.
Core supplies opaque declaration and recursive bundle contracts, but no GPU implementation.
Graphics/effect/RenderGraph/table lowering, upload/download, submission, caches and
Graphics execution sessions remain the responsibility of an explicitly supplied backend.

A context admits only this invocation's input leases and expires after processor
completion. Publish/Forward creates exactly one output lease. PublishOwned
transfers its independent cleanup lease on successful publication; on publication
failure the caller still owns it. Successful results retain all invocation owners
and processors independently of pipeline disposal. Dispose closes result reads;
failed cleanup can be retried, without returning successful owners twice.

Cancellation is cooperative. Queued work and await continuations require runtime
pumping to reach a real terminal state. PrepareAsync never releases owners merely
because a waiting token was canceled; it awaits JobRun.Completion first. An
uncooperative asynchronous processor can prevent completion indefinitely. Pipeline
Dispose rejects new runs but does not secretly wait or destroy active owners.
Failed-run cleanup is retained by the pipeline and retried by its Dispose.

## Finite budgets

Schema-free `IImmutablePipelineInput` values require an explicit finite application
element ceiling, for example `new PipelineBudget(64, 4096, 1, maxInputElements: 10)`.
Build uses that ceiling rather than inventing a one-element extent; run admission
rejects larger inputs before invoking any processor. A dynamic metadata value
produced by another stage requires its producer to declare `OutputMaxElements`,
and synchronous and asynchronous publication both validate that output ceiling.
The original three-argument constructor remains available for scalar/schema inputs.

PipelineBudget limits declared nodes, retained runs, and per-run declared host
bytes. At most six incoming edges per node bounds total graph work. ExecutionRuntime
separately reserves graph queue/work metadata. Its MaxGraphNodes counts physical
lowered nodes + lowered edges + queued workers, not logical pipeline stages;
PipelineBudget.MaxNodes continues to bound logical stages. The cooperative bridge
also charges an explicit checked per-run metadata allowance (logical-stage
contexts/input arrays/producers/continuations, ready/pending/completion slots and
the value table; 1024 bytes per logical stage plus fixed/table metadata) to the existing
graph byte reservation until JobRun.Dispose. Immutable compiled plans are owned
by the pipeline, rather than allocated per run. Build describes upper bounds before
Create and rejects impossible minimum requirements. **Actual cumulative bytes are
reserved just before each stage invocation**, before that stage allocates; later
rejection does not roll back earlier work, but cleans all its owners. Checked
arithmetic rejects overflow. Retaining results consumes in-flight capacity.

For closed scalar payload types that cannot provide placement or input metadata,
Build reuses immutable actual input descriptions by arity. Describe itself still
runs before every stage and all actual requirement, host, extension and cumulative
reservation validation remains in place. Dynamic IImmutablePipelineInput and
Host/schema metadata use the general live validation path.

Stage requirements must conservatively include every extension-owned payload byte,
not just references. Framework publication charges lease/value metadata; CopyHostData
also charges copied canonical bytes. The runtime cannot measure arbitrary reference
object graphs or police allocations performed directly by a malicious processor.
Initial borrowed input bytes are caller-owned, not charged as newly owned payload.
The declared payload retained by active pipeline runs is bounded by
`MaxHostBytes * MaxInFlight`, separately from graph/runtime metadata and caller-owned input
storage. Copied Host snapshots can remain reachable in caller code after result
disposal; these managed copies are caller retention, not a process-heap bound
guaranteed by the in-flight accounting.

## Public-only consumer

See tests/Core/Lumyte.Pipelines.Tests: the test assembly has no friend access and
all stages use the same public factory/processor contracts as an external author.
For example:

~~~csharp
var graph = new PipelineGraph<int>();
var output = graph.Input.Then(myImmutableStage);
var definition = graph.Complete(output);
using var host = new PipelineBuildHost(runtime, new PipelineBudget(16, 4096, 2));
using var pipeline = host.Build(definition);
// Keep the caller-owned runtime pumped while awaiting this operation.
using var result = await pipeline.PrepareAsync(42, cancellationToken);
Console.WriteLine(result.Value);
~~~

Canonical final outputs are publicly readable without friend access:

~~~csharp
Host<MyFloatSchema> snapshot = canonicalResult.Value;
Console.WriteLine(snapshot.Extent.ElementCount);
ReadOnlySpan<float> values = snapshot.ReadElements<float>();
~~~

The descriptor and element layout are captured at copy time. A copied Host keeps
its immutable managed byte snapshot alive even after the result is disposed; it
does not expose pooled or native storage. A default/uninitialized Host is rejected.

Core graph/schema/lease code is shared within this library. No Graphics
owner/runner, Graphics friend assembly or separate legacy execution path is used.

## Captured compiler and same-graph templates

PipelineDefinition.Visit(IPipelineDefinitionVisitor) is a compiler seam separate from
DescribeDataflow diagnostics. Six generic Stage overloads receive the original typed
factory instance (including private generic implementations and immutable parameters),
typed producer edges and the output edge. Edge zero is the input; OutputEdge selects
the result. No factory reconstruction, reflection or type-name dispatch is required.
Visitors must not mutate factories.

PipelineGraph.Invoke(definition,input) appends rebound logical nodes to this graph.
Repeated invocations make distinct bounded instances while retaining intra-template
sharing. Identity definitions add no node. The input must belong to the open parent;
schemas validate before append. Flattened nodes share MaxNodes, host budgets and one
runtime dependency program. Never implement templates by recursively calling independent
pipelines' PrepareAsync and joining their leases.

## Explicit backend and transactions

Pass a borrowed IPipelineBackend as PipelineBuildHost's third constructor argument.
Core calls Validate for every compiled requirement before any Create, supplies each
node's BuildCapabilities to the original factory, and continues to own creation,
execution, actual host validation, cumulative reservations and cleanup.
PrepareCapabilities receives compiled and actual requirements: the backend must reject
unsupported or growing extensions before returning a capability. Null capabilities
fail rather than selecting a fallback. The default CPU host rejects extension
requirements before Create.

Use fresh per-invocation capabilities for IStageScopeBinding or IStageBackendCapabilities.
Core rejects reuse by object identity, including sequential reuse across compiled pipelines. Bind runs inside Core's
failure boundary; Complete(true) runs only after output identity and schemas validate.
Any processor, publication or output validation failure calls Complete(false). If
Complete(true) throws, Core invokes Complete(false) before failing the run. Hook failures
are aggregated with primary failures. Rollback must handle partial Bind and partial
commit; it must not undo earlier successful invocations or external GPU effects. This
is a declaration transaction, not whole-frame atomic submission. All context/backend
views expire before completion hooks.

IStageBackendCapabilities alone grants nothing: only the exact capability supplied by
the explicit backend can obtain context.Backend(capability). Its StageBackendScope
reads exact admitted input leases as immutable declarations, publishes one declaration
under the same host budget, and Owns an independent cleanup lease before output exists.
IStageBackendCapabilities also requires IStageBackendValueValidation. Every generic
StageBackendScope.Read calls `ValidateRead<T>` after expiry and exact-input admission,
before returning the declaration. Every Publish calls `ValidatePublication<T>` before
quota charging or lease creation. Implement backend frame/resource provenance checks
and input-producer registration in these common generic hooks, including all opaque
children of supported typed bundles; specialized backend facade methods alone are not
a security boundary. Hooks must respect finite explicit shape traversal and propagate
validation exceptions. An escaping hook failure uses the same invocation rollback and
owned cleanup path as processor failures; rejected publication has consumed no quota
or output slot. Both read and publication hooks run under a common invocation
reentrancy guard. During either callback, all output routes (CPU Publish/PublishOwned,
PublishHost, Forward and generic BackendPublish), CopyHostData, PlaceHost, backend
Own, and recursive backend value validation are rejected before scope state changes.
Read-only CPU input access is still subject to the ordinary CPU policy. Hooks may
record backend-specific dependency metadata, but must not mutate the Core invocation
scope. The guard is always cleared in finally; callbacks that reject or throw do not
consume quota/output slots, transfer rejected owners or prevent a later legal publish.
BackendPublish rechecks publication eligibility after the callback. No extra objects
or callback dispatch were added to the default CPU hot path; only guard checks apply.
It never exposes ValueLease.Value, run/producer identities or native device access.
Saved or foreign views cannot read other invocation inputs or publish after expiry.
Own transfers cleanup immediately, including before publication failure; PublishOwned
transfers only after successful CPU publication. Rejected resources remain caller-owned.
CPU publication also checks canonical Host provenance recursively inside bundles; use
Forward for an admitted bundle rather than repackaging foreign canonical data.
PlaceHost wraps only data copied by the current invocation, without consuming the
one output slot, allowing a fresh canonical placement inside a supported output bundle.
Successful owners live through the result; failed cleanup is retryable via pipeline
Dispose. Backend resource bytes must be included in finite requirements; Core cannot
measure arbitrary reference object graphs or sandbox malicious extension allocations.

## Payload shapes and author contracts

CPU Read, Publish and Forward accept known scalars, Host, explicit ICpuPipelineValue
payloads and recursively CPU-readable supported bundles. Unknown arbitrary composites
are rejected rather than reflectively inspected. IImmutablePipelineInput certifies only
metadata/immutability, not absence of opaque placements. Implement ICpuPipelineValue
only for pure CPU data: every contained field must be immutable and contain no Host,
opaque placement, ValueLease, SchemaData, resource/session owner or other
provenance-bearing child at any depth. Placement-containing values must use explicit
typed recursive bundle shapes so Core can validate every placement. A custom CPU
wrapper hiding a saved Host violates the marker contract even without any Device.
Not every provenance-bearing type is a supported bundle leaf: use Host for canonical
placements and scope-validated ValueLease parameters rather than nesting leases or
owners in payloads. This marker is an author declaration, not a runtime proof or
reflective deep validation; as with factory purity, it is not a malicious-code sandbox.

PipelineBundle<A,B> is recursively supported. Device-containing bundles remain opaque
as a whole: CPU code cannot Read or Forward to extract just the host child. Backend
code can read their declarations using a genuine scope. Custom shapes implement
IPipelineBundle.VisitChildren and visit all immediate children exactly once,
deterministically and with their real generic type, including default children.
Traversal is bounded to 16 levels and 64 total values. Schema metadata must be
available before actual input exists: use default-describable immutable value types,
as with Host. Reference-type placements/bundles unable to describe their default are
unsupported. Child schemas are captured and revalidated on Visit, Invoke, Build and
Prepare, and against actual inputs/outputs before commit. Null placement extents are
uninitialized and rejected, including nested children. Neither shape nor CPU markers
authorize changing captured shape/schema while retained. INonCpuReadablePlacement
exposes only schema/extent diagnostics, not a GPU resource capability. Core has no
Graphics references or friend-only extension path.
