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
    /// <summary>The environment validates its required owner module before any endpoint connects.</summary>
    [Fact]
    public void MissingAudioModuleIsRejected()
    {
        var runtime = new ResourceRuntime();
        var audioEnvironment = new AudioResourceRuntime(new AudioModule(), runtime);
        Assert.Throws<ArgumentException>(() => new ExecutionRuntime([runtime, audioEnvironment]));
    }

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
        AudioResourceRuntime audioEnvironment = provider.GetRequiredService<AudioResourceRuntime>();
        Assert.Same(audioEnvironment, Assert.Single(modules.OfType<AudioResourceRuntime>()));
        ResourceRuntime runtime = provider.GetRequiredService<ResourceRuntime>();
        Assert.Same(runtime, Assert.Single(modules.OfType<ResourceRuntime>()));
        Assert.Same(runtime, provider.GetRequiredService<IResourceRuntime>());
        Assert.Same(runtime.Executor, audioEnvironment.Executor);
        Assert.Same(audioEnvironment, Assert.Single(audioEnvironment.Dependencies));
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        ResourceManager first = scope.ServiceProvider.GetRequiredKeyedService<ResourceManager>("first");
        ResourceManager second = scope.ServiceProvider.GetRequiredKeyedService<ResourceManager>("second");
        Assert.NotSame(first, second);
    }
}
