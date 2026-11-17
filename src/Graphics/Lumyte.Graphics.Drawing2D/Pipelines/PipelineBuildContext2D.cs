using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>Runtime construction environment for lowering passive child declarations.</summary>
public sealed class PipelineBuildContext2D
{
    private readonly BackendCompileScope2D _scope;
    private bool _active = true;

    internal PipelineBuildContext2D(IGraphicDevice device, BackendCompileScope2D scope)
    {
        Device = device;
        _scope = scope;
    }

    /// <summary>Gets the validated device generation for this build.</summary>
    public IGraphicDevice Device { get; }

    /// <summary>Resolves one graph-local child, sharing only the same declaration reference.</summary>
    /// <typeparam name="TProcessor">The required processing interface.</typeparam>
    /// <param name="declaration">The passive child identity.</param>
    /// <returns>The actual child processor, not a drawing wrapper.</returns>
    public TProcessor GetProcessor<TProcessor>(PipelineDeclaration2D declaration)
        where TProcessor : class
    {
        if (!_active)
        {
            throw new InvalidOperationException("The construction callback has ended.");
        }

        ArgumentNullException.ThrowIfNull(declaration);
        return _scope.Resolve(declaration) as TProcessor ?? throw new InvalidOperationException("The child processor does not implement its declared capability.");
    }

    internal void Close() => _active = false;
}
