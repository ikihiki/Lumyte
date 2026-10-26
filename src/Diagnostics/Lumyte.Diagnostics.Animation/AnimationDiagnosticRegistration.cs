namespace Lumyte.Diagnostics.Animation;

/// <summary>Owns one animation diagnostic registration.</summary>
public sealed class AnimationDiagnosticRegistration : IDisposable
{
    private readonly string _id;
    private readonly object _target;
    private AnimationDiagnosticRegistry? _registry;

    internal AnimationDiagnosticRegistration(AnimationDiagnosticRegistry registry, string id, object target)
    {
        _registry = registry;
        _id = id;
        _target = target;
    }

    /// <summary>Removes this registration. Repeated disposal has no effect.</summary>
    public void Dispose() => Interlocked.Exchange(ref _registry, null)?.Remove(_id, _target);
}
