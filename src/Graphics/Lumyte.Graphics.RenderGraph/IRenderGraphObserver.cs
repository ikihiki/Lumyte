namespace Lumyte.Graphics.RenderGraph;

/// <summary>Receives detached graph diagnostics without owning GPU resources.</summary>
/// <remarks>Implementations must not throw or mutate the graph. Calls occur on the graph owner thread.</remarks>
public interface IRenderGraphObserver
{
    /// <summary>Starts correlated CPU tracing for a compile or record operation.</summary>
    /// <param name="operation">Compile or record.</param>
    /// <returns>A scope closed when the operation completes, or null when tracing is disabled.</returns>
    IDisposable? Begin(string operation);

    /// <summary>Receives the immutable compile snapshot and CPU duration.</summary>
    /// <param name="snapshot">Pass, resource, dependency and barrier descriptions.</param>
    /// <param name="durationMilliseconds">The compile CPU duration.</param>
    void Compiled(RenderGraphSnapshot snapshot, double durationMilliseconds);

    /// <summary>Receives successful command recording, without GPU completion.</summary>
    /// <param name="snapshot">The recorded plan.</param>
    /// <param name="durationMilliseconds">The record CPU duration.</param>
    void Recorded(RenderGraphSnapshot snapshot, double durationMilliseconds);

    /// <summary>Receives a compile or recording failure.</summary>
    /// <param name="operation">Compile or record.</param>
    /// <param name="exception">The original failure.</param>
    void Failed(string operation, Exception exception);
}
