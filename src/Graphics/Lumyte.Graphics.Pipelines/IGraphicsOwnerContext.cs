namespace Lumyte.Graphics.Pipelines;

/// <summary>Serializes native operations and CPU continuations on the caller-owned context.</summary>
public interface IGraphicsOwnerContext
{
    /// <summary>Runs one operation on this context and observes its completion.</summary>
    /// <param name="operation">The borrowed operation.</param>
    /// <returns>The operation completion.</returns>
    ValueTask InvokeAsync(Func<ValueTask> operation);
}
