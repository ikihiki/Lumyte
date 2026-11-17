namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>Host-owned immutable snapshot of one typed named slot.</summary>
internal sealed class CommandRoute2D(DrawKind2D kind, IBackendBinding2D binding, Func<BackendCompileScope2D, object> compile)
{
    internal DrawKind2D Kind { get; } = kind;

    internal IBackendBinding2D Binding { get; } = binding;

    internal object Compile(BackendCompileScope2D scope) => compile(scope);
}
