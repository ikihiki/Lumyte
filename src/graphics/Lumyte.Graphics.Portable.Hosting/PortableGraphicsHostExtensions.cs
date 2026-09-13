using Lumyte.Graphics.Hosting;
using Lumyte.Graphics.Portable.RenderGraph;
using Lumyte.Graphics.RenderGraph;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.Graphics.Portable.Hosting;

public delegate ValueTask<IPortableGpuBackend> PortableGraphicsBackendFactory(
    IServiceProvider services, GpuRenderRuntimeOptions options, CancellationToken cancellationToken);

public static class PortableGraphicsHostExtensions
{
    public static LumyteGraphicsBuilder AddPortableProvider(
        this LumyteGraphicsBuilder builder,
        string id,
        PortableGraphicsBackendFactory createBackend,
        Action<PortableRenderPassRegistry>? configurePasses = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(createBackend);
        return builder.AddProvider(new ProviderDefinition(id, createBackend, configurePasses));
    }

    public static LumyteGraphicsBuilder AddPortablePasses(
        this LumyteGraphicsBuilder builder,
        Action<IServiceProvider, PortableRenderPassRegistry> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        return AddRegistration(builder, new ConfigureRegistration(configure));
    }

    /// <summary>Register asynchronous CPU/package preparation, once within the selected runtime's DI scope.</summary>
    public static LumyteGraphicsBuilder AddPortablePass<TRequest, TResult>(
        this LumyteGraphicsBuilder builder,
        IGpuRenderPassContract<TRequest, TResult> contract,
        Func<IServiceProvider, CancellationToken, ValueTask<PortableRenderPassFactory<TRequest, TResult>>> prepareFactory)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(prepareFactory);
        ArgumentException.ThrowIfNullOrWhiteSpace(contract.Id);
        return AddRegistration(builder, new PreparedRegistration<TRequest, TResult>(contract, prepareFactory));
    }

    private static LumyteGraphicsBuilder AddRegistration(LumyteGraphicsBuilder builder, IPassRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(builder);
        foreach (var descriptor in builder.Services.Where(item => item.ServiceType == typeof(IPassRegistration)))
        {
            if (descriptor.ImplementationInstance is not IPassRegistration existing) { continue; }
            if (Equals(existing, registration)) { return builder; }
            if (registration.Id is not null && existing.Id == registration.Id)
            { throw new ArgumentException($"Pass '{registration.Id}' already has a different Portable preparation definition.", nameof(registration)); }
        }
        builder.Services.AddSingleton(registration);
        return builder;
    }

    private interface IPassRegistration
    {
        GpuRenderPassId? Id { get; }
        ValueTask ConfigureAsync(IServiceProvider services, PortableRenderPassRegistry registry, CancellationToken cancellationToken);
    }

    private sealed record ConfigureRegistration(Action<IServiceProvider, PortableRenderPassRegistry> Configure) : IPassRegistration
    {
        public GpuRenderPassId? Id => null;
        public ValueTask ConfigureAsync(IServiceProvider services, PortableRenderPassRegistry registry, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Configure(services, registry);
            return ValueTask.CompletedTask;
        }
    }

    private sealed record PreparedRegistration<TRequest, TResult>(
        IGpuRenderPassContract<TRequest, TResult> Contract,
        Func<IServiceProvider, CancellationToken, ValueTask<PortableRenderPassFactory<TRequest, TResult>>> Prepare) : IPassRegistration
    {
        public GpuRenderPassId? Id { get; } = new GpuRenderPassId(Contract.Id, Contract.Version);
        public async ValueTask ConfigureAsync(IServiceProvider services, PortableRenderPassRegistry registry, CancellationToken cancellationToken)
        {
            if (Id != new GpuRenderPassId(Contract.Id, Contract.Version))
            { throw new InvalidOperationException("The pass contract identity changed after startup registration."); }
            cancellationToken.ThrowIfCancellationRequested();
            PortableRenderPassFactory<TRequest, TResult> factory = await Prepare(services, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The pass preparation callback returned no factory.");
            cancellationToken.ThrowIfCancellationRequested();
            registry.Register(Contract, factory);
        }
    }

    private sealed record ProviderDefinition(
        string Id, PortableGraphicsBackendFactory CreateBackend,
        Action<PortableRenderPassRegistry>? ConfigurePasses) : IGpuRenderProviderDefinition
    {
        public async ValueTask<IGpuRenderProvider> CreateAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var passes = new PortableRenderPassRegistry();
            ConfigurePasses?.Invoke(passes);
            foreach (IPassRegistration registration in services.GetServices<IPassRegistration>())
            {
                await registration.ConfigureAsync(services, passes, cancellationToken).ConfigureAwait(false);
            }
            return new PortableRenderProvider(Id, (options, token) => CreateBackend(services, options, token), passes);
        }
    }
}
