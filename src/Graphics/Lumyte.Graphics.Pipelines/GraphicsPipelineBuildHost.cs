using Lumyte.Execution;
using Lumyte.Graphics.Abstractions;
using Lumyte.Pipelines;

namespace Lumyte.Graphics.Pipelines;

/// <summary>Compiles typed declarations with explicit device, completed artifacts and finite policy.</summary>
public sealed class GraphicsPipelineBuildHost : IDisposable
{
    private readonly ExecutionRuntime _execution;
    private readonly IGraphicDevice _device;
    private readonly IStageArtifactProvider _artifacts;
    private readonly GraphicsPipelinePolicy _policy;
    private readonly GraphicsDeviceGeneration _generation;
    private readonly IGraphicsDeviceRetirementAuthority? _retirementAuthority;
    private readonly List<IDisposable> _cleanup = [];
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="GraphicsPipelineBuildHost"/> class.</summary>
    /// <param name="execution">The borrowed CPU runtime.</param>
    /// <param name="device">The borrowed device.</param>
    /// <param name="artifacts">The immutable completed-artifact provider.</param>
    /// <param name="policy">The finite execution policy.</param>
    /// <param name="retirementAuthority">The optional trusted generation authority.</param>
    public GraphicsPipelineBuildHost(ExecutionRuntime execution, IGraphicDevice device, IStageArtifactProvider artifacts, GraphicsPipelinePolicy policy, IGraphicsDeviceRetirementAuthority? retirementAuthority = null)
    {
        ArgumentNullException.ThrowIfNull(execution);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(artifacts);
        ArgumentNullException.ThrowIfNull(policy);
        _execution = execution;
        _device = device;
        _artifacts = artifacts;
        _policy = policy;
        if (retirementAuthority is not null)
        {
            if (!ReferenceEquals(retirementAuthority.Device, device))
            {
                throw new ArgumentException("Retirement authority belongs to another device.", nameof(retirementAuthority));
            }

            retirementAuthority.Generation.ValidateDevice(device);
            retirementAuthority.Generation.ValidateAuthority(retirementAuthority);
        }

        _generation = retirementAuthority?.Generation ?? GraphicsDeviceGeneration.CreateLocal(device);
        _retirementAuthority = retirementAuthority;
    }

    /// <summary>Validates the full inventory before acquiring processors or native artifacts.</summary>
    /// <typeparam name="TInput">The arbitrary input type.</typeparam>
    /// <typeparam name="TOutput">The arbitrary output type.</typeparam>
    /// <param name="definition">The immutable declaration.</param>
    /// <returns>The independent compiled pipeline.</returns>
    public IPipeline<TInput, TOutput> Build<TInput, TOutput>(PipelineDefinition<TInput, TOutput> definition)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(definition);
        definition.ValidateSchemas();
        _generation.ValidateDevice(_device);
        if (_retirementAuthority is not null)
        {
            _generation.ValidateAuthority(_retirementAuthority);
        }

        int maxInFlight = PipelineExecutionLimits.MaxInFlight(_execution);
        var requirements = new StageRequirements[definition.Nodes.Length];
        var graphics = new GraphicsStageRequirements?[requirements.Length];
        var descriptions = new Dictionary<StageArtifactKey, StageArtifactDescription>();
        long hostBytes = 0;
        for (int i = 0; i < requirements.Length; i++)
        {
            Node node = definition.Nodes[i];
            StageRequirements requirement = node.Factory.Describe(InputDescriptions.Build<TInput>(node, definition.Nodes, requirements, _policy.MaxScratchBytes));
            ArgumentNullException.ThrowIfNull(requirement);
            requirements[i] = requirement;
            hostBytes = checked(hostBytes + requirement.MaxHostBytes);
            if (requirement.Extensions.Count > 1 || requirement.Extensions.Any(extension => extension is not GraphicsStageRequirements))
            {
                throw new NotSupportedException("The graphics host cannot satisfy the requested extension.");
            }

            graphics[i] = requirement.Extensions.SingleOrDefault() as GraphicsStageRequirements;
            bool[] deviceInputs = node.Inputs.Select(producer => IsDevice(producer == 0 ? typeof(TInput) : definition.Nodes[producer - 1].OutputType)).ToArray();
            if (graphics[i] is not { } gpu)
            {
                if (deviceInputs.Any(device => device) && (node.Factory is not IDeviceProjectionFactory || !IsDevice(node.OutputType)))
                {
                    throw new ArgumentException("Device inputs cannot be consumed by a CPU stage without an explicit scheduler-owned download.");
                }

                continue;
            }

            if (gpu.Domain is GraphicsExecutionDomain.Compute or GraphicsExecutionDomain.Raster or GraphicsExecutionDomain.Mesh &&
                (!IsDevice(node.OutputType) || deviceInputs.Any(device => !device) || gpu.InputAccesses.Count != deviceInputs.Length))
            {
                throw new ArgumentException("GPU stages require complete device-only typed input/output and access declarations.");
            }

            if (gpu.Domain is GraphicsExecutionDomain.Upload or GraphicsExecutionDomain.Download && node.Factory is not ITransferFactory)
            {
                throw new ArgumentException("Only scheduler-owned explicit transfer factories can declare a transfer domain.");
            }

            if (gpu.InputAccesses.Any(access => access.InputIndex >= node.Inputs.Length))
            {
                throw new ArgumentException("A device access refers to a missing input.");
            }

            if (gpu.Domain == GraphicsExecutionDomain.Compute && (_device.Caps.ShaderTarget == ShaderTarget.Wgsl || _device.Caps.MaxComputeInvocationsPerWorkgroup == 0))
            {
                throw new NotSupportedException("The selected compute topology is unavailable on this device.");
            }

            if (gpu.Domain == GraphicsExecutionDomain.Mesh && _device.Caps.MeshShaderLimits is null)
            {
                throw new NotSupportedException("The selected mesh topology is unavailable on this device.");
            }

            foreach (StageArtifactKey key in gpu.Artifacts)
            {
                if (!descriptions.ContainsKey(key))
                {
                    StageArtifactDescription description = _artifacts.Describe(key, _device);
                    if (description.Key != key || description.BackendTarget != _device.Caps.ShaderTarget.ToString())
                    {
                        throw new ArgumentException("Artifact inventory does not match the selected key and target.");
                    }

                    descriptions.Add(key, description);
                }
            }
        }

        int[] nodeEpochs = new int[requirements.Length];
        int[] outputEpochs = new int[requirements.Length + 1];
        for (int i = 0; i < requirements.Length; i++)
        {
            nodeEpochs[i] = definition.Nodes[i].Inputs.Max(input => outputEpochs[input]);
            outputEpochs[i + 1] = nodeEpochs[i] + (graphics[i]?.Domain == GraphicsExecutionDomain.Download ? 1 : 0);
        }

        int gpuEpochs = Enumerable.Range(0, graphics.Length).Where(index => graphics[index] is not null).Select(index => nodeEpochs[index] + 1).DefaultIfEmpty(0).Max();
        if (gpuEpochs > _policy.MaxEpochs)
        {
            throw new InvalidOperationException("Declared execution epochs exceed the finite policy.");
        }

        if (hostBytes > _policy.MaxScratchBytes)
        {
            long minimum = 0;
            for (int index = 0; index < definition.Nodes.Length; index++)
            {
                Node node = definition.Nodes[index];
                long bytes = graphics[index] is null || node.Factory is ITransferFactory ? node.Factory.Describe(InputDescriptions.Minimum(node.Inputs.Length)).MaxHostBytes : requirements[index].MaxHostBytes;
                minimum = checked(minimum + bytes);
            }

            if (minimum > _policy.MaxScratchBytes)
            {
                throw new InvalidOperationException("Minimum host requirements exceed the finite graphics scratch policy.");
            }
        }

        bool targetEndpoint = false;
        if (definition.Output > 0 && definition.Nodes[definition.Output - 1].HostSchema is { } outputSchema &&
            outputSchema.Metadata.TryGetValue("resourceRole", out object? outputRole) && Equals(outputRole, "target-writes"))
        {
            int outputIndex = definition.Output - 1;
            Node outputNode = definition.Nodes[outputIndex];
            targetEndpoint = graphics[outputIndex]?.InputAccesses.Any(access =>
            {
                int producer = outputNode.Inputs[access.InputIndex];
                SchemaDescriptor? schema = producer > 0 ? definition.Nodes[producer - 1].HostSchema : null;
                return (access.Access & ResourceAccess.ColorWrite) != 0 && schema is not null &&
                    schema.Metadata.TryGetValue("resourceRole", out object? role) && Equals(role, "target");
            }) == true;
            if (!targetEndpoint)
            {
                throw new ArgumentException("A canonical target-write dependency must consume an explicit target write version.");
            }
        }

        var owners = new List<IDisposable>();
        try
        {
            var artifacts = new Dictionary<StageArtifactKey, StageArtifactLease>();
            var artifactContext = new StageArtifactBuildContext(_device, _generation, owner => _cleanup.Add(owner));
            foreach ((StageArtifactKey key, StageArtifactDescription description) in descriptions)
            {
                StageArtifactLease artifact = _artifacts.Acquire(key, artifactContext);
                owners.Add(artifact);
                if (artifact.Description != description || !ReferenceEquals(artifact.Device, _device) || !ReferenceEquals(artifact.Generation, _generation))
                {
                    throw new ArgumentException("Acquired artifact differs from the preflight inventory or generation.");
                }

                artifacts.Add(key, artifact);
            }

            var buildContexts = new GraphicsStageBuildContext[requirements.Length];
            var processors = new Processor[requirements.Length];
            for (int i = 0; i < requirements.Length; i++)
            {
                buildContexts[i] = new GraphicsStageBuildContext(artifacts, graphics[i]?.Artifacts ?? []);
                processors[i] = definition.Nodes[i].Create(new StageBuildContext(new GraphicsStageCapabilities(buildContexts[i], null, i, graphics[i])));
                owners.Add(processors[i]);
            }

            var core = new CompiledPipeline<TInput, TOutput>(_execution, definition, processors, requirements, new PipelineBudget(Math.Max(1, requirements.Length), _policy.MaxScratchBytes, maxInFlight), (index, run) => new GraphicsStageCapabilities(buildContexts[index], (GraphicsRun)run, index, graphics[index]));
            owners.RemoveAll(owner => owner is Processor);
            var pipeline = new GraphicsCompiledPipeline<TInput, TOutput>(core, _device, _generation, _policy, owners.ToArray(), nodeEpochs, graphics.Any(requirement => requirement?.Domain == GraphicsExecutionDomain.Download), targetEndpoint, graphics.Any(requirement => requirement is not null) || default(TOutput) is INonCpuReadablePlacement);
            owners.Clear();
            return pipeline;
        }
        catch (Exception primary)
        {
            var errors = new List<Exception> { primary };
            for (int i = owners.Count - 1; i >= 0; i--)
            {
                try
                {
                    owners[i].Dispose();
                }
                catch (Exception error)
                {
                    _cleanup.Add(owners[i]);
                    errors.Add(error);
                }
            }

            if (errors.Count > 1)
            {
                throw new AggregateException(errors);
            }

            throw;
        }
    }

    /// <summary>Stops new builds and retries only unpublished failed cleanup.</summary>
    public void Dispose()
    {
        _disposed = true;
        var errors = new List<Exception>();
        for (int i = _cleanup.Count - 1; i >= 0; i--)
        {
            try
            {
                _cleanup[i].Dispose();
                _cleanup.RemoveAt(i);
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

    private static bool IsDevice(Type type) => type.IsConstructedGenericType && type.GetGenericTypeDefinition() == typeof(Device<>);
}
