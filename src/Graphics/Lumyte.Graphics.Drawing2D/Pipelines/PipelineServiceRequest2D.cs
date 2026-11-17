namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>Immutable semantic request implemented by component authors, not consumer DSL bindings.</summary>
/// <typeparam name="TService">The internal service type.</typeparam>
/// <typeparam name="TOptions">Immutable value-equal configuration.</typeparam>
/// <param name="kind">A service-kind discriminator.</param>
/// <param name="options">All compatibility-affecting options.</param>
/// <param name="version">The factory contract version.</param>
public abstract class PipelineServiceRequest2D<TService, TOptions>(string kind, TOptions options, int version)
    where TService : PipelineSharedService2D
    where TOptions : notnull
{
    /// <summary>Gets the semantic service kind.</summary>
    public string Kind { get; } = !string.IsNullOrWhiteSpace(kind) ? kind : throw new ArgumentException("A service kind is required.", nameof(kind));

    /// <summary>Gets the immutable compatibility configuration.</summary>
    public TOptions Options { get; } = options is not null ? options : throw new ArgumentNullException(nameof(options));

    /// <summary>Gets the factory contract version.</summary>
    public int Version { get; } = version;

    internal TService CreateInstance(PipelineBuildContext2D context) => CreateService(context);

    /// <summary>Creates the dependency during the active build callback only.</summary>
    /// <param name="context">Allows nested semantic dependency requests.</param>
    /// <returns>A fresh owned service.</returns>
    protected abstract TService CreateService(PipelineBuildContext2D context);
}
