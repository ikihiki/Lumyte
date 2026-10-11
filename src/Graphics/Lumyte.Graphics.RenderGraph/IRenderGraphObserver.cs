namespace Lumyte.Graphics.RenderGraph;

/// <summary>Receives detached graph diagnostics without owning GPU resources.</summary>
/// <remarks>Implementations must not throw or mutate the graph. Calls occur on the graph owner thread.</remarks>
public interface IRenderGraphObserver
{
    /// <summary>Gets a value indicating whether timing and notifications are enabled.</summary>
    bool IsEnabled { get; }

    /// <summary>Selects this compilation for detailed capture, if a request is pending.</summary>
    /// <param name="run">The compilation identity.</param>
    /// <returns>Capture limits, or null to collect only fixed-size statistics.</returns>
    RenderGraphCaptureOptions? RequestCapture(RenderGraphRun run);

    /// <summary>Starts correlated CPU tracing for a compile or record operation.</summary>
    /// <param name="run">The compilation identity, also used when recording.</param>
    /// <param name="operation">Compile or record.</param>
    /// <returns>A scope closed when the operation completes, or null when tracing is disabled.</returns>
    IDisposable? Begin(RenderGraphRun run, string operation);

    /// <summary>Receives the immutable compile snapshot and CPU duration.</summary>
    /// <param name="run">The compilation identity.</param>
    /// <param name="statistics">Fixed-size counts.</param>
    /// <param name="snapshot">Requested descriptions, or null when not requested or over the byte limit.</param>
    /// <param name="durationMilliseconds">The compile CPU duration.</param>
    void Compiled(RenderGraphRun run, RenderGraphStatistics statistics, RenderGraphSnapshot? snapshot, double durationMilliseconds);

    /// <summary>Receives successful command recording, without GPU completion.</summary>
    /// <param name="run">The identity of the recorded compilation.</param>
    /// <param name="durationMilliseconds">The record CPU duration.</param>
    void Recorded(RenderGraphRun run, double durationMilliseconds);

    /// <summary>Receives a compile or recording failure.</summary>
    /// <param name="run">The identity of the failing compilation or recording.</param>
    /// <param name="operation">Compile or record.</param>
    /// <param name="exception">The original failure.</param>
    void Failed(RenderGraphRun run, string operation, Exception exception);
}
