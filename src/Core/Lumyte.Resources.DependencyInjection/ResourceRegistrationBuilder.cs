using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.Resources.DependencyInjection;

/// <summary>Registers immutable pipeline metadata and constructor-injected scoped implementations.</summary>
public sealed class ResourceRegistrationBuilder
{
    private readonly string _managerName;
    private readonly List<Action<IServiceCollection>> _registrations = [];
    private readonly HashSet<RuntimeTypeHandle> _pipelines = [];
    private readonly HashSet<string> _runtimeDependencies = new(StringComparer.Ordinal);
    private readonly Dictionary<Type, Func<IServiceProvider, IResourceRuntime>> _stageRuntimes = [];
    private Func<IServiceProvider, IResourceSource>? _source;
    private ResourceReloadOptions? _reloadOptions;
    private bool _completed;

    internal ResourceRegistrationBuilder(string managerName)
    {
        _managerName = managerName;
    }

    internal ResourceReloadOptions? ReloadOptions => _reloadOptions;

    /// <summary>Registers the one source borrowed by this manager.</summary>
    /// <typeparam name="TSource">The constructor-injected source implementation.</typeparam>
    /// <param name="lifetime">The implementation lifetime; only scoped ownership is supported.</param>
    /// <returns>This builder.</returns>
    public ResourceRegistrationBuilder UseSource<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TSource>(ServiceLifetime lifetime = ServiceLifetime.Scoped)
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

    /// <summary>Configures automatic reload of changes reported by the registered resource source.</summary>
    /// <param name="options">Whether to subscribe, together with batching and retry settings.</param>
    /// <returns>This builder.</returns>
    public ResourceRegistrationBuilder ConfigureHotReload(ResourceReloadOptions options)
    {
        EnsureConfiguring();
        if (_reloadOptions is not null)
        {
            throw new InvalidOperationException("Automatic reload is already configured for this manager.");
        }

        _reloadOptions = options;
        return this;
    }

    /// <summary>Registers a typed starting stage without a fixed primary input.</summary>
    /// <typeparam name="TOutput">The exact completed output type.</typeparam>
    /// <typeparam name="TStage">The constructor-injected stage implementation.</typeparam>
    /// <param name="lifetime">The implementation lifetime; only scoped ownership is supported.</param>
    /// <returns>This builder.</returns>
    public ResourceRegistrationBuilder AddSourcePipeline<TOutput, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TStage>(ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TOutput : class
        where TStage : class, IResourceSourceStage<TOutput>
    {
        EnsureConfiguring();
        ValidateLifetime(lifetime);
        AddPipelineType<TOutput>();
        _registrations.Add(services => services.AddKeyedScoped<IResourceStage, TStage>(_managerName));
        return this;
    }

    /// <summary>Registers a typed transformation whose primary input is tracked as a dependency.</summary>
    /// <typeparam name="TInput">The exact primary input type.</typeparam>
    /// <typeparam name="TOutput">The exact completed output type.</typeparam>
    /// <typeparam name="TStage">The constructor-injected transform implementation.</typeparam>
    /// <param name="lifetime">The implementation lifetime; only scoped ownership is supported.</param>
    /// <returns>This builder.</returns>
    public ResourceRegistrationBuilder AddTransformPipeline<TInput, TOutput, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TStage>(ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TInput : class
        where TOutput : class
        where TStage : class, IResourceTransform<TInput, TOutput>
    {
        EnsureConfiguring();
        ValidateLifetime(lifetime);
        AddPipelineType<TOutput>();
        _registrations.Add(services => services.AddKeyedScoped<IResourceStage, TStage>(_managerName));
        return this;
    }

    /// <summary>Selects an already registered environment for the stage producing the specified output.</summary>
    /// <typeparam name="TOutput">The exact output type of a registered stage.</typeparam>
    /// <typeparam name="TRuntime">The environment service borrowed by the manager.</typeparam>
    /// <returns>This builder.</returns>
    public ResourceRegistrationBuilder UseStageRuntime<TOutput, TRuntime>()
        where TOutput : class
        where TRuntime : class, IResourceRuntime
        => UseStageRuntime<TOutput>(static provider => provider.GetRequiredService<TRuntime>());

    /// <summary>Selects a named or otherwise configured environment once when the manager is constructed.</summary>
    /// <typeparam name="TOutput">The exact output type of a registered stage.</typeparam>
    /// <param name="resolve">Resolves the borrowed environment, whose lifetime must cover manager cleanup.</param>
    /// <returns>This builder.</returns>
    public ResourceRegistrationBuilder UseStageRuntime<TOutput>(Func<IServiceProvider, IResourceRuntime> resolve)
        where TOutput : class
    {
        EnsureConfiguring();
        if (!_stageRuntimes.TryAdd(typeof(TOutput), resolve))
        {
            throw new ArgumentException($"A runtime is already selected for '{typeof(TOutput)}'.");
        }

        return this;
    }

    /// <summary>Registers a constructor-injected scoped runtime dependency.</summary>
    /// <typeparam name="TDependency">The runtime provider implementation.</typeparam>
    /// <param name="dependencyId">The unique manager-local runtime dependency name.</param>
    /// <returns>This builder.</returns>
    public ResourceRegistrationBuilder UseRuntimeDependency<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TDependency>(string dependencyId)
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

        foreach (Type output in _stageRuntimes.Keys)
        {
            if (!_pipelines.Contains(output.TypeHandle))
            {
                throw new ArgumentException($"No stage produces '{output}'.");
            }
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

    internal IEnumerable<ResourceStageRuntime> ResolveStageRuntimes(IServiceProvider provider)
    {
        foreach ((Type output, Func<IServiceProvider, IResourceRuntime> resolve) in _stageRuntimes)
        {
            yield return new(output, resolve(provider));
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

    private void AddPipelineType<TOutput>()
    {
        if (!_pipelines.Add(typeof(TOutput).TypeHandle))
        {
            throw new ArgumentException($"A pipeline for '{typeof(TOutput)}' is already registered.");
        }
    }

    private sealed record RegistrationKey(string ManagerName, string Kind, string Id);
}
