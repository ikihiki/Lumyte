# Lumyte.Pipelines — Core CPU slice

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
thread pool is created. PipelineJobBridge lowers to existing graph/workflow queues.

Inputs, arbitrary published values and custom metadata marked immutable are a
**caller/extension immutability contract**, not reflective deep-cloned objects.
Never mutate them while retained. Dictionary containers are snapshotted; mutable
arrays as schema metadata are rejected. Host schema data uses an owned byte copy;
ReadElements exposes only a read-only span. CPU contexts reject non-CPU placement.
Device values/bundles, Graphics/effect/RenderGraph/table lowering, upload/download,
GPU submission, caches and Graphics execution sessions are not implemented here.

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

PipelineBudget limits declared nodes, retained runs, and per-run declared host
bytes. At most six incoming edges per node bounds total graph work. ExecutionRuntime
separately reserves graph queue/work metadata. Build describes upper bounds before
Create and rejects impossible minimum requirements. **Actual cumulative bytes are
reserved just before each stage invocation**, before that stage allocates; later
rejection does not roll back earlier work, but cleans all its owners. Checked
arithmetic rejects overflow. Retaining results consumes in-flight capacity.

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
