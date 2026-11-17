namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>Immutable declaration topology resolved only by the runtime compiler.</summary>
internal sealed class CommandRoute2D(DrawKind2D kind, PipelineDeclaration2D declaration)
{
    internal DrawKind2D Kind { get; } = kind;

    internal PipelineDeclaration2D Declaration { get; } = declaration;

    internal object Compile(BackendCompileScope2D scope) => scope.Resolve(Declaration);
}
