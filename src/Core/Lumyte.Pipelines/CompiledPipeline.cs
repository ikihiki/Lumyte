using Lumyte.Execution;

namespace Lumyte.Pipelines;

internal sealed class CompiledPipeline<TInput, TOutput> : IPipeline<TInput, TOutput>
{
    private readonly object _sync = new();
    private readonly List<RunOwner> _failedRunCleanup = [];
    private readonly ExecutionRuntime _execution;
    private readonly PipelineDefinition<TInput, TOutput> _definition;
    private readonly Processor?[] _processors;
    private readonly StageRequirements[] _requirements;
    private readonly PipelineBudget _budget;
    private readonly Func<int, object, IStageCapabilities> _capabilities;
    private readonly JobGraph<RunContext>? _jobs;
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
        if (processors.Length != 0)
        {
            Func<RunContext, CancellationToken, ValueTask<Unit>>[] actions = Enumerable.Range(0, processors.Length).Select<int, Func<RunContext, CancellationToken, ValueTask<Unit>>>(index => (run, token) => run.EvaluateAsync(index)).ToArray();
            int[][] dependencies = definition.Nodes.Select(node => node.Inputs.Where(index => index != 0).Select(index => index - 1).ToArray()).ToArray();
            _jobs = PipelineJobBridge.Create(execution, actions, dependencies);
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
            _definition.ValidateInput(0, context.Values[0]);
            InputDescriptions.Actual([context.Values[0]], _budget.MaxHostBytes);
            if (_jobs is not null)
            {
                CommandEnqueueStatus status = _execution.Schedule(_jobs, in context, out JobRun? job, cancellationToken);
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

        internal RunContext(CompiledPipeline<TInput, TOutput> pipeline, TInput input, object runIdentity, CancellationToken token, RunValueOwners owners)
        {
            _pipeline = pipeline;
            _identity = runIdentity;
            _token = token;
            _memory = new RunMemoryBudget(pipeline._budget.MaxHostBytes);
            _owners = owners;
            Values = new object[pipeline._processors.Length + 1];
            Values[0] = new ValueLease<TInput>(input, _identity, _identity);
        }

        internal object[] Values { get; }

        internal async ValueTask<Unit> EvaluateAsync(int index)
        {
            _token.ThrowIfCancellationRequested();
            Node node = _pipeline._definition.Nodes[index];
            object[] values = Values;
            object[] inputs = node.Inputs.Select(input => values[input]).ToArray();
            for (int inputIndex = 0; inputIndex < inputs.Length; inputIndex++)
            {
                _pipeline._definition.ValidateInput(node.Inputs[inputIndex], inputs[inputIndex]);
            }

            StageRequirements compiled = _pipeline._requirements[index];
            StageRequirements actual = node.Factory.Describe(InputDescriptions.Actual(inputs, _pipeline._budget.MaxHostBytes));
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
                Values[index + 1] = await _pipeline._processors[index]!.PrepareAsync(context, inputs, _token);
                _pipeline._definition.ValidateInput(index + 1, Values[index + 1]);
                return default;
            }
            finally
            {
                context.Close();
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
