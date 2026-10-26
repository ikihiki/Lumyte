namespace Lumyte.Diagnostics.Windowing;

/// <summary>Owns one WindowSystem diagnostic registration.</summary>
public sealed class WindowDiagnosticRegistration : IDisposable
{
    private readonly string _id;
    private readonly object _target;
    private WindowDiagnosticRegistry? _registry;

    internal WindowDiagnosticRegistration(WindowDiagnosticRegistry registry, string id, object target)
    {
        _registry = registry;
        _id = id;
        _target = target;
    }

    /// <summary>Removes this registration. Repeated disposal has no effect.</summary>
    public void Dispose() => Interlocked.Exchange(ref _registry, null)?.Remove(_id, _target);
}
