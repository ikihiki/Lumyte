using Lumyte.Execution;

namespace Lumyte.Pipelines;

/// <summary>Compiles typed declarations against an explicit borrowed CPU runtime.</summary>
public sealed class PipelineBuildHost : IDisposable
{
    private readonly ExecutionRuntime _execution;
    private readonly PipelineBudget _budget;
    private readonly IPipelineBackend? _backend;
    private readonly List<Processor> _failedCleanup = [];
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="PipelineBuildHost"/> class.</summary>
    /// <param name="execution">The borrowed runtime, pumped by its caller.</param>
    /// <param name="budget">The explicit finite admission limits.</param>
    /// <param name="backend">An explicit borrowed extension backend, or null for strict CPU execution.</param>
    public PipelineBuildHost(ExecutionRuntime execution, PipelineBudget budget, IPipelineBackend? backend = null)
    {
        ArgumentNullException.ThrowIfNull(execution);
        ArgumentNullException.ThrowIfNull(budget);
        _execution = execution;
        _budget = budget;
        _backend = backend;
    }

    /// <summary>Builds independent processors after validating all finite requirements.</summary>
    /// <typeparam name="TInput">The arbitrary input type.</typeparam>
    /// <typeparam name="TOutput">The arbitrary output type.</typeparam>
    /// <param name="definition">The immutable declaration.</param>
    /// <returns>The independently owned compiled pipeline.</returns>
    public IPipeline<TInput, TOutput> Build<TInput, TOutput>(PipelineDefinition<TInput, TOutput> definition)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(definition);
        definition.ValidateSchemas();
        if (definition.Nodes.Length > _budget.MaxNodes)
        {
            throw new InvalidOperationException("Pipeline node budget exceeded.");
        }

        var requirements = new StageRequirements[definition.Nodes.Length];
        long bytes = 0;
        for (int i = 0; i < requirements.Length; i++)
        {
            requirements[i] = definition.Nodes[i].Factory.Describe(InputDescriptions.Build<TInput>(definition.Nodes[i], definition.Nodes, requirements, _budget));
            ArgumentNullException.ThrowIfNull(requirements[i]);
            if (_backend is null && requirements[i].Extensions.Count != 0)
            {
                throw new NotSupportedException("The CPU host cannot satisfy extension requirements.");
            }

            _backend?.Validate(i, requirements[i]);
            bytes = checked(bytes + requirements[i].MaxHostBytes);
        }

        if (bytes > _budget.MaxHostBytes)
        {
            long minimum = 0;
            foreach (Node node in definition.Nodes)
            {
                StageRequirements requirement = node.Factory.Describe(InputDescriptions.Minimum(node.Inputs.Length));
                minimum = checked(minimum + requirement.MaxHostBytes);
            }

            if (minimum > _budget.MaxHostBytes)
            {
                throw new InvalidOperationException("Pipeline minimum host requirements exceed the finite budget before processor creation.");
            }
        }

        var processors = new List<Processor>();
        try
        {
            for (int i = 0; i < definition.Nodes.Length; i++)
            {
                var context = new StageBuildContext(_backend is null ? CpuCapabilities.Instance : _backend.BuildCapabilities(i) ?? throw new InvalidOperationException("The backend returned null build capabilities."));
                processors.Add(definition.Nodes[i].Create(context));
            }

            return new CompiledPipeline<TInput, TOutput>(_execution, definition, processors.ToArray(), requirements, _budget, backend: _backend);
        }
        catch (Exception primary)
        {
            var errors = new List<Exception> { primary };
            for (int i = processors.Count - 1; i >= 0; i--)
            {
                try
                {
                    processors[i].Dispose();
                }
                catch (Exception cleanup)
                {
                    _failedCleanup.Add(processors[i]);
                    errors.Add(cleanup);
                }
            }

            if (errors.Count > 1)
            {
                throw new AggregateException(errors);
            }

            throw;
        }
    }

    /// <summary>Stops builds and retries unpublished processor cleanup.</summary>
    public void Dispose()
    {
        _disposed = true;
        var errors = new List<Exception>();
        for (int i = _failedCleanup.Count - 1; i >= 0; i--)
        {
            try
            {
                _failedCleanup[i].Dispose();
                _failedCleanup.RemoveAt(i);
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
}
