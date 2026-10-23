using Lumyte.Audio.Execution;
using Lumyte.Execution;
using Lumyte.Memory.DependencyInjection;
using Lumyte.Resources.Audio.DependencyInjection;
using Lumyte.Resources.Execution;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lumyte.Resources.Audio.Tests;

/// <summary>Verifies production registrations share one module set across named managers.</summary>
public sealed class AudioRegistrationTests
{
    /// <summary>Both managers resolve the complete pipeline without duplicate execution modules.</summary>
    /// <returns>The registration and scope disposal check.</returns>
    [Fact]
    public async Task NamedManagersShareTheAudioServiceAsync()
    {
        var services = new ServiceCollection();
        services.AddMemoryManagement();
        services.AddAudioResources<RegistrationSource>("first");
        services.AddAudioResources<RegistrationSource>("second");
        await using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        IExecutionModule[] modules = provider.GetServices<IExecutionModule>().ToArray();
        Assert.Equal(3, modules.Length);
        Assert.Same(provider.GetRequiredService<AudioModule>(), Assert.Single(modules.OfType<AudioModule>()));
        Assert.Same(provider.GetRequiredService<AudioResourceRuntime>(), Assert.Single(modules.OfType<AudioResourceRuntime>()));
        ResourceRuntime runtime = provider.GetRequiredService<ResourceRuntime>();
        Assert.Same(runtime, Assert.Single(modules.OfType<ResourceRuntime>()));
        Assert.Same(runtime, provider.GetRequiredService<IResourceRuntime>());
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        ResourceManager first = scope.ServiceProvider.GetRequiredKeyedService<ResourceManager>("first");
        ResourceManager second = scope.ServiceProvider.GetRequiredKeyedService<ResourceManager>("second");
        Assert.NotSame(first, second);
    }
}
