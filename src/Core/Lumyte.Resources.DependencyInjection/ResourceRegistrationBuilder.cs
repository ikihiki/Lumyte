using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.Resources.DependencyInjection;

/// <summary>Registers immutable pipeline metadata and constructor-injected scoped implementations.</summary>
public sealed class ResourceRegistrationBuilder
{
    private readonly string _managerName;
    private readonly List<Action<IServiceCollection>> _registrations = [];
    private readonly HashSet<string> _pipelines = new(StringComparer.Ordinal);
    private readonly HashSet<string> _runtimeDependencies = new(StringComparer.Ordinal);
    private Func<IServiceProvider, IResourceSource>? _source;
    private bool _watchChanges;
    private bool _completed;

    internal ResourceRegistrationBuilder(string managerName)
    {
        _managerName = managerName;
    }

    /// <summary>Registers the one source borrowed by this manager.</summary>
    /// <typeparam name="TSource">The constructor-injected source implementation.</typeparam>
    /// <param name="lifetime">The implementation lifetime; only scoped ownership is supported.</param>
    /// <returns>This builder.</returns>
    public ResourceRegistrationBuilder UseSource<TSource>(ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TSource : class, IResourceSource
    {
        EnsureConfiguring();
        ValidateLifetime(lifetime);
        if (_source is not null)
        {
            throw new InvalidOperationException("A resource source is already registered for this manager.");
        }

        var key = new RegistrationKey(_managerName, "source", string.Empty);
        _registrations.Add(services => services.AddKeyedScoped<TSource>(key));
        _source = provider => provider.GetRequiredKeyedService<TSource>(key);
        return this;
    }

    /// <summary>Registers a scoped automatic reload service and its constructor-injected change source.</summary>
    /// <typeparam name="TChangeSource">The source of asynchronous change notifications.</typeparam>
    /// <param name="options">Immutable batching and retry settings, or null for defaults.</param>
    /// <returns>This builder.</returns>
    /// <remarks>Resolve the named <see cref="ResourceReloadService"/> and call its Start method to begin monitoring. Scope disposal stops monitoring before disposing the manager.</remarks>
    public ResourceRegistrationBuilder WatchChanges<TChangeSource>(ResourceReloadOptions? options = null)
        where TChangeSource : class, IResourceChangeSource
    {
        EnsureConfiguring();
        if (_watchChanges)
        {
            throw new InvalidOperationException("A resource change source is already registered for this manager.");
        }

        _watchChanges = true;
        var key = new RegistrationKey(_managerName, "changes", string.Empty);
        _registrations.Add(services =>
        {
            services.AddKeyedScoped<TChangeSource>(key);
            services.AddKeyedScoped<ResourceReloadService>(_managerName, (provider, _) => new ResourceReloadService(
                provider.GetRequiredKeyedService<ResourceManager>(_managerName),
                provider.GetRequiredKeyedService<TChangeSource>(key),
                options));
        });
        return this;
    }

    /// <summary>Registers a typed starting stage without a fixed primary input.</summary>
    /// <typeparam name="TOutput">The exact completed output type.</typeparam>
    /// <typeparam name="TStage">The constructor-injected stage implementation.</typeparam>
    /// <param name="pipelineId">The unique manager-local pipeline name.</param>
    /// <param name="lifetime">The implementation lifetime; only scoped ownership is supported.</param>
    /// <returns>This builder.</returns>
    public ResourceRegistrationBuilder AddSourcePipeline<TOutput, TStage>(string pipelineId, ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TOutput : class
        where TStage : class, IResourceSourceStage<TOutput>
    {
        EnsureConfiguring();
        ValidateLifetime(lifetime);
        AddPipelineId(pipelineId);
        var key = new RegistrationKey(_managerName, "stage", pipelineId);
        _registrations.Add(services =>
        {
            services.AddKeyedScoped<TStage>(key);
            services.AddKeyedScoped<ResourcePipeline>(_managerName, (provider, _) =>
                new SourcePipeline<TOutput>(pipelineId, provider.GetRequiredKeyedService<TStage>(key)));
        });
        return this;
    }

    /// <summary>Registers a typed transformation whose primary input is tracked as a dependency.</summary>
    /// <typeparam name="TInput">The exact primary input type.</typeparam>
    /// <typeparam name="TOutput">The exact completed output type.</typeparam>
    /// <typeparam name="TStage">The constructor-injected transform implementation.</typeparam>
    /// <param name="pipelineId">The unique manager-local output pipeline name.</param>
    /// <param name="inputPipelineId">The registered primary input pipeline name.</param>
    /// <param name="lifetime">The implementation lifetime; only scoped ownership is supported.</param>
    /// <returns>This builder.</returns>
    public ResourceRegistrationBuilder AddTransformPipeline<TInput, TOutput, TStage>(string pipelineId, string inputPipelineId, ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TInput : class
        where TOutput : class
        where TStage : class, IResourceTransform<TInput, TOutput>
    {
        EnsureConfiguring();
        ValidateLifetime(lifetime);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPipelineId);
        AddPipelineId(pipelineId);
        var key = new RegistrationKey(_managerName, "stage", pipelineId);
        _registrations.Add(services =>
        {
            services.AddKeyedScoped<TStage>(key);
            services.AddKeyedScoped<ResourcePipeline>(_managerName, (provider, _) =>
                new TransformPipeline<TInput, TOutput>(pipelineId, inputPipelineId, provider.GetRequiredKeyedService<TStage>(key)));
        });
        return this;
    }

    /// <summary>Registers a constructor-injected scoped runtime dependency.</summary>
    /// <typeparam name="TDependency">The runtime provider implementation.</typeparam>
    /// <param name="dependencyId">The unique manager-local runtime dependency name.</param>
    /// <returns>This builder.</returns>
    public ResourceRegistrationBuilder UseRuntimeDependency<TDependency>(string dependencyId)
        where TDependency : class, IResourceRuntimeDependency
    {
        EnsureConfiguring();
        ArgumentException.ThrowIfNullOrWhiteSpace(dependencyId);
        if (!_runtimeDependencies.Add(dependencyId))
        {
            throw new ArgumentException("The runtime dependency name is already registered.", nameof(dependencyId));
        }

        _registrations.Add(services => ResourceServiceCollectionExtensions.AddRuntimeDependency<TDependency>(services, _managerName, dependencyId));
        return this;
    }

    internal Func<IServiceProvider, IResourceSource> Complete()
    {
        EnsureConfiguring();
        if (_source is null)
        {
            throw new InvalidOperationException("Register a resource source before completing the manager configuration.");
        }

        if (_pipelines.Count == 0)
        {
            throw new InvalidOperationException("Register at least one resource pipeline.");
        }

        _completed = true;
        return _source;
    }

    internal void Apply(IServiceCollection services)
    {
        foreach (Action<IServiceCollection> registration in _registrations)
        {
            registration(services);
        }
    }

    private static void ValidateLifetime(ServiceLifetime lifetime)
    {
        if (lifetime != ServiceLifetime.Scoped)
        {
            throw new NotSupportedException("Resource sources and stages require scoped registration. Singleton and transient registrations cannot guarantee the manager's borrowed-service lifetime.");
        }
    }

    private void EnsureConfiguring()
    {
        if (_completed)
        {
            throw new InvalidOperationException("Resource registration is immutable after the configuration callback completes.");
        }
    }

    private void AddPipelineId(string pipelineId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipelineId);
        if (!_pipelines.Add(pipelineId))
        {
            throw new ArgumentException("The pipeline name is already registered.", nameof(pipelineId));
        }
    }

    private sealed record RegistrationKey(string ManagerName, string Kind, string Id);

    private sealed class SourcePipeline<TOutput>(string id, IResourceSourceStage<TOutput> stage)
        : ResourcePipeline(id, typeof(TOutput))
        where TOutput : class
    {
        public override async ValueTask<object> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
            => await stage.BuildAsync(context, cancellationToken).ConfigureAwait(false);

        public override ValueTask ReleaseAsync(object resource) => stage.ReleaseAsync((TOutput)resource);
    }

    private sealed class TransformPipeline<TInput, TOutput>(string id, string inputId, IResourceTransform<TInput, TOutput> stage)
        : ResourcePipeline(id, typeof(TOutput), inputId, typeof(TInput))
        where TInput : class
        where TOutput : class
    {
        public override async ValueTask<object> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
        {
            var key = new ResourceKey<TInput>(stage.GetInput(context.Address), InputPipelineId!);
            TInput input = await context.RequireAsync(key, stage.InputLifetime, cancellationToken).ConfigureAwait(false);
            return await stage.BuildAsync(input, context, cancellationToken).ConfigureAwait(false);
        }

        public override ValueTask ReleaseAsync(object resource) => stage.ReleaseAsync((TOutput)resource);
    }
}
