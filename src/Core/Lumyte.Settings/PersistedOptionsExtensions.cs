using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Lumyte.Settings;

/// <summary>Registers the shared settings medium and module-owned Options.</summary>
public static class PersistedOptionsExtensions
{
    /// <summary>Registers one borrowed settings source independently of module registration order.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="source">The source registered in configuration.</param>
    /// <returns>The configured services.</returns>
    public static IServiceCollection AddSettings(this IServiceCollection services, PersistedSettingsSource source)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(source);
        ServiceDescriptor? existing = services.FirstOrDefault(item => item.ServiceType == typeof(PersistedSettingsSource));
        if (existing is not null)
        {
            if (!ReferenceEquals(existing.ImplementationInstance, source))
            {
                throw new InvalidOperationException("Only one shared settings source can be registered.");
            }

            return services;
        }

        services.AddSingleton(source);
        services.AddSingleton<SettingsDocument>();
        services.AddSingleton<ISettingsDocument>(provider => provider.GetRequiredService<SettingsDocument>());
        return services;
    }

    /// <summary>Registers a module's settings and automatically enables Host startup validation.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="sectionId">The stable lowercase module ID.</param>
    /// <typeparam name="T">The settings type.</typeparam>
    /// <returns>A standard Options builder.</returns>
    public static OptionsBuilder<T> AddPersistedOptions<T>(this IServiceCollection services, string sectionId)
        where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionId);
        if (sectionId.Any(character => character is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '-')))
        {
            throw new ArgumentException("Use lowercase letters, digits and hyphens for a section ID.", nameof(sectionId));
        }

        Registration? existing = services.Where(item => item.ServiceType == typeof(Registration))
            .Select(item => (Registration)item.ImplementationInstance!)
            .FirstOrDefault(item => item.SectionId == sectionId || item.Type == typeof(T));
        if (existing is not null)
        {
            if (existing.SectionId != sectionId || existing.Type != typeof(T))
            {
                throw new InvalidOperationException("Each settings type and section ID must be unique.");
            }

            return services.AddOptions<T>();
        }

        services.TryAddSingleton<ISettingsDefinition<T>, JsonSettingsDefinition<T>>();
        services.AddSingleton(new Registration(sectionId, typeof(T), provider => provider.GetRequiredService<SettingsState<T>>()));
        services.AddSingleton(provider => new SettingsState<T>(
            sectionId,
            provider.GetRequiredService<SettingsDocument>(),
            provider.GetRequiredService<ISettingsDefinition<T>>(),
            provider.GetServices<IConfigureOptions<T>>(),
            provider.GetServices<IPostConfigureOptions<T>>(),
            provider.GetServices<IValidateOptions<T>>()));
        services.AddSingleton<IEditableOptions<T>>(provider => provider.GetRequiredService<SettingsState<T>>());
        services.AddTransient<IOptionsFactory<T>, PersistedOptionsFactory<T>>();
        return services.AddOptions<T>().ValidateOnStart();
    }

    /// <summary>Registers module-owned JSON metadata, cloning and migration.</summary>
    /// <param name="builder">The default-named Options builder.</param>
    /// <typeparam name="T">The settings type.</typeparam>
    /// <typeparam name="TDefinition">The module's definition.</typeparam>
    /// <returns>The Options builder.</returns>
    public static OptionsBuilder<T> UseJsonDefinition<T, TDefinition>(this OptionsBuilder<T> builder)
        where T : class, new()
        where TDefinition : class, ISettingsDefinition<T>
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (builder.Name != Options.DefaultName)
        {
            throw new InvalidOperationException("Persisted settings only support the default Options name.");
        }

        ServiceDescriptor? existing = builder.Services.FirstOrDefault(item => item.ServiceType == typeof(ISettingsDefinition<T>));
        if (existing is not null && existing.ImplementationType != typeof(TDefinition) && existing.ImplementationType != typeof(JsonSettingsDefinition<T>))
        {
            throw new InvalidOperationException("The settings definition is already registered.");
        }

        if (existing?.ImplementationType == typeof(JsonSettingsDefinition<T>))
        {
            builder.Services.Remove(existing);
        }

        builder.Services.TryAddSingleton<ISettingsDefinition<T>, TDefinition>();
        return builder;
    }

    /// <summary>Registers JSON metadata with automatic copying and optional section migration.</summary>
    /// <param name="builder">The default-named Options builder.</param>
    /// <param name="metadata">The serialization metadata, preferably source-generated for AOT.</param>
    /// <param name="schemaVersion">The current section version, defaulting to one.</param>
    /// <param name="upgrade">An optional migration from an older version to the current version.</param>
    /// <typeparam name="T">The settings model.</typeparam>
    /// <returns>The Options builder.</returns>
    public static OptionsBuilder<T> UseJsonTypeInfo<T>(this OptionsBuilder<T> builder, JsonTypeInfo<T> metadata, int schemaVersion = 1, Func<JsonObject, int, JsonObject>? upgrade = null)
        where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(metadata);
        if (builder.Name != Options.DefaultName)
        {
            throw new InvalidOperationException("Persisted settings only support the default Options name.");
        }

        ServiceDescriptor? existing = builder.Services.FirstOrDefault(item => item.ServiceType == typeof(ISettingsDefinition<T>));
        if (existing?.ImplementationInstance is JsonSettingsDefinition<T> definition && definition.Matches(metadata, schemaVersion, upgrade))
        {
            return builder;
        }

        if (existing is not null && existing.ImplementationType != typeof(JsonSettingsDefinition<T>))
        {
            throw new InvalidOperationException("The settings definition is already registered.");
        }

        var replacement = new JsonSettingsDefinition<T>(metadata, schemaVersion, upgrade);
        if (existing is not null)
        {
            builder.Services.Remove(existing);
        }

        builder.Services.AddSingleton<ISettingsDefinition<T>>(replacement);
        return builder;
    }

    /// <summary>Registers the source at the same loading stage as AddJsonFile.</summary>
    /// <param name="builder">The configuration builder.</param>
    /// <param name="source">The source.</param>
    /// <returns>The builder with the source registered.</returns>
    public static IConfigurationBuilder AddPersistedJsonFile(this IConfigurationBuilder builder, PersistedJsonFileSource source)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(source);
        return builder.Add(source);
    }

    /// <summary>Registers a preloaded source for an asynchronous storage medium.</summary>
    /// <param name="builder">The configuration builder.</param>
    /// <param name="source">The preloaded source.</param>
    /// <returns>The builder with the source registered.</returns>
    public static IConfigurationBuilder AddPersistedSettings(this IConfigurationBuilder builder, PersistedSettingsSource source)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(source);
        return builder.Add(source);
    }

    internal sealed record Registration(string SectionId, Type Type, Func<IServiceProvider, ISettingsSlot> Resolve);

    private sealed class PersistedOptionsFactory<T>(SettingsState<T> state) : IOptionsFactory<T>
        where T : class, new()
    {
        public T Create(string name)
        {
            if (name != Options.DefaultName)
            {
                throw new InvalidOperationException("Persisted settings only support the default Options name.");
            }

            return state.Current.Value;
        }
    }
}
