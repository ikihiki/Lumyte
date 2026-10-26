using System.Runtime.CompilerServices;
using Lumyte.Audio.Execution;
using Lumyte.Execution;
using Lumyte.Memory.DependencyInjection;
using Lumyte.Resources.Audio.DependencyInjection;
using Lumyte.Resources.DependencyInjection;
using Lumyte.Resources.Execution;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lumyte.Resources.Audio.Tests;

/// <summary>Verifies production registrations share one manager and module set.</summary>
public sealed class AudioRegistrationTests
{
    /// <summary>Senders do not add per-voice lease owners or identifiers.</summary>
    [Fact]
    public void AudioSendersContainOneReference()
    {
        Assert.Equal(IntPtr.Size, Unsafe.SizeOf<AudioClip>());
        Assert.Equal(IntPtr.Size, Unsafe.SizeOf<AudioVoice>());
    }

    /// <summary>The environment validates its required owner module before any endpoint connects.</summary>
    [Fact]
    public void MissingAudioModuleIsRejected()
    {
        var runtime = new ResourceRuntime();
        var audioEnvironment = new AudioResourceRuntime(new AudioModule(), runtime);
        Assert.Throws<ArgumentException>(() => new ExecutionRuntime([runtime, audioEnvironment]));
    }

    /// <summary>Audio registration leaves manager construction to the common builder and does not duplicate modules.</summary>
    /// <returns>The registration and scope disposal check.</returns>
    [Fact]
    public async Task CommonManagerUsesTheAudioModulesAsync()
    {
        var services = new ServiceCollection();
        services.AddMemoryManagement();
        services.AddAudioResources();
        services.AddAudioResources();
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(ResourceManager));
        services.AddResources(builder => builder.UseSource<RegistrationSource>().AddAudioPipeline());
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
        ResourceManager manager = provider.GetRequiredService<ResourceManager>();
        Assert.Same(manager, scope.ServiceProvider.GetRequiredService<ResourceManager>());
        Assert.Same(manager, Assert.Single(provider.GetServices<ResourceManager>()));
    }
}
