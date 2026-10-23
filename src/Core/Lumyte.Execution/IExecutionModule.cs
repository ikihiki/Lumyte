namespace Lumyte.Execution;

/// <summary>Declares managed definitions and owns native lifecycle workflows.</summary>
public interface IExecutionModule
{
    /// <summary>Gets the unique module name.</summary>
    string Name { get; }

    /// <summary>Declares work; must not create native devices or threads.</summary>
    /// <param name="builder">The temporary declaration builder.</param>
    void Declare(ModuleBuilder builder);

    /// <summary>Declares physical placement and update driving.</summary>
    /// <param name="builder">The temporary placement builder.</param>
    /// <param name="placement">The fixed-thread placement policy.</param>
    void Configure(ExecutionPlanBuilder builder, OwnerPlacement placement);

    /// <summary>Initializes resources by submitting owner work and awaiting it.</summary>
    /// <param name="context">The temporary startup authority.</param>
    /// <param name="token">The startup cancellation token.</param>
    /// <returns>The initialization completion.</returns>
    ValueTask StartAsync(ModuleContext context, CancellationToken token);

    /// <summary>Releases resources by submitting cleanup work and awaiting it.</summary>
    /// <param name="context">The temporary cleanup authority.</param>
    /// <returns>The cleanup completion.</returns>
    ValueTask StopAsync(ModuleContext context);
}
