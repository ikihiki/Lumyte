using Lumyte.Audio.Abstractions;
using Lumyte.Audio.Execution;
using Lumyte.Execution;
using Lumyte.Resources.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lumyte.Resources.Audio.DependencyInjection;

/// <summary>Connects named audio resource managers to the shared audio owner and CPU executor.</summary>
public static class AudioResourceServiceCollectionExtensions
{
    /// <summary>Registers WAVE decoding, clip/voice pipelines and the same audio runtime provider for a named manager.</summary>
    /// <typeparam name="TSource">The scoped source supplying WAVE files.</typeparam>
    /// <param name="services">The collection, with memory management already registered.</param>
    /// <param name="name">The unique resource manager name.</param>
    /// <returns>The supplied collection.</returns>
    public static IServiceCollection AddAudioResources<TSource>(this IServiceCollection services, string name)
        where TSource : class, IResourceSource
    {
        AddModule<AudioModule>(services);
        AddModule<AudioResourceRuntime>(services);
        services.AddResources(name, builder => builder.UseSource<TSource>().AddAudioPipeline());
        return services.AddResourceRuntimeDependency(name, "Audio", static provider => provider.GetRequiredService<AudioResourceRuntime>());
    }

    /// <summary>Adds the standard WAVE to PCM to clip to voice pipeline to a manager configuration.</summary>
    /// <param name="builder">The configuring resource registration.</param>
    /// <returns>The supplied builder.</returns>
    public static ResourceRegistrationBuilder AddAudioPipeline(this ResourceRegistrationBuilder builder)
        => builder.AddSourcePipeline<AudioPcmData, AudioDecodeStage>()
            .AddTransformPipeline<AudioPcmData, IAudioClip, AudioClipStage>()
            .AddTransformPipeline<IAudioClip, IAudioVoice, AudioVoiceStage>();

    private static void AddModule<TModule>(IServiceCollection services)
        where TModule : class, IExecutionModule
    {
        services.TryAddSingleton<TModule>();
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(IExecutionModule) && descriptor.ImplementationFactory == ModuleRegistration<TModule>.Factory))
        {
            services.Add(ServiceDescriptor.Singleton(typeof(IExecutionModule), ModuleRegistration<TModule>.Factory));
        }
    }

    private static class ModuleRegistration<TModule>
        where TModule : class, IExecutionModule
    {
        internal static readonly Func<IServiceProvider, object> Factory = static provider => provider.GetRequiredService<TModule>();
    }
}
