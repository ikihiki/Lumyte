namespace Lumyte.UI.Automation;

/// <summary>Optional support for PNG readback from a newly rendered and completed GPU frame.</summary>
public interface IUiAutomationScreenshotHost
{
    /// <summary>Captures actual pixels; cancellation retains GPU resources until completion or retirement.</summary>
    /// <param name="cancellationToken">Cancels the request, not GPU resource lifetime.</param>
    /// <returns>PNG bytes owned by the caller.</returns>
    ValueTask<byte[]> CapturePngAsync(CancellationToken cancellationToken = default);
}
