using System.Runtime.ExceptionServices;
using Lumyte.Execution;

namespace Lumyte.Pipelines;

internal sealed class CompiledPipeline<TInput, TOutput> : IPipeline<TInput, TOutput>
{
    private static readonly TimeSpan _sliceBudget = TimeSpan.FromMicroseconds(250);
    private readonly object _sync = new();
    private readonly List<RunOwner> _failedRunCleanup = [];
    private readonly ExecutionRuntime _execution;
    private readonly PipelineDefinition<TInput, TOutput> _definition;
    private readonly Processor?[] _processors;
    private readonly StageRequirements[] _requirements;
    private readonly PipelineBudget _budget;
    private readonly StageInputDescriptions?[] _fixedInputs;
    private readonly Func<int, object, IStageCapabilities> _capabilities;
    private readonly JobGraph<RunContext>? _jobs;
    private readonly JobGraph<RunContext>? _cooperativeJobs;
    private int _runs;
    private bool _disposed;

    internal CompiledPipeline(ExecutionRuntime execution, PipelineDefinition<TInput, TOutput> definition, Processor[] processors, StageRequirements[] requirements, PipelineBudget budget, Func<int, object, IStageCapabilities>? capabilities = null)
    {
        _execution = execution;
        _definition = definition;
        _processors = processors;
        _requirements = requirements;
        _budget = budget;
        _capabilities = capabilities ?? ((_, _) => CpuCapabilities.Instance);
        _fixedInputs = new StageInputDescriptions?[processors.Length];
        var descriptions = new StageInputDescriptions?[7];
        for (int i = 0; i < processors.Length; i++)
        {
            Node node = definition.Nodes[i];
            if (node.Inputs.All(input => FixedValueMetadata.IsFixed(input == 0 ? typeof(TInput) : definition.Nodes[input - 1].OutputType)))
            {
                _fixedInputs[i] = descriptions[node.Inputs.Length] ??= InputDescriptions.Fixed(node.Inputs.Length, budget.MaxHostBytes);
            }
        }

        if (processors.Length != 0)
        {
            var segments = new CompiledSegments(definition.Nodes);
            var actions = new Func<RunContext, PipelineJobScope, CancellationToken, ValueTask<Unit>>[segments.Nodes.Length];
            for (int i = 0; i < actions.Length; i++)
            {
                int[] indices = segments.Nodes[i];
                actions[i] = (run, scope, token) => run.EvaluateSegmentAsync(indices, scope);
            }

            // Values plus a conservative finite allowance for per-logical-stage input,
            // producer, context and suspended continuation metadata. Payload leases
            // remain separately charged by the pipeline host-byte budget.
            long retainedBytes = checked(1024L + ((processors.Length + 1L) * IntPtr.Size) + (processors.Length * 1024L));
            _jobs = PipelineJobBridge.CreateCooperative(execution, actions, segments.Dependencies, retainedBytes);
            var program = new CompiledProgram(definition.Nodes);
            Func<RunContext, PipelineJobScope, CancellationToken, ValueTask<Unit>> programAction = (run, scope, token) => new ProgramRun(run, program, scope).ExecuteAsync();
            _cooperativeJobs = PipelineJobBridge.CreateCooperative(execution, [programAction], [[]], retainedBytes);
        }
    }

    public ValueTask<PipelineResult<TOutput>> PrepareAsync(TInput input, CancellationToken cancellationToken = default)
        => PrepareRunAsync(input, new object(), cancellationToken);

    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
            var errors = new List<Exception>();
            for (int i = _failedRunCleanup.Count - 1; i >= 0; i--)
            {
                try
                {
                    _failedRunCleanup[i].Dispose();
                    _failedRunCleanup.RemoveAt(i);
                }
                catch (Exception error)
                {
                    errors.Add(error);
                }
            }

            if (errors.Count != 0)
            {
                throw new AggregateException(errors);
            }

            if (_runs == 0)
            {
                Cleanup();
            }
        }
    }

    internal async ValueTask<PipelineResult<TOutput>> PrepareRunAsync(TInput input, object runIdentity, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            _definition.ValidateSchemas();
            if (_runs >= _budget.MaxInFlight)
            {
                throw new InvalidOperationException("Pipeline retained run capacity exhausted.");
            }

            _runs++;
        }

        var owner = new RunOwner(this);
        try
        {
            var context = new RunContext(this, input, runIdentity, cancellationToken, owner.Values);
            _definition.ValidateInput(0, context.Input);
            InputDescriptions.Actual([context.Input], _budget.MaxHostBytes);
            if (_jobs is not null)
            {
                JobGraph<RunContext> jobs = PipelineJobBridge.IsCooperative(_execution) ? _cooperativeJobs! : _jobs;
                CommandEnqueueStatus status = _execution.Schedule(jobs, in context, out JobRun? job, cancellationToken);
                if (status != CommandEnqueueStatus.Accepted)
                {
                    throw new InvalidOperationException($"CPU pipeline admission rejected: {status}.");
                }

                using (job!)
                {
                    await job!.Completion.ConfigureAwait(false);
                    job.ThrowIfFailed();
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            var output = (ValueLease<TOutput>)context.Values[_definition.Output];
            return new PipelineResult<TOutput>(output.Value, owner);
        }
        catch (Exception primary)
        {
            try
            {
                owner.Dispose();
            }
            catch (Exception cleanup)
            {
                lock (_sync)
                {
                    _failedRunCleanup.Add(owner);
                }

                throw new AggregateException(primary, cleanup);
            }

            throw;
        }
    }

    private void Cleanup()
    {
        var errors = new List<Exception>();
        for (int i = _processors.Length - 1; i >= 0; i--)
        {
            try
            {
                _processors[i]?.Dispose();
                _processors[i] = null;
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
        }

        if (errors.Count != 0)
        {
            throw new AggregateException(errors);
        }
    }

    private readonly struct RunContext
    {
        private readonly CompiledPipeline<TInput, TOutput> _pipeline;
        private readonly object _identity;
        private readonly CancellationToken _token;
        private readonly RunMemoryBudget _memory;
        private readonly RunValueOwners _owners;
        private readonly RunValues _values;

        internal RunContext(CompiledPipeline<TInput, TOutput> pipeline, TInput input, object runIdentity, CancellationToken token, RunValueOwners owners)
        {
            _pipeline = pipeline;
            _identity = runIdentity;
            _token = token;
            _memory = new RunMemoryBudget(pipeline._budget.MaxHostBytes);
            _owners = owners;
            _values = new RunValues(new ValueLease<TInput>(input, _identity, _identity), pipeline._processors.Length + 1);
        }

        internal object Input => _values.Input;

        internal object[] Values => _values.GetValues();

        internal async ValueTask<Unit> EvaluateSegmentAsync(int[] indices, PipelineJobScope scope)
        {
            foreach (int index in indices)
            {
                if (!scope.TryStartLogicalStage())
                {
                    break;
                }

                ValueTask<Unit> operation = EvaluateAsync(index);
                if (!operation.IsCompletedSuccessfully)
                {
                    await operation;
                }
                else
                {
                    operation.GetAwaiter().GetResult();
                }

                if (ExecutionRuntime.ShouldYield(_sliceBudget))
                {
                    await Task.Yield();
                }
            }

            return default;
        }

        internal ValueTask<Unit> EvaluateAsync(int index)
        {
            _token.ThrowIfCancellationRequested();
            Node node = _pipeline._definition.Nodes[index];
            object[] values = Values;
            object[] inputs = new object[node.Inputs.Length];
            for (int i = 0; i < inputs.Length; i++)
            {
                inputs[i] = values[node.Inputs[i]];
            }

            for (int inputIndex = 0; inputIndex < inputs.Length; inputIndex++)
            {
                _pipeline._definition.ValidateInput(node.Inputs[inputIndex], inputs[inputIndex]);
            }

            StageRequirements compiled = _pipeline._requirements[index];
            StageRequirements actual = node.Factory.Describe(_pipeline._fixedInputs[index] ?? InputDescriptions.Actual(inputs, _pipeline._budget.MaxHostBytes));
            if (actual.MaxHostBytes > compiled.MaxHostBytes)
            {
                throw new InvalidOperationException("Actual input requirements exceed the compiled finite host reservation.");
            }

            _memory.Reserve(actual.MaxHostBytes);
            InputDescriptions.ValidateHostInputs(compiled, inputs);
            InputDescriptions.ValidateHostInputs(actual, inputs);
            IStageCapabilities capabilities = _pipeline._capabilities(index, _identity);
            if (capabilities is IStageRequirementValidation validation)
            {
                validation.Validate(actual);
            }
            else if (actual.Extensions.Count != 0)
            {
                throw new InvalidOperationException("Actual requirements introduce an uncompiled extension.");
            }

            var context = new StagePreparationContext(capabilities, _identity, new object(), inputs, actual.MaxHostBytes, _owners);
            try
            {
                ValueTask<object> operation = _pipeline._processors[index]!.PrepareAsync(context, inputs, _token);
                if (!operation.IsCompletedSuccessfully)
                {
                    return AwaitOutputAsync(operation, context, index);
                }

                values[index + 1] = operation.Result;
                _pipeline._definition.ValidateInput(index + 1, values[index + 1]);
            }
            catch
            {
                context.Close();
                throw;
            }

            context.Close();
            return default;
        }

        private async ValueTask<Unit> AwaitOutputAsync(ValueTask<object> operation, StagePreparationContext context, int index)
        {
            try
            {
                Values[index + 1] = await operation;
                _pipeline._definition.ValidateInput(index + 1, Values[index + 1]);
                return default;
            }
            finally
            {
                context.Close();
            }
        }
    }

    private sealed class RunValues(object input, int length)
    {
        private readonly object _sync = new();
        private object[]? _values;

        internal object Input { get; } = input;

        internal object[] GetValues()
        {
            object[]? values = Volatile.Read(ref _values);
            if (values is not null)
            {
                return values;
            }

            // All graph-sized run state is allocated only by an admitted action.
            // Independent ThreadPool segments share this one initialization.
            lock (_sync)
            {
                values = _values;
                if (values is null)
                {
                    values = new object[length];
                    values[0] = Input;
                    Volatile.Write(ref _values, values);
                }

                return values;
            }
        }
    }

    // This is a pipeline program interpreter, not a dispatch backend. Its single
    // workflow, continuations, yields and terminal drain remain runtime-owned.
    private sealed class ProgramRun
    {
        private readonly RunContext _run;
        private readonly CompiledProgram _program;
        private readonly PipelineJobScope _scope;
        private readonly SynchronizationContext _workflow;
        private readonly int[] _incoming;
        private readonly int[] _ready;
        private int[]? _completed;
        private ValueTask<Unit>[]? _operations;
        private TaskCompletionSource? _wake;
        private ExceptionDispatchInfo? _failure;
        private int _readyRead;
        private int _readyWrite;
        private int _completedRead;
        private int _completedWrite;
        private int _pending;
        private int _finished;
        private bool _suppressed;

        internal ProgramRun(RunContext run, CompiledProgram program, PipelineJobScope scope)
        {
            _run = run;
            _program = program;
            _scope = scope;
            _workflow = SynchronizationContext.Current ?? throw new InvalidOperationException("A cooperative pipeline requires the existing runtime workflow context.");
            _incoming = (int[])program.Incoming.Clone();
            _ready = new int[_incoming.Length];
            for (int i = 0; i < _incoming.Length; i++)
            {
                if (_incoming[i] == 0)
                {
                    _ready[_readyWrite++] = i;
                }
            }
        }

        internal async ValueTask<Unit> ExecuteAsync()
        {
            while (true)
            {
                while (_completedRead < _completedWrite)
                {
                    int index = _completed![_completedRead++];
                    ValueTask<Unit> operation = _operations![index];
                    _operations![index] = default;
                    _pending--;
                    Observe(index, operation);
                    if (ExecutionRuntime.ShouldYield(_sliceBudget))
                    {
                        await Task.Yield();
                    }
                }

                if (!_suppressed && _readyRead < _readyWrite)
                {
                    if (!_scope.TryStartLogicalStage())
                    {
                        _suppressed = true;
                        continue;
                    }

                    int index = _ready[_readyRead++];
                    try
                    {
                        ValueTask<Unit> operation = _run.EvaluateAsync(index);
                        if (operation.IsCompleted)
                        {
                            Observe(index, operation);
                        }
                        else
                        {
                            _operations ??= new ValueTask<Unit>[_incoming.Length];
                            _completed ??= new int[_incoming.Length];
                            _operations[index] = operation;
                            _pending++;
                            var completion = new PendingStage(this, index);
                            operation.ConfigureAwait(false).GetAwaiter().UnsafeOnCompleted(completion.Post);
                        }
                    }
                    catch (Exception error)
                    {
                        Fail(error);
                    }

                    if (ExecutionRuntime.ShouldYield(_sliceBudget))
                    {
                        await Task.Yield();
                    }

                    continue;
                }

                if (_pending == 0)
                {
                    break;
                }

                // All completion notifications are serialized by the captured
                // workflow context. No notification can interleave between the
                // empty check and publishing this waiter, so wakeups cannot be lost.
                _wake = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                await _wake.Task;
                _wake = null;
            }

            _failure?.Throw();
            if (!_suppressed && _finished != _incoming.Length)
            {
                throw new InvalidOperationException("The compiled pipeline did not reach a terminal state.");
            }

            return default;
        }

        private void Observe(int index, ValueTask<Unit> operation)
        {
            try
            {
                operation.GetAwaiter().GetResult();
                _finished++;
                if (!_suppressed)
                {
                    foreach (int successor in _program.Outgoing[index])
                    {
                        if (--_incoming[successor] == 0)
                        {
                            _ready[_readyWrite++] = successor;
                        }
                    }
                }
            }
            catch (Exception error)
            {
                Fail(error);
            }
        }

        private void Fail(Exception error)
        {
            _suppressed = true;

            // A handler OCE is a fault, just as in JobRun.Record. Actual graph
            // cancellation is recorded separately by the logical-stage checkpoint.
            _failure ??= ExceptionDispatchInfo.Capture(error);
        }

        private sealed class PendingStage(ProgramRun program, int index)
        {
            internal void Post() => program._workflow.Post(static state => ((PendingStage)state!).Complete(), this);

            private void Complete()
            {
                program._completed![program._completedWrite++] = index;
                program._wake?.TrySetResult();
            }
        }
    }

    private sealed class RunOwner(CompiledPipeline<TInput, TOutput> pipeline) : IDisposable
    {
        private bool _returned;

        internal RunValueOwners Values { get; } = new();

        public void Dispose()
        {
            lock (pipeline._sync)
            {
                if (_returned)
                {
                    return;
                }

                Values.Dispose();
                if (pipeline._disposed && pipeline._runs == 1)
                {
                    pipeline.Cleanup();
                }

                pipeline._runs--;
                _returned = true;
            }
        }
    }
}
