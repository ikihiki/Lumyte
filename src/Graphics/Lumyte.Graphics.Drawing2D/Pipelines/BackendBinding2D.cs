namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>An explicit borrowed leaf or a projection of one graph-local owned backend.</summary>
/// <typeparam name="T">The original typed command renderer interface.</typeparam>
public sealed class BackendBinding2D<T> : IBackendBinding2D
    where T : class
{
    private readonly Func<BackendCompileScope2D, T> _resolve;
    private readonly Type _concreteType;
    private readonly object? _sharedIdentity;
    private readonly string? _sharedName;
    private readonly IReadOnlyList<Type> _choices;

    private BackendBinding2D(Func<BackendCompileScope2D, T> resolve, Type concreteType, object? sharedIdentity, string? sharedName, IReadOnlyList<Type> choices)
    {
        _resolve = resolve;
        _concreteType = concreteType;
        _sharedIdentity = sharedIdentity;
        _sharedName = sharedName;
        _choices = choices;
    }

    /// <inheritdoc/>
    object? IBackendBinding2D.SharedIdentity => _sharedIdentity;

    /// <inheritdoc/>
    string? IBackendBinding2D.SharedName => _sharedName;

    /// <inheritdoc/>
    IReadOnlyList<Type> IBackendBinding2D.ChoiceBackends => _choices;

    /// <inheritdoc/>
    Type IBackendBinding2D.ConcreteType => _concreteType;

    /// <summary>Binds an existing instance without transferring ownership.</summary>
    /// <param name="backend">The borrowed backend.</param>
    /// <returns>An explicit leaf binding.</returns>
    public static BackendBinding2D<T> Borrowed(T backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        return new(_ => backend, backend.GetType(), null, null, (backend as ICommandChoice2D)?.CandidateTypes ?? Array.Empty<Type>());
    }

    /// <summary>Projects the same actual shared instance into a command-specific interface.</summary>
    /// <typeparam name="TShared">The shared manager type.</typeparam>
    /// <param name="shared">The graph-local identity.</param>
    /// <param name="select">The explicit interface projection, without resource creation.</param>
    /// <returns>The typed binding.</returns>
    public static BackendBinding2D<T> Shared<TShared>(SharedBackend2D<TShared> shared, Func<TShared, T> select)
        where TShared : class, IDisposable
    {
        ArgumentNullException.ThrowIfNull(shared);
        ArgumentNullException.ThrowIfNull(select);
        return new(scope => select(scope.Resolve(shared)) ?? throw new InvalidOperationException("A backend projection returned null."), typeof(TShared), shared, shared.Name, Array.Empty<Type>());
    }

    internal T Resolve(BackendCompileScope2D scope) => _resolve(scope);
}
