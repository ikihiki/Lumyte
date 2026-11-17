namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>A declaration attached atomically to exactly one renderer definition.</summary>
public abstract class CommandRoute2D
{
    internal object? Parent { get; set; }

    internal abstract DrawKind2D Kind { get; }

    internal abstract IBackendBinding2D Binding { get; }

    internal abstract object Compile(BackendCompileScope2D scope);
}
