using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.Memory.DependencyInjection;

/// <summary>Registers memory management in a dependency injection root.</summary>
public static class MemoryServiceCollectionExtensions
{
    /// <summary>Registers the standard singleton memory manager.</summary>
    /// <param name="services">The target service collection.</param>
    /// <param name="configure">An optional callback configuring pool budgets and CPU chunk sizing.</param>
    /// <returns>The supplied service collection.</returns>
    public static IServiceCollection AddMemoryManagement(this IServiceCollection services, Action<MemoryManagementOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        RejectDuplicate(services);
        var options = new MemoryManagementOptions();
        configure?.Invoke(options);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.ChunkSizeBytes);
        var budgets = new Dictionary<MemoryPoolId, MemoryBudget>(options.Budgets);
        int chunkSizeBytes = options.ChunkSizeBytes;
        services.AddSingleton<IMemoryManager>(_ => new MemoryManager(budgets, chunkSizeBytes));
        return services;
    }

    /// <summary>Registers a custom singleton memory manager.</summary>
    /// <typeparam name="TManager">The manager implementation.</typeparam>
    /// <param name="services">The target service collection.</param>
    /// <returns>The supplied service collection.</returns>
    public static IServiceCollection AddMemoryManagement<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TManager>(this IServiceCollection services)
        where TManager : class, IMemoryManager
    {
        ArgumentNullException.ThrowIfNull(services);
        RejectDuplicate(services);
        services.AddSingleton<IMemoryManager, TManager>();
        return services;
    }

    private static void RejectDuplicate(IServiceCollection services)
    {
        if (services.Any(static descriptor => descriptor.ServiceType == typeof(IMemoryManager)))
        {
            throw new InvalidOperationException("Memory management is already registered.");
        }
    }
}
