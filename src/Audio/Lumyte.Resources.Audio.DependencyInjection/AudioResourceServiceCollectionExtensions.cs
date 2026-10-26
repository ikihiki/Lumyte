using System.Diagnostics.CodeAnalysis;
using Lumyte.Resources.DependencyInjection;

namespace Lumyte.Resources.Audio.DependencyInjection;

/// <summary>Registers the audio environment and its clip pipeline for the application resource manager.</summary>
public static class AudioResourceServiceCollectionExtensions
{
    /// <summary>Registers the shared audio owner and resource environment modules.</summary>
    /// <param name="services">The collection configured separately with the common resource manager.</param>
    /// <returns>The supplied collection.</returns>
    public static IServiceCollection AddAudioResources(this IServiceCollection services)
    {
        AddModule<AudioModule>(services);
        AddModule<AudioResourceRuntime>(services);
        return services;
    }

    /// <summary>Adds the standard WAVE to PCM to audio clip pipeline to the manager configuration.</summary>
    /// <param name="builder">The configuring resource registration.</param>
    /// <returns>The supplied builder.</returns>
    public static ResourceRegistrationBuilder AddAudioPipeline(this ResourceRegistrationBuilder builder)
        => builder.AddSourcePipeline<AudioPcmData, AudioDecodeStage>()
            .AddTransformPipeline<AudioPcmData, AudioClip, AudioClipStage>()
            .UseStageRuntime<AudioClip, AudioResourceRuntime>();

    private static void AddModule<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TModule>(IServiceCollection services)
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
