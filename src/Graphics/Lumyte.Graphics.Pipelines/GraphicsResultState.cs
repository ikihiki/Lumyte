using Lumyte.Pipelines;

namespace Lumyte.Graphics.Pipelines;

internal sealed class GraphicsResultState(IPipelineIdentity pipeline, GraphicsRun run, IDisposable pipelineUse, Task preparation, bool hasReadback, bool hasGraphicsReadiness) : IDisposable, IPipelineResultOwner
{
    private readonly object _gate = new();
    private int _uses = 1;
    private bool _claimed;
    private bool _accepted;

    internal IPipelineIdentity Pipeline { get; } = pipeline;

    internal GraphicsRun Run { get; } = run;

    internal Task Preparation { get; } = preparation;

    internal bool HasReadback { get; } = hasReadback;

    internal bool HasGraphicsReadiness { get; } = hasGraphicsReadiness;

    public void ValidateReturn()
    {
        lock (_gate)
        {
            if (_uses == 1 && !Preparation.IsCompleted)
            {
                AbortUnaccepted();
                throw new InvalidOperationException("The segmented preparation still owns unfinished CPU continuations.");
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_uses <= 0)
            {
                return;
            }

            if (_uses == 1)
            {
                if (!Preparation.IsCompleted)
                {
                    AbortUnaccepted();
                    throw new InvalidOperationException("The segmented preparation still owns unfinished CPU continuations.");
                }

                Run.Dispose();
                pipelineUse.Dispose();
            }

            _uses--;
        }
    }

    internal void MarkAccepted()
    {
        lock (_gate)
        {
            Run.Validate();
            _accepted = true;
        }
    }

    internal void AbortUnaccepted()
    {
        lock (_gate)
        {
            if (!_accepted && !Preparation.IsCompleted)
            {
                Run.AbortContinuations(new OperationCanceledException("The unsubmitted segmented preparation was abandoned."));
            }
        }
    }

    internal void Retain()
    {
        lock (_gate)
        {
            if (_uses <= 0)
            {
                throw new ObjectDisposedException(nameof(GraphicsResultState));
            }

            _uses++;
        }
    }

    internal GraphicsPipelineExecution AcquireExecution(IPipelineIdentity expected, IGraphicsOwnerContext? context)
    {
        lock (_gate)
        {
            if (_uses <= 0 || _claimed || !ReferenceEquals(Pipeline, expected))
            {
                throw new InvalidOperationException("Result is returned, already claimed, or belongs to another pipeline.");
            }

            if (!HasGraphicsReadiness)
            {
                throw new NotSupportedException("CPU-only results have no graphics execution readiness.");
            }

            Run.Validate();
            var execution = new GraphicsPipelineExecution(this, context);
            _uses++;
            _claimed = true;
            return execution;
        }
    }

    internal GraphicsPipelineRecordPlan Acquire(IPipelineIdentity expected, IGraphicsOwnerContext context)
    {
        lock (_gate)
        {
            if (_uses <= 0 || _claimed || !ReferenceEquals(Pipeline, expected))
            {
                throw new InvalidOperationException("Result is returned, already claimed, or belongs to another pipeline.");
            }

            ArgumentNullException.ThrowIfNull(context);
            if (!HasGraphicsReadiness)
            {
                throw new NotSupportedException("CPU-only results have no graphics record readiness.");
            }

            if (HasReadback)
            {
                throw new NotSupportedException("Readback requires an explicit segmented execution host.");
            }

            Run.Validate();
            var plan = new GraphicsPipelineRecordPlan(this, context);
            _uses++;
            _claimed = true;
            return plan;
        }
    }
}
