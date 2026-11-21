using System.Reflection;
using Lumyte.Execution;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Vulkan;
using Lumyte.Pipelines;
using Xunit;

namespace Lumyte.Graphics.Pipelines.Tests;

/// <summary>Exercises failure retention using actual Vulkan commands and submissions.</summary>
public sealed partial class NativeUploadTests
{
    /// <summary>A genuinely accepted submission with lost receipt remains quarantined until trusted native retirement.</summary>
    [NativeFact]
    public void UnknownNativeAcceptanceRequiresMatchingTrustedProof()
    {
        using var session = new Session<Device<Words>>(graph => graph.Stage(new Producer(), graph.Input).Then(Transfers.Upload<Words>()), trusted: true);
        var context = new InlineContext();
        using GraphicsPipelineExecution execution = session.Result.AcquireExecution(session.Pipeline, context);
        IGraphicsSubmission? accepted = null;
        using var host = new GraphicsPipelineExecutionHost(session.Pipeline, context, session.Device.RetirementAuthority, commands =>
        {
            accepted = session.Device.Queue.Submit(commands);
            throw new InjectedExecutionException("Accepted receipt intentionally lost.");
        });
        Assert.Throws<InjectedExecutionException>(() => Pump(session.Runtime, host.ExecuteAsync(execution).AsTask()));
        Assert.Equal(1, host.RecoveryState.QuarantinedEpochs);
        Assert.Throws<InvalidOperationException>(execution.Dispose);
        Assert.Throws<InvalidOperationException>(host.Dispose);
        AggregateException blocked = Assert.Throws<AggregateException>(() => Pump(session.Runtime, host.DrainAsync().AsTask()));
        Assert.Contains(blocked.InnerExceptions, error => error is PipelineQuarantinedException);
        Assert.Equal(1, host.RecoveryState.QuarantinedEpochs);
        Assert.NotNull(accepted);
        typeof(VulkanDevice).GetMethod("ReportLoss", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(session.Device, ["Injected native loss lifecycle after accepted submission."]);
        Task<GraphicsDeviceRetirementProof> proofTask = session.Device.RetirementAuthority.WaitForRetirementAsync().AsTask();
        Pump(session.Runtime, proofTask);
        Pump(session.Runtime, host.RecoverAsync(proofTask.GetAwaiter().GetResult()).AsTask());
        Assert.Equal(0, host.RecoveryState.QuarantinedEpochs);
        Assert.Equal(0, host.RecoveryState.PendingCleanupOwners);
        execution.Dispose();
        accepted.Dispose();
        session.Result.Dispose();
    }

    /// <summary>An arbitrary wrapper returning the genuine generation is not the issuing authority.</summary>
    [NativeFact]
    public void WrappedAuthorityRejectedBeforeHostAcceptance()
    {
        using var session = new Session<Device<Words>>(graph => graph.Stage(new Producer(), graph.Input).Then(Transfers.Upload<Words>()), trusted: true);
        var fake = new AuthorityWrapper(session.Device.RetirementAuthority);
        Assert.Throws<ArgumentException>(() => new GraphicsPipelineExecutionHost(session.Pipeline, new InlineContext(), fake));
        Assert.Throws<ArgumentException>(() => new GraphicsPipelineBuildHost(session.Runtime, session.Device, new NoArtifacts(), new GraphicsPipelinePolicy(16384, 128, 128, 2), fake));
    }

    /// <summary>A mapping-dispatch failure stops the CPU consumer while known native work still drains.</summary>
    [NativeFact]
    public void MapFailureStopsCpuContinuationAndDrainsNativeWork()
    {
        var probe = new ProbeWords();
        using var session = new Session<Host<Words>>(graph => graph.Stage(new Producer(), graph.Input).Then(Transfers.Upload<Words>()).Then(Transfers.Download<Words>()).Then(probe));
        var context = new FailingContext(2);
        using GraphicsPipelineExecution execution = session.Result.AcquireExecution(session.Pipeline, context);
        using var host = new GraphicsPipelineExecutionHost(session.Pipeline, context);
        Assert.Throws<InjectedExecutionException>(() => Pump(session.Runtime, host.ExecuteAsync(execution).AsTask()));
        Pump(session.Runtime, host.DrainAsync().AsTask());
        Assert.Equal(0, probe.Calls);
        Assert.Equal(0, host.RecoveryState.PendingCleanupOwners);
        execution.Dispose();
        session.Result.Dispose();
    }

    /// <summary>A cleanup failure cannot leave a later readback waiting forever or permit another submission.</summary>
    [NativeFact]
    public void CleanupFailureLatchesStopBeforeFutureReadback()
    {
        var probe = new ProbeWords();
        using var session = new Session<Host<Words>>(graph => graph.Stage(new Producer(), graph.Input).Then(Transfers.Upload<Words>()).Then(Transfers.Download<Words>()).Then(probe).Then(Transfers.Upload<Words>()).Then(Transfers.Download<Words>()));
        var context = new FailingContext(3);
        int submits = 0;
        using GraphicsPipelineExecution execution = session.Result.AcquireExecution(session.Pipeline, context);
        using var host = new GraphicsPipelineExecutionHost(session.Pipeline, context, null, commands =>
        {
            submits++;
            return session.Device.Queue.Submit(commands);
        });
        Assert.Throws<InjectedExecutionException>(() => Pump(session.Runtime, host.ExecuteAsync(execution).AsTask()));
        Pump(session.Runtime, host.DrainAsync().AsTask());
        Assert.Equal(1, submits);
        Assert.Equal(0, probe.Calls);
        Assert.Equal(0, host.RecoveryState.PendingCleanupOwners);
        execution.Dispose();
        session.Result.Dispose();
    }

    /// <summary>A CPU-only result cannot pretend to be a GPU record or execution plan.</summary>
    [NativeFact]
    public void CpuOnlyResultHasNoGpuReadiness()
    {
        using var session = new Session<int>(graph => graph.Input);
        var context = new InlineContext();
        Assert.Equal(4, session.Result.Value);
        Assert.Throws<NotSupportedException>(() => session.Result.AcquireRecordPlan(session.Pipeline, context));
        Assert.Throws<NotSupportedException>(() => session.Result.AcquireExecution(session.Pipeline, context));
        Assert.Throws<ArgumentException>(() => new GraphicsPipelineExecutionHost(session.Pipeline, context));
        Assert.Equal(4, session.Result.Value);
    }

    /// <summary>No CPU consumer may resume before every mapping and epoch retirement has succeeded.</summary>
    [NativeFact]
    public void ParallelDownloadsPublishOnlyAfterWholeEpochSucceeds()
    {
        var probe = new ProbeWords();
        using var session = new Session<Host<Words>>(graph =>
        {
            Port<Device<Words>> uploaded = graph.Stage(new Producer(), graph.Input).Then(Transfers.Upload<Words>());
            Port<Host<Words>> first = uploaded.Then(Transfers.Download<Words>()).Then(probe);
            Port<Host<Words>> second = uploaded.Then(Transfers.Download<Words>());
            return graph.Stage(new JoinWords(), first, second);
        });
        var context = new InlineContext();
        using GraphicsPipelineExecution execution = session.Result.AcquireExecution(session.Pipeline, context);
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int maps = 0;
        execution.State.Run.BeforeReadbackMap = _ =>
        {
            if (++maps != 2)
            {
                return ValueTask.CompletedTask;
            }

            held.TrySetResult();
            return new ValueTask(release.Task);
        };
        int submits = 0;
        using var host = new GraphicsPipelineExecutionHost(session.Pipeline, context, null, commands =>
        {
            submits++;
            return session.Device.Queue.Submit(commands);
        });
        Task running = host.ExecuteAsync(execution).AsTask();
        Pump(session.Runtime, held.Task);
        for (int index = 0; index < 32; index++)
        {
            session.Runtime.PumpJobs(64, TimeSpan.FromMilliseconds(2));
        }

        int premature = probe.Calls;
        release.TrySetException(new InjectedExecutionException("The second mapping failed after the first copy."));
        Assert.Throws<InjectedExecutionException>(() => Pump(session.Runtime, running));
        Pump(session.Runtime, host.DrainAsync().AsTask());
        Assert.Equal(0, premature);
        Assert.Equal(0, probe.Calls);
        Assert.Equal(1, submits);
        execution.Dispose();
        session.Result.Dispose();
    }

    /// <summary>A retained source validator runs again before each later native recording interval.</summary>
    [NativeFact]
    public void SourceValidationRejectsLaterEpochBeforeSubmission()
    {
        var probe = new ProbeWords();
        using var session = new Session<Host<Words>>(graph => graph.Stage(new Producer(), graph.Input).Then(Transfers.Upload<Words>()).Then(Transfers.Download<Words>()).Then(probe).Then(Transfers.Upload<Words>()).Then(Transfers.Download<Words>()));
        var context = new InlineContext();
        int validations = 0;
        session.Result.AddRecordValidation(() =>
        {
            if (++validations == 2)
            {
                throw new InjectedExecutionException("Source generation expired before the second epoch.");
            }
        });
        int submits = 0;
        using GraphicsPipelineExecution execution = session.Result.AcquireExecution(session.Pipeline, context);
        using var host = new GraphicsPipelineExecutionHost(session.Pipeline, context, null, commands =>
        {
            submits++;
            return session.Device.Queue.Submit(commands);
        });
        Assert.Throws<InjectedExecutionException>(() => Pump(session.Runtime, host.ExecuteAsync(execution).AsTask()));
        Pump(session.Runtime, host.DrainAsync().AsTask());
        Assert.Equal(2, validations);
        Assert.Equal(1, submits);
        Assert.Equal(1, probe.Calls);
        execution.Dispose();
        session.Result.Dispose();
    }

    /// <summary>Execute-only cancellation during mapping or delayed publication never releases CPU consumers.</summary>
    /// <param name="dispatch">Whether cancellation occurs when the publication dispatcher begins.</param>
    [NativeTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExecuteCancellationBeforePublicationStopsCpuConsumers(bool dispatch)
    {
        var probe = new ProbeWords();
        using var session = new Session<Host<Words>>(graph => graph.Stage(new Producer(), graph.Input).Then(Transfers.Upload<Words>()).Then(Transfers.Download<Words>()).Then(probe));
        using var cancel = new CancellationTokenSource();
        IGraphicsOwnerContext context = dispatch ? new CancelPublicationContext(cancel) : new InlineContext();
        using GraphicsPipelineExecution execution = session.Result.AcquireExecution(session.Pipeline, context);
        if (!dispatch)
        {
            execution.State.Run.BeforeReadbackMap = _ =>
            {
                cancel.Cancel();
                return ValueTask.CompletedTask;
            };
        }

        using var host = new GraphicsPipelineExecutionHost(session.Pipeline, context);
        Exception? error = Record.Exception(() => Pump(session.Runtime, host.ExecuteAsync(execution, cancel.Token).AsTask()));
        Pump(session.Runtime, host.DrainAsync().AsTask());
        int calls = probe.Calls;
        execution.Dispose();
        session.Result.Dispose();
        Assert.IsAssignableFrom<OperationCanceledException>(error);
        Assert.Equal(0, calls);
    }

    /// <summary>An acquired but unsubmitted segmented result can abort its waiting downloads without native submission.</summary>
    /// <param name="resultFirst">Whether the caller returns the outer result before the unaccepted execution.</param>
    [NativeTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnsubmittedExecutionDisposalAbortsPendingPreparation(bool resultFirst)
    {
        using var session = new Session<Host<Words>>(graph => graph.Stage(new Producer(), graph.Input).Then(Transfers.Upload<Words>()).Then(Transfers.Download<Words>()));
        GraphicsPipelineExecution execution = session.Result.AcquireExecution(session.Pipeline, new InlineContext());
        GraphicsRun run = execution.State.Run;
        Task preparation = execution.State.Preparation;
        if (resultFirst)
        {
            session.Result.Dispose();
        }

        _ = Record.Exception(execution.Dispose);
        Pump(session.Runtime, Task.WhenAny(preparation, Task.Delay(100)));
        bool aborted = preparation.IsCompleted;
        if (!aborted)
        {
            // Release the deliberately broken before-fix fixture without masking the regression assertion.
            run.AbortContinuations(new OperationCanceledException("Test cleanup after unsubmitted abort failed."));
        }

        _ = Record.Exception(() => Pump(session.Runtime, preparation));
        execution.Dispose();
        session.Result.Dispose();
        Assert.True(aborted, "Unaccepted execution disposal left a download continuation permanently waiting.");
    }

    /// <summary>Returning the sole unclaimed result starts abort and retains cleanup until the runtime drains CPU work.</summary>
    [NativeFact]
    public void UnclaimedSegmentedResultCanBeReturnedAfterAbortPump()
    {
        using var session = new Session<Host<Words>>(graph => graph.Stage(new Producer(), graph.Input).Then(Transfers.Upload<Words>()).Then(Transfers.Download<Words>()));
        var state = (GraphicsResultState)typeof(PipelineResult<Host<Words>>).GetProperty("PlatformState", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session.Result)!;
        _ = Record.Exception(session.Result.Dispose);
        _ = Record.Exception(() => Pump(session.Runtime, state.Preparation));
        Assert.True(state.Preparation.IsCompleted);
        session.Result.Dispose();
        Assert.Throws<ArgumentException>(() => session.Result.AcquireExecution(session.Pipeline, new InlineContext()));
    }

    private sealed class CancelPublicationContext(CancellationTokenSource cancel) : IGraphicsOwnerContext
    {
        private int _calls;

        public ValueTask InvokeAsync(Func<ValueTask> operation)
        {
            if (++_calls == 4)
            {
                cancel.Cancel();
            }

            return operation();
        }
    }

    private sealed class Session<TOutput> : IDisposable
    {
        private readonly GraphicsPipelineBuildHost _build;

        internal Session(Func<PipelineGraph<int>, Port<TOutput>> configure, bool trusted = false)
        {
            Device = VulkanDevice.Create();
            Runtime = new ExecutionRuntime([]);
            Runtime.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.Cooperative, 1, new ExecutionOptions());
            Pump(Runtime, Runtime.StartAsync().AsTask());
            var graph = new PipelineGraph<int>();
            Port<TOutput> output = configure(graph);
            _build = new GraphicsPipelineBuildHost(Runtime, Device, new NoArtifacts(), new GraphicsPipelinePolicy(65536, 256, 256, 4), trusted ? Device.RetirementAuthority : null);
            Pipeline = _build.Build(graph.Complete(output));
            Task<PipelineResult<TOutput>> prepared = Pipeline.PrepareAsync(4).AsTask();
            Pump(Runtime, prepared);
            Result = prepared.GetAwaiter().GetResult();
        }

        internal VulkanDevice Device { get; }

        internal ExecutionRuntime Runtime { get; }

        internal IPipeline<int, TOutput> Pipeline { get; }

        internal PipelineResult<TOutput> Result { get; }

        public void Dispose()
        {
            Result.Dispose();
            Pipeline.Dispose();
            _build.Dispose();
            Pump(Runtime, Runtime.StopAsync().AsTask());
            Device.Dispose();
        }
    }

    private sealed class ProbeWords : IStage<Host<Words>, Host<Words>>, IStageFactory<Host<Words>, Host<Words>>, IStageProcessor<Host<Words>, Host<Words>>
    {
        public IStageFactory<Host<Words>, Host<Words>> Factory => this;

        internal int Calls { get; private set; }

        public StageRequirements Describe(StageInputDescriptions inputs) => Requirements.HostPassthrough<Words>(16);

        public IStageProcessor<Host<Words>, Host<Words>> Create(StageBuildContext context) => this;

        public ValueTask<ValueLease<Host<Words>>> PrepareAsync(StagePreparationContext context, ValueLease<Host<Words>> a, CancellationToken cancellationToken)
        {
            Calls++;
            return ValueTask.FromResult(context.Forward(a));
        }

        public void Dispose()
        {
        }
    }

    private sealed class JoinWords : IStage<Host<Words>, Host<Words>, Host<Words>>, IStageFactory<Host<Words>, Host<Words>, Host<Words>>, IStageProcessor<Host<Words>, Host<Words>, Host<Words>>
    {
        public IStageFactory<Host<Words>, Host<Words>, Host<Words>> Factory => this;

        public StageRequirements Describe(StageInputDescriptions inputs) => Requirements.HostPassthrough<Words>(16);

        public IStageProcessor<Host<Words>, Host<Words>, Host<Words>> Create(StageBuildContext context) => this;

        public ValueTask<ValueLease<Host<Words>>> PrepareAsync(StagePreparationContext context, ValueLease<Host<Words>> a, ValueLease<Host<Words>> b, CancellationToken cancellationToken)
            => ValueTask.FromResult(context.Forward(a));

        public void Dispose()
        {
        }
    }

    private sealed class FailingContext(int failAt) : IGraphicsOwnerContext
    {
        private int _calls;

        public ValueTask InvokeAsync(Func<ValueTask> operation)
        {
            if (++_calls == failAt)
            {
                return ValueTask.FromException(new InjectedExecutionException("Injected owner-context boundary failure."));
            }

            return operation();
        }
    }

    private sealed class AuthorityWrapper(IGraphicsDeviceRetirementAuthority source) : IGraphicsDeviceRetirementAuthority
    {
        public IGraphicDevice Device => source.Device;

        public GraphicsDeviceGeneration Generation => source.Generation;

        public ValueTask<GraphicsDeviceRetirementProof> WaitForRetirementAsync(CancellationToken cancellationToken = default) => source.WaitForRetirementAsync(cancellationToken);
    }

    private sealed class InjectedExecutionException(string message) : InvalidOperationException(message);
}
