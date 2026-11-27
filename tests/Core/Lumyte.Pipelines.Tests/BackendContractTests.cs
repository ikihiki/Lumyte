using System.Collections.Concurrent;
using Lumyte.Execution;
using Xunit;

namespace Lumyte.Pipelines.Tests;

/// <summary>External-assembly tests using only the public backend contracts.</summary>
public sealed class BackendContractTests
{
    /// <summary>Opaque declarations are accessible only through a genuine expiring backend scope.</summary>
    [Fact]
    public void ExternalBackendReadsPublishesAndExpires()
    {
        StageBackendScope? saved = null;
        ValueLease<Opaque>? admitted = null;
        var backend = new Backend();
        var graph = new PipelineGraph<Opaque>();
        var stage = new Stage<Opaque, Opaque>((context, input) =>
        {
            Capabilities capability = Assert.IsType<Capabilities>(context.Capabilities);
            Assert.Throws<InvalidOperationException>(() => context.Backend(new Capabilities()));
            Assert.Throws<InvalidOperationException>(() => context.Read(input));
            Assert.Throws<InvalidOperationException>(() => context.Forward(input));
            Assert.Throws<InvalidOperationException>(() => context.Publish(new Opaque(8)));
            saved = context.Backend(capability);
            admitted = input;
            Opaque declaration = saved.Read(input);
            ValueLease<Opaque> result = saved.Publish(new Opaque(declaration.Id + 1));
            Assert.Throws<InvalidOperationException>(() => saved.Publish(declaration));
            Assert.Throws<InvalidOperationException>(() => saved.Read(result));
            return result;
        });
        Execute(graph.Complete(graph.Stage(stage, graph.Input)), new Opaque(4), backend, result => Assert.Equal(5, result.Value.Id));
        Assert.Throws<ObjectDisposedException>(() => saved!.Read(admitted!));
        Assert.Throws<ObjectDisposedException>(() => saved!.Publish(new Opaque(9)));
        Assert.Equal([true], backend.Scopes.Single().Completions);
    }

    /// <summary>The default CPU host never admits an opaque root, even for an identity definition.</summary>
    [Fact]
    public void CpuIdentityRejectsOpaqueAndUnknownComposites()
    {
        var opaque = new PipelineGraph<Opaque>();
        Assert.Throws<InvalidOperationException>(() => Execute(opaque.Complete(opaque.Input), new Opaque(1), null));
        var unknown = new PipelineGraph<Hidden>();
        Assert.Throws<InvalidOperationException>(() => Execute(unknown.Complete(unknown.Input), new Hidden(new Opaque(1)), null));
        var metadataOnly = new PipelineGraph<MetadataOnly>();
        Assert.Throws<InvalidOperationException>(() => Execute(metadataOnly.Complete(metadataOnly.Input), new MetadataOnly(), null));
    }

    /// <summary>Immutability metadata is not a certificate that an arbitrary payload is CPU-readable.</summary>
    [Fact]
    public void CpuPublishRejectsUnknownShapeBeforeOwnershipTransfer()
    {
        var owner = new Owner();
        var graph = new PipelineGraph<int>();
        var stage = new Stage<int, Hidden>((context, input) => context.PublishOwned(new Hidden(new Opaque(1)), owner), extensions: false);
        Assert.Throws<InvalidOperationException>(() => Execute(graph.Complete(graph.Stage(stage, graph.Input)), 1, null));
        Assert.Equal(0, owner.Disposals);
    }

    /// <summary>Device-containing nested supported bundles are recursively inaccessible on CPU.</summary>
    [Fact]
    public void RecursiveBundlesStayOpaque()
    {
        var value = new PipelineBundle<int, PipelineBundle<Opaque, int>>(3, new(new Opaque(4), 5));
        Assert.False(PipelineValueSafety.IsCpuReadable(value));
        Assert.True(PipelineValueSafety.IsCpuReadable(new PipelineBundle<int, PipelineBundle<int, string>>(1, new(2, "three"))));
        var graph = new PipelineGraph<int>();
        var stage = new Stage<int, PipelineBundle<int, PipelineBundle<Opaque, int>>>((context, input) =>
        {
            Assert.Throws<InvalidOperationException>(() => context.Publish(value));
            return context.Backend((IStageBackendCapabilities)context.Capabilities).Publish(value);
        });
        Port<PipelineBundle<int, PipelineBundle<Opaque, int>>> output = graph.Stage(stage, graph.Input);
        var consumer = new Stage<PipelineBundle<int, PipelineBundle<Opaque, int>>, int>((context, input) =>
        {
            Assert.Throws<InvalidOperationException>(() => context.Read(input));
            Assert.Throws<InvalidOperationException>(() => context.Forward(input));
            StageBackendScope scope = context.Backend((IStageBackendCapabilities)context.Capabilities);
            return scope.Publish(scope.Read(input).Second.First.Id);
        });
        Execute(graph.Complete(graph.Stage(consumer, output)), 0, new Backend(), result => Assert.Equal(4, result.Value));
    }

    /// <summary>Requirements are validated before Create and again before invocation.</summary>
    [Fact]
    public void RequirementsCannotGrowAfterBuild()
    {
        var backend = new Backend();
        var graph = new PipelineGraph<int>();
        var stage = new Stage<int, int>((context, input) => context.Publish(context.Read(input)), actualExtension: 2);
        Assert.Throws<InvalidOperationException>(() => Execute(graph.Complete(graph.Stage(stage, graph.Input)), 1, backend));
        Assert.Equal(1, stage.Creates);
        Assert.Empty(backend.Scopes);
    }

    /// <summary>A CPU host rejects extension requirements before creating a processor.</summary>
    [Fact]
    public void CpuRejectsExtensionBeforeCreate()
    {
        var graph = new PipelineGraph<int>();
        var stage = new Stage<int, int>((context, input) => context.Publish(1));
        using var host = new PipelineBuildHost(new ExecutionRuntime([]), new PipelineBudget(8, 4096, 2));
        Assert.Throws<NotSupportedException>(() => host.Build(graph.Complete(graph.Stage(stage, graph.Input))));
        Assert.Equal(0, stage.Creates);
    }

    /// <summary>Resources registered before any output are cleaned on failure.</summary>
    [Fact]
    public void EarlyOwnedResourceIsCleanedAndTransactionRolledBack()
    {
        var owner = new Owner();
        var backend = new Backend();
        StageBackendScope? saved = null;
        var graph = new PipelineGraph<int>();
        var stage = new Stage<int, int>((context, input) =>
        {
            saved = context.Backend((IStageBackendCapabilities)context.Capabilities);
            saved.Own(owner);
            throw new InvalidOperationException("lower failed before publication");
        });
        Assert.Throws<InvalidOperationException>(() => Execute(graph.Complete(graph.Stage(stage, graph.Input)), 1, backend));
        Assert.Equal(1, owner.Disposals);
        Assert.Equal([false], backend.Scopes.Single().Completions);
        Assert.Throws<ObjectDisposedException>(() => saved!.Own(new Owner()));
    }

    /// <summary>Successful publication does not commit a processor that subsequently fails.</summary>
    [Fact]
    public void FailureAfterPublicationRollsBack()
    {
        var backend = new Backend();
        var owner = new Owner();
        var graph = new PipelineGraph<int>();
        var stage = new Stage<int, int>((context, input) =>
        {
            context.PublishOwned(1, owner);
            throw new InvalidOperationException("after publish");
        });
        Assert.Throws<InvalidOperationException>(() => Execute(graph.Complete(graph.Stage(stage, graph.Input)), 0, backend));
        Assert.Equal([false], backend.Scopes.Single().Completions);
        Assert.Equal(1, owner.Disposals);
    }

    /// <summary>A failing success hook is followed by rollback and cleanup, never a public result.</summary>
    [Fact]
    public void CommitFailureRollsBackAndReleasesOwners()
    {
        var backend = new Backend { FailCommit = true };
        var owner = new Owner();
        var graph = new PipelineGraph<int>();
        var stage = new Stage<int, int>((context, input) => context.PublishOwned(1, owner));
        Assert.Throws<InvalidOperationException>(() => Execute(graph.Complete(graph.Stage(stage, graph.Input)), 0, backend));
        Assert.Equal([true, false], backend.Scopes.Single().Completions);
        Assert.Equal(1, owner.Disposals);
    }

    /// <summary>Backend allocation metadata shares the same finite host ceiling as CPU publication.</summary>
    [Fact]
    public void BackendCannotBypassHostBudget()
    {
        var backend = new Backend();
        var graph = new PipelineGraph<int>();
        var stage = new Stage<int, int>((context, input) => context.Backend((IStageBackendCapabilities)context.Capabilities).Publish(1), hostBytes: 0);
        Assert.Throws<InvalidOperationException>(() => Execute(graph.Complete(graph.Stage(stage, graph.Input)), 0, backend));
        Assert.Equal([false], backend.Scopes.Single().Completions);
    }

    /// <summary>Two overlapping runs receive different capabilities and cannot use one another's input references.</summary>
    /// <returns>The completed overlapping-run test.</returns>
    [Fact]
    public async Task TwoRunsRejectCrossRunReferencesAsync()
    {
        using var runtime = new RuntimeScope();
        var backend = new Backend();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var seen = new ConcurrentQueue<(StagePreparationContext Context, ValueLease<int> Input)>();
        var graph = new PipelineGraph<int>();
        var stage = new AsyncStage(async (context, input) =>
        {
            seen.Enqueue((context, input));
            if (seen.Count == 2)
            {
                gate.SetResult();
            }

            await gate.Task;
            StageBackendScope scope = context.Backend((IStageBackendCapabilities)context.Capabilities);
            (StagePreparationContext Context, ValueLease<int> Input) foreign = seen.Single(item => !ReferenceEquals(item.Input, input));
            Assert.Throws<InvalidOperationException>(() => scope.Read(foreign.Input));
            Assert.Throws<InvalidOperationException>(() => context.Backend((IStageBackendCapabilities)foreign.Context.Capabilities));
            return scope.Publish(scope.Read(input) + 1);
        });
        using var host = new PipelineBuildHost(runtime.Runtime, new PipelineBudget(8, 4096, 2), backend);
        using IPipeline<int, int> pipeline = host.Build(graph.Complete(graph.Stage(stage, graph.Input)));
        Task<PipelineResult<int>> first = pipeline.PrepareAsync(1).AsTask();
        Task<PipelineResult<int>> second = pipeline.PrepareAsync(5).AsTask();
        runtime.Pump(Task.WhenAll(first, second));
        using PipelineResult<int> a = await first;
        using PipelineResult<int> b = await second;
        Assert.Equal(2, a.Value);
        Assert.Equal(6, b.Value);
        Assert.Equal(2, backend.Scopes.Count);
        Assert.All(backend.Scopes, scope => Assert.Equal([true], scope.Completions));
    }

    /// <summary>Implementing a marker cannot forge authorization on a CPU-owned invocation.</summary>
    [Fact]
    public void CpuContextRejectsForgedBackendCapability()
    {
        var graph = new PipelineGraph<int>();
        var stage = new Stage<int, int>(
            (context, input) =>
            {
                Assert.Throws<InvalidOperationException>(() => context.Backend(new Capabilities()));
                return context.Publish(context.Read(input));
            },
            extensions: false);
        Execute(graph.Complete(graph.Stage(stage, graph.Input)), 1, null, result => Assert.Equal(1, result.Value));
    }

    /// <summary>The host never binds a mutable backend capability to two logical invocations.</summary>
    [Fact]
    public void ReusedBackendCapabilityIsRejected()
    {
        var backend = new Backend { ReuseCapability = true };
        var graph = new PipelineGraph<int>();
        var stage = new Stage<int, int>((context, input) => context.Publish(context.Read(input)));
        Port<int> first = graph.Stage(stage, graph.Input);
        Assert.Throws<InvalidOperationException>(() => Execute(graph.Complete(graph.Stage(stage, first)), 1, backend));
        Assert.Equal([true], backend.Scopes.First().Completions);
    }

    /// <summary>A non-CPU execution domain cannot accidentally use CPU methods on ordinary payloads.</summary>
    [Fact]
    public void ExplicitCpuPolicyIsRespected()
    {
        var backend = new Backend { DisableCpu = true };
        var graph = new PipelineGraph<int>();
        var stage = new Stage<int, int>((context, input) =>
        {
            Assert.Throws<InvalidOperationException>(() => context.Read(input));
            Assert.Throws<InvalidOperationException>(() => context.Forward(input));
            Assert.Throws<InvalidOperationException>(() => context.Publish(1));
            StageBackendScope scope = context.Backend((IStageBackendCapabilities)context.Capabilities);
            return scope.Publish(scope.Read(input));
        });
        Execute(graph.Complete(graph.Stage(stage, graph.Input)), 2, backend, result => Assert.Equal(2, result.Value));
    }

    /// <summary>Rollback failures preserve the original processor error and still release owned resources.</summary>
    [Fact]
    public void RollbackFailurePreservesPrimaryFailure()
    {
        var backend = new Backend { FailRollback = true };
        var owner = new Owner();
        var primary = new InvalidOperationException("processor failed");
        var graph = new PipelineGraph<int>();
        var stage = new Stage<int, int>((context, input) =>
        {
            context.Backend((IStageBackendCapabilities)context.Capabilities).Own(owner);
            throw primary;
        });
        AggregateException failure = Assert.Throws<AggregateException>(() => Execute(graph.Complete(graph.Stage(stage, graph.Input)), 0, backend));
        Assert.Same(primary, failure.InnerExceptions[0]);
        Assert.Equal("rollback failed", failure.InnerExceptions[1].Message);
        Assert.Equal(1, owner.Disposals);
        Assert.Equal([false], backend.Scopes.Single().Completions);
    }

    /// <summary>Opaque roots contribute their captured canonical stride to upper-bound requirements.</summary>
    [Fact]
    public void OpaqueRootBuildMetadataUsesCapturedSchema()
    {
        bool sawUpperBound = false;
        var graph = new PipelineGraph<Opaque>();
        var stage = new Stage<Opaque, Opaque>(
            (context, input) => context.Backend((IStageBackendCapabilities)context.Capabilities).Publish(new Opaque(1)),
            inspect: inputs =>
            {
                if (inputs.Extents[0].Metadata.ContainsKey("UpperBound"))
                {
                    sawUpperBound = true;
                    Assert.Equal(1024, inputs.Extents[0].ElementCount);
                }
            });
        Execute(graph.Complete(graph.Stage(stage, graph.Input)), new Opaque(1), new Backend());
        Assert.True(sawUpperBound);
    }

    /// <summary>A changed nested canonical schema fails before the transaction success hook.</summary>
    [Fact]
    public void ChangedOutputBundleSchemaRollsBack()
    {
        MutableOpaque.Version = 1;
        var backend = new Backend();
        var graph = new PipelineGraph<int>();
        var stage = new Stage<int, PipelineBundle<int, MutableOpaque>>((context, input) =>
        {
            MutableOpaque.Version = 2;
            return context.Backend((IStageBackendCapabilities)context.Capabilities).Publish(new PipelineBundle<int, MutableOpaque>(1, default));
        });
        try
        {
            Assert.Throws<InvalidOperationException>(() => Execute(graph.Complete(graph.Stage(stage, graph.Input)), 0, backend));
            Assert.Equal([false], backend.Scopes.Single().Completions);
        }
        finally
        {
            MutableOpaque.Version = 1;
        }
    }

    /// <summary>Wrapping a foreign canonical host in a supported bundle does not bypass CPU publication provenance.</summary>
    [Fact]
    public void CpuBundleKeepsCanonicalHostProvenanceAndForwarding()
    {
        Host<IntSchema> source = default;
        var producer = new PipelineGraph<int>();
        var copy = new Stage<int, Host<IntSchema>>(
            (context, input) => context.PublishHost(context.CopyHostData<IntSchema, int>([7], new SchemaExtent(1, new Dictionary<string, object>()))),
            extensions: false,
            hostBytes: 512);
        Execute(producer.Complete(producer.Stage(copy, producer.Input)), 0, null, result => source = result.Value);
        var graph = new PipelineGraph<PipelineBundle<Host<IntSchema>, int>>();
        var stage = new Stage<PipelineBundle<Host<IntSchema>, int>, PipelineBundle<Host<IntSchema>, int>>(
            (context, input) =>
            {
                PipelineBundle<Host<IntSchema>, int> value = context.Read(input);
                Assert.Throws<InvalidOperationException>(() => context.Publish(value));
                return context.Forward(input);
            },
            extensions: false);
        Execute(graph.Complete(graph.Stage(stage, graph.Input)), new PipelineBundle<Host<IntSchema>, int>(source, 3), null, result => Assert.Equal(7, result.Value.First.ReadElements<int>()[0]));
    }

    /// <summary>Direct, nested and opaque-mixed CPU publication cannot rebind a canonical host saved from another run.</summary>
    [Fact]
    public void ForeignHostIsRejectedAtEverySupportedBundleDepth()
    {
        Host<IntSchema> saved = default;
        var producer = new PipelineGraph<int>();
        var copy = new Stage<int, Host<IntSchema>>(
            (context, input) => context.PublishHost(context.CopyHostData<IntSchema, int>([9], new SchemaExtent(1, new Dictionary<string, object>()))),
            extensions: false,
            hostBytes: 512);
        Execute(producer.Complete(producer.Stage(copy, producer.Input)), 0, null, result => saved = result.Value);
        var graph = new PipelineGraph<int>();
        var probe = new Stage<int, int>(
            (context, input) =>
            {
                Assert.Throws<InvalidOperationException>(() => context.Publish(saved));
                Assert.Throws<InvalidOperationException>(() => context.Publish(new PipelineBundle<Host<IntSchema>, int>(saved, 1)));
                Assert.Throws<InvalidOperationException>(() => context.Publish(new PipelineBundle<int, PipelineBundle<int, Host<IntSchema>>>(1, new(2, saved))));
                Assert.Throws<InvalidOperationException>(() => context.Publish(new PipelineBundle<Opaque, Host<IntSchema>>(new Opaque(1), saved)));
                return context.Publish(1);
            },
            extensions: false);
        Execute(graph.Complete(graph.Stage(probe, graph.Input)), 0, null, result => Assert.Equal(1, result.Value));
    }

    /// <summary>Canonical data copied by the current invocation can be placed in nested bundles and published once.</summary>
    [Fact]
    public void CurrentInvocationHostIsAllowedInsideNestedBundles()
    {
        StagePreparationContext? savedContext = null;
        SchemaData<IntSchema>? savedData = null;
        var graph = new PipelineGraph<int>();
        var copy = new Stage<int, PipelineBundle<int, PipelineBundle<Host<IntSchema>, int>>>(
            (context, input) =>
            {
                savedContext = context;
                savedData = context.CopyHostData<IntSchema, int>([11], new SchemaExtent(1, new Dictionary<string, object>()));
                Host<IntSchema> host = context.PlaceHost(savedData);
                return context.Publish(new PipelineBundle<int, PipelineBundle<Host<IntSchema>, int>>(1, new(host, 2)));
            },
            extensions: false,
            hostBytes: 512);
        Execute(graph.Complete(graph.Stage(copy, graph.Input)), 0, null, result => Assert.Equal(11, result.Value.Second.First.ReadElements<int>()[0]));
        Assert.Throws<ObjectDisposedException>(() => savedContext!.PlaceHost(savedData!));

        var foreign = new PipelineGraph<int>();
        var probe = new Stage<int, int>(
            (context, input) =>
            {
                Assert.Throws<ArgumentException>(() => context.PlaceHost(savedData!));
                return context.Publish(0);
            },
            extensions: false);
        Execute(foreign.Complete(foreign.Stage(probe, foreign.Input)), 0, null);
    }

    /// <summary>Partially bound capabilities roll back, expire and clean early owners without masking the original Bind exception.</summary>
    [Fact]
    public void PartialBindFailureRollsBackBeforeProcessorRuns()
    {
        var primary = new InvalidOperationException("partial bind failed");
        var owner = new Owner();
        StageBackendScope? saved = null;
        bool evaluated = false;
        var backend = new Backend
        {
            OnBind = context =>
            {
                saved = context.Backend((IStageBackendCapabilities)context.Capabilities);
                saved.Own(owner);
                throw primary;
            },
        };
        var graph = new PipelineGraph<int>();
        var stage = new Stage<int, int>((context, input) =>
        {
            evaluated = true;
            return context.Publish(0);
        });
        InvalidOperationException observed = Assert.Throws<InvalidOperationException>(() => Execute(graph.Complete(graph.Stage(stage, graph.Input)), 0, backend));
        Assert.Same(primary, observed);
        Assert.False(evaluated);
        Assert.Equal([false], backend.Scopes.Single().Completions);
        Assert.Equal(1, owner.Disposals);
        Assert.Throws<ObjectDisposedException>(() => saved!.Own(new Owner()));
    }

    /// <summary>Backend owners and pending invocation scopes survive independent failure until actual asynchronous drain.</summary>
    [Fact]
    public void BackendOwnersWaitForActualDrain()
    {
        using var runtime = new RuntimeScope();
        var backend = new Backend();
        var owner = new Owner();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var primary = new InvalidOperationException("independent failure");
        var graph = new PipelineGraph<int>();
        var blocked = new AsyncStage(async (context, input) =>
        {
            StageBackendScope scope = context.Backend((IStageBackendCapabilities)context.Capabilities);
            scope.Own(owner);
            started.SetResult();
            await release.Task;
            return scope.Publish(scope.Read(input));
        });
        var failing = new AsyncStage(async (context, input) =>
        {
            await started.Task;
            failed.SetResult();
            throw primary;
        });
        graph.Stage(blocked, graph.Input);
        Port<int> output = graph.Stage(failing, graph.Input);
        using var host = new PipelineBuildHost(runtime.Runtime, new PipelineBudget(8, 4096, 2), backend);
        using IPipeline<int, int> pipeline = host.Build(graph.Complete(output));
        Task<PipelineResult<int>> pending = pipeline.PrepareAsync(1).AsTask();
        runtime.Pump(failed.Task);
        Assert.False(pending.IsCompleted);
        Assert.Equal(0, owner.Disposals);
        Assert.Contains(backend.Scopes, scope => scope.Completions.Count == 0);
        release.SetResult();
        InvalidOperationException observed = Assert.Throws<InvalidOperationException>(() => runtime.Pump(pending));
        Assert.Same(primary, observed);
        Assert.Equal(1, owner.Disposals);
        Assert.Contains(backend.Scopes, scope => scope.Completions.SequenceEqual([true]));
        Assert.Contains(backend.Scopes, scope => scope.Completions.SequenceEqual([false]));
    }

    /// <summary>Generic scopes invoke the same recursive backend guard as a specialized facade, before publication charges.</summary>
    [Fact]
    public void GenericBackendScopesCannotBypassRecursiveValueGuard()
    {
        var validation = new RecordingValueValidation();
        var backend = new Backend { ValueValidation = validation };
        StageBackendScope? saved = null;
        ValueLease<PipelineBundle<int, PipelineBundle<Opaque, int>>>? admitted = null;
        var value = new PipelineBundle<int, PipelineBundle<Opaque, int>>(1, new(new Opaque(7), 2));
        var foreign = new PipelineBundle<int, PipelineBundle<Opaque, int>>(1, new(new Opaque(-7), 2));
        var graph = new PipelineGraph<PipelineBundle<int, PipelineBundle<Opaque, int>>>();
        var stage = new Stage<PipelineBundle<int, PipelineBundle<Opaque, int>>, PipelineBundle<int, PipelineBundle<Opaque, int>>>((context, input) =>
        {
            saved = context.Backend((IStageBackendCapabilities)context.Capabilities);
            admitted = input;
            PipelineBundle<int, PipelineBundle<Opaque, int>> read = saved.Read(input);
            Assert.Equal(7, read.Second.First.Id);
            Assert.Equal([7], validation.ReadProducers);
            InvalidOperationException rejection = Assert.Throws<InvalidOperationException>(() => saved.Publish(foreign));
            Assert.Same(validation.Rejection, rejection);
            ValueLease<PipelineBundle<int, PipelineBundle<Opaque, int>>> output = saved.Publish(read);
            Assert.Throws<InvalidOperationException>(() => saved.Read(output));
            Assert.Throws<InvalidOperationException>(() => saved.Publish(read));
            return output;
        });
        Execute(graph.Complete(graph.Stage(stage, graph.Input)), value, backend, result => Assert.Equal(7, result.Value.Second.First.Id));
        Assert.Equal(1, validation.ReadCalls);
        Assert.Equal(2, validation.PublicationCalls);
        Assert.Throws<ObjectDisposedException>(() => saved!.Read(admitted!));
        Assert.Throws<ObjectDisposedException>(() => saved!.Publish(value));
        Assert.Equal(1, validation.ReadCalls);
        Assert.Equal(2, validation.PublicationCalls);
    }

    /// <summary>Admitted foreign declarations cannot escape generic reads; callback exceptions roll back and preserve cleanup.</summary>
    [Fact]
    public void GenericReadGuardRejectsNestedForeignInputAndPreservesException()
    {
        var validation = new RecordingValueValidation();
        var backend = new Backend { ValueValidation = validation };
        var owner = new Owner();
        var graph = new PipelineGraph<PipelineBundle<int, PipelineBundle<Opaque, int>>>();
        var stage = new Stage<PipelineBundle<int, PipelineBundle<Opaque, int>>, int>((context, input) =>
        {
            StageBackendScope scope = context.Backend((IStageBackendCapabilities)context.Capabilities);
            scope.Own(owner);
            scope.Read(input);
            return scope.Publish(1);
        });
        var foreign = new PipelineBundle<int, PipelineBundle<Opaque, int>>(1, new(new Opaque(-1), 2));
        InvalidOperationException rejection = Assert.Throws<InvalidOperationException>(() => Execute(graph.Complete(graph.Stage(stage, graph.Input)), foreign, backend));
        Assert.Same(validation.Rejection, rejection);
        Assert.Equal(1, validation.ReadCalls);
        Assert.Equal(0, validation.PublicationCalls);
        Assert.Empty(validation.ReadProducers);
        Assert.Equal([false], backend.Scopes.Single().Completions);
        Assert.Equal(1, owner.Disposals);
    }

    /// <summary>Generic declaration publication validates backend provenance before host-budget failure or output creation.</summary>
    [Fact]
    public void GenericPublicationGuardRunsBeforeZeroBudgetReservation()
    {
        var validation = new RecordingValueValidation();
        var backend = new Backend { ValueValidation = validation };
        var graph = new PipelineGraph<int>();
        var stage = new Stage<int, Opaque>(
            (context, input) => context.Backend((IStageBackendCapabilities)context.Capabilities).Publish(new Opaque(-1)),
            hostBytes: 0);
        InvalidOperationException rejection = Assert.Throws<InvalidOperationException>(() => Execute(graph.Complete(graph.Stage(stage, graph.Input)), 0, backend));
        Assert.Same(validation.Rejection, rejection);
        Assert.Equal(0, validation.ReadCalls);
        Assert.Equal(1, validation.PublicationCalls);
        Assert.Equal([false], backend.Scopes.Single().Completions);
    }

    /// <summary>A validation callback cannot create and save a second output lease by reentering CPU publication.</summary>
    [Fact]
    public void BackendPublicationHookCannotReenterPublication()
    {
        StagePreparationContext? bound = null;
        ValueLease<int>? smuggled = null;
        var validation = new CallbackValueValidation(publication: () => smuggled = bound!.Publish(123));
        var backend = new Backend { ValueValidation = validation, OnBind = context => bound = context };
        var graph = new PipelineGraph<int>();
        var stage = new Stage<int, int>(
            (context, input) =>
            {
                StageBackendScope scope = context.Backend((IStageBackendCapabilities)context.Capabilities);
                Assert.Throws<InvalidOperationException>(() => scope.Publish(456));
                Assert.Null(smuggled);
                return context.Publish(789);
            },
            hostBytes: 512);
        Execute(graph.Complete(graph.Stage(stage, graph.Input)), 1, backend, result => Assert.Equal(789, result.Value));
    }

    /// <summary>Both read and publication validation reject every output/quota/ownership mutation and recursive validation.</summary>
    /// <param name="duringRead">Whether to exercise the read hook instead of publication hook.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BackendValueHooksCannotMutateInvocationScope(bool duringRead)
    {
        Action? probe = null;
        int attempts = 0;
        var owner = new Owner();
        var validation = new CallbackValueValidation(read: duringRead ? () => probe!() : null, publication: duringRead ? null : () => probe!());
        var backend = new Backend { ValueValidation = validation };
        var graph = new PipelineGraph<int>();
        var stage = new Stage<int, int>(
            (context, input) =>
            {
                SchemaData<IntSchema> data = context.CopyHostData<IntSchema, int>([1], new SchemaExtent(1, new Dictionary<string, object>()));
                StageBackendScope scope = context.Backend((IStageBackendCapabilities)context.Capabilities);
                probe = () =>
                {
                    Assert.Equal(1, context.Read(input));
                    Action[] mutations =
                    [
                        () => context.Publish(123),
                        () => context.PublishOwned(123, owner),
                        () => context.PublishHost(data),
                        () => context.Forward(input),
                        () => scope.Publish(456),
                        () => scope.Own(owner),
                        () => context.CopyHostData<IntSchema, int>([2], new SchemaExtent(1, new Dictionary<string, object>())),
                        () => context.PlaceHost(data),
                        () => scope.Read(input),
                    ];
                    foreach (Action mutate in mutations)
                    {
                        Assert.Throws<InvalidOperationException>(mutate);
                        attempts++;
                    }
                };
                Assert.Equal(1, scope.Read(input));
                return scope.Publish(456);
            },
            hostBytes: Requirements.SchemaDataBytes + sizeof(int) + Requirements.ValueLeaseBytes + sizeof(int));
        try
        {
            Execute(graph.Complete(graph.Stage(stage, graph.Input)), 1, backend, result => Assert.Equal(456, result.Value));
            Assert.Equal(9, attempts);
            Assert.Equal(0, owner.Disposals);
            Assert.Equal([true], backend.Scopes.Single().Completions);
        }
        finally
        {
            owner.Dispose();
        }
    }

    /// <summary>A read-hook exception preserves its identity and clears the guard for a subsequent legal publication.</summary>
    [Fact]
    public void BackendReadHookExceptionDoesNotConsumePublicationOrQuota()
    {
        var primary = new InvalidOperationException("read validation rejected");
        var validation = new CallbackValueValidation(read: () => throw primary);
        var backend = new Backend { ValueValidation = validation };
        var owner = new Owner();
        var graph = new PipelineGraph<int>();
        var stage = new Stage<int, int>(
            (context, input) =>
            {
                StageBackendScope scope = context.Backend((IStageBackendCapabilities)context.Capabilities);
                InvalidOperationException rejection = Assert.Throws<InvalidOperationException>(() => scope.Read(input));
                Assert.Same(primary, rejection);
                return context.PublishOwned(789, owner);
            },
            hostBytes: Requirements.ValueLeaseBytes + sizeof(int));
        Execute(graph.Complete(graph.Stage(stage, graph.Input)), 1, backend, result => Assert.Equal(789, result.Value));
        Assert.Equal(1, owner.Disposals);
        Assert.Equal([true], backend.Scopes.Single().Completions);
    }

    private static void Execute<TA, TO>(PipelineDefinition<TA, TO> definition, TA input, Backend? backend, Action<PipelineResult<TO>>? inspect = null)
    {
        using var runtime = new RuntimeScope();
        using var host = new PipelineBuildHost(runtime.Runtime, new PipelineBudget(32, 4096, 2), backend);
        using IPipeline<TA, TO> pipeline = host.Build(definition);
        Task<PipelineResult<TO>> pending = pipeline.PrepareAsync(input).AsTask();
        runtime.Pump(pending);
        using PipelineResult<TO> result = pending.GetAwaiter().GetResult();
        inspect?.Invoke(result);
    }

    private readonly struct MutableOpaque : INonCpuReadablePlacement
    {
        public SchemaDescriptor Descriptor => new(Version, typeof(int), 4, new Dictionary<string, object>());

        public SchemaExtent Extent => new(1, new Dictionary<string, object>());

        public string DiagnosticPlacement => "mutable-test";

        internal static int Version { get; set; }
    }

    private readonly struct IntSchema : ISchema
    {
        public static SchemaDescriptor Descriptor => new(1, typeof(int), 4, new Dictionary<string, object>());
    }

    private readonly record struct Opaque(int Id) : INonCpuReadablePlacement
    {
        public SchemaDescriptor Descriptor => new(1, typeof(int), 4, new Dictionary<string, object>());

        public SchemaExtent Extent => new(1, new Dictionary<string, object>());

        public string DiagnosticPlacement => "external-declaration";
    }

    private sealed record Requirement(int Units) : IStageExtensionRequirement;

    private sealed record Hidden(Opaque Placement);

    private sealed class MetadataOnly : IImmutablePipelineInput
    {
        public SchemaExtent Description => new(1, new Dictionary<string, object>());
    }

    private sealed class Owner : IDisposable
    {
        internal int Disposals { get; private set; }

        public void Dispose() => Disposals++;
    }

    private sealed class CallbackValueValidation(Action? read = null, Action? publication = null) : IStageBackendValueValidation
    {
        public void ValidateRead<T>(T value) => read?.Invoke();

        public void ValidatePublication<T>(T value) => publication?.Invoke();
    }

    private sealed class RecordingValueValidation : IStageBackendValueValidation, IPipelineBundleVisitor
    {
        private bool _reading;

        internal InvalidOperationException Rejection { get; } = new("foreign backend declaration");

        internal List<int> ReadProducers { get; } = [];

        internal int ReadCalls { get; private set; }

        internal int PublicationCalls { get; private set; }

        public void ValidateRead<T>(T value)
        {
            ReadCalls++;
            _reading = true;
            Child(value);
        }

        public void ValidatePublication<T>(T value)
        {
            PublicationCalls++;
            _reading = false;
            Child(value);
        }

        public void Child<T>(T value)
        {
            if (value is IPipelineBundle bundle)
            {
                bundle.VisitChildren(this);
            }
            else if (value is Opaque opaque)
            {
                if (opaque.Id < 0)
                {
                    throw Rejection;
                }

                if (_reading)
                {
                    ReadProducers.Add(opaque.Id);
                }
            }
        }
    }

    private sealed class Backend : IPipelineBackend
    {
        internal ConcurrentBag<Capabilities> Scopes { get; } = [];

        internal bool FailCommit { get; init; }

        internal bool FailRollback { get; init; }

        internal bool DisableCpu { get; init; }

        internal bool ReuseCapability { get; init; }

        internal IStageBackendValueValidation? ValueValidation { get; init; }

        internal Action<StagePreparationContext>? OnBind { get; init; }

        public void Validate(int node, StageRequirements requirements)
            => Assert.All(requirements.Extensions, requirement => Assert.Equal(1, Assert.IsType<Requirement>(requirement).Units));

        public IStageCapabilities BuildCapabilities(int node) => new Capabilities();

        public IStageCapabilities PrepareCapabilities(int node, StageRequirements compiled, StageRequirements actual)
        {
            if (actual.Extensions.Count != compiled.Extensions.Count || actual.Extensions.Zip(compiled.Extensions).Any(pair => Assert.IsType<Requirement>(pair.First).Units > Assert.IsType<Requirement>(pair.Second).Units))
            {
                throw new InvalidOperationException("actual extension reservation exceeded");
            }

            Capabilities capabilities = ReuseCapability && !Scopes.IsEmpty ? Scopes.First() : new Capabilities { FailCommit = FailCommit, FailRollback = FailRollback, DisableCpu = DisableCpu, OnBind = OnBind, ValueValidation = ValueValidation };
            Scopes.Add(capabilities);
            return capabilities;
        }
    }

    private sealed class Capabilities : IStageBackendCapabilities, IStageScopeBinding, IStageCpuPolicy
    {
        public bool CanReadCpu => !DisableCpu;

        public bool CanPublishCpu => !DisableCpu;

        internal List<bool> Completions { get; } = [];

        internal bool FailCommit { get; init; }

        internal bool FailRollback { get; init; }

        internal bool DisableCpu { get; init; }

        internal Action<StagePreparationContext>? OnBind { get; init; }

        internal IStageBackendValueValidation? ValueValidation { get; init; }

        public void ValidateRead<T>(T value) => ValueValidation?.ValidateRead(value);

        public void ValidatePublication<T>(T value) => ValueValidation?.ValidatePublication(value);

        public void Bind(StagePreparationContext context)
        {
            Assert.Same(this, context.Capabilities);
            OnBind?.Invoke(context);
        }

        public void Complete(bool succeeded)
        {
            Completions.Add(succeeded);
            if (succeeded && FailCommit)
            {
                throw new InvalidOperationException("commit failed");
            }

            if (!succeeded && FailRollback)
            {
                throw new InvalidOperationException("rollback failed");
            }
        }
    }

    private sealed class Stage<TA, TO>(Func<StagePreparationContext, ValueLease<TA>, ValueLease<TO>> evaluate, bool extensions = true, int actualExtension = 1, long hostBytes = 128, Action<StageInputDescriptions>? inspect = null) : IStage<TA, TO>, IStageFactory<TA, TO>, IStageProcessor<TA, TO>
    {
        public IStageFactory<TA, TO> Factory => this;

        internal int Creates { get; private set; }

        public StageRequirements Describe(StageInputDescriptions inputs)
        {
            inspect?.Invoke(inputs);
            bool upperBound = inputs.Extents[0].Metadata.ContainsKey("UpperBound");
            return new StageRequirements(hostBytes, extensions ? [new Requirement(upperBound ? 1 : actualExtension)] : []);
        }

        public IStageProcessor<TA, TO> Create(StageBuildContext context)
        {
            Creates++;
            return this;
        }

        public ValueTask<ValueLease<TO>> PrepareAsync(StagePreparationContext context, ValueLease<TA> a, CancellationToken cancellationToken)
            => ValueTask.FromResult(evaluate(context, a));

        public void Dispose()
        {
        }
    }

    private sealed class AsyncStage(Func<StagePreparationContext, ValueLease<int>, ValueTask<ValueLease<int>>> evaluate) : IStage<int, int>, IStageFactory<int, int>, IStageProcessor<int, int>
    {
        public IStageFactory<int, int> Factory => this;

        public StageRequirements Describe(StageInputDescriptions inputs) => new(512, [new Requirement(1)]);

        public IStageProcessor<int, int> Create(StageBuildContext context) => this;

        public ValueTask<ValueLease<int>> PrepareAsync(StagePreparationContext context, ValueLease<int> a, CancellationToken cancellationToken) => evaluate(context, a);

        public void Dispose()
        {
        }
    }

    private sealed class RuntimeScope : IDisposable
    {
        internal RuntimeScope()
        {
            Runtime.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.Cooperative, 1, new ExecutionOptions());
            Pump(Runtime.StartAsync().AsTask());
        }

        internal ExecutionRuntime Runtime { get; } = new([]);

        public void Dispose() => Pump(Runtime.StopAsync().AsTask());

        internal void Pump(Task task)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (!task.IsCompleted)
            {
                Runtime.PumpHostLifecycle(64, TimeSpan.FromMilliseconds(2));
                Runtime.PumpJobs(64, TimeSpan.FromMilliseconds(2));
                if (watch.Elapsed > TimeSpan.FromSeconds(10))
                {
                    throw new TimeoutException();
                }

                Thread.Yield();
            }

            task.GetAwaiter().GetResult();
        }
    }
}
