using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>Runtime construction environment for lowering passive child declarations.</summary>
public sealed class PipelineBuildContext2D
{
    private readonly BackendCompileScope2D _scope;
    private readonly PipelineOccurrence2D _occurrence;
    private bool _active = true;

    internal PipelineBuildContext2D(IGraphicDevice device, BackendCompileScope2D scope, PipelineOccurrence2D occurrence)
    {
        Device = device;
        _scope = scope;
        _occurrence = occurrence;
    }

    /// <summary>Gets the validated device generation for this build.</summary>
    public IGraphicDevice Device { get; }

    /// <summary>Resolves a unique direct child; repeated references require GetChild(index).</summary>
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
        int[] matches = _occurrence.Children.Select((child, index) => (child, index)).Where(pair => ReferenceEquals(pair.child.Declaration, declaration)).Select(pair => pair.index).ToArray();
        if (matches.Length != 1)
        {
            throw new ArgumentException("Child lookup requires exactly one matching direct child placement.", nameof(declaration));
        }

        return GetChild<TProcessor>(matches[0]);
    }

    /// <summary>Resolves one frozen direct child position, cached only at that position.</summary>
    /// <typeparam name="TProcessor">The required processor capability.</typeparam>
    /// <param name="index">The zero-based child position captured during preflight.</param>
    /// <returns>The child placement's processing instance.</returns>
    public TProcessor GetChild<TProcessor>(int index)
        where TProcessor : class
    {
        if (!_active)
        {
            throw new InvalidOperationException("The construction callback has ended.");
        }

        return _scope.Resolve(_occurrence.Children[index]) as TProcessor ?? throw new InvalidOperationException("The child processor does not implement its declared capability.");
    }

    /// <summary>Gets a compatible build-local internal dependency without sharing processors.</summary>
    /// <typeparam name="TService">The dependency type.</typeparam>
    /// <typeparam name="TOptions">Immutable semantic options.</typeparam>
    /// <param name="request">Component-authored semantic request.</param>
    /// <returns>The build-owned borrowed dependency.</returns>
    public TService GetSharedService<TService, TOptions>(PipelineServiceRequest2D<TService, TOptions> request)
        where TService : PipelineSharedService2D
        where TOptions : notnull
    {
        if (!_active)
        {
            throw new InvalidOperationException("The construction callback has ended.");
        }

        ArgumentNullException.ThrowIfNull(request);
        return _scope.GetService(request, this);
    }

    internal PipelineBuildContext2D CreateCallbackContext() => new(Device, _scope, _occurrence);

    internal void Close() => _active = false;
}
