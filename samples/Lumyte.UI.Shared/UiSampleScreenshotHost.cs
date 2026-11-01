using Lumyte.UI.Automation;

namespace Lumyte.UI.SampleSupport;

internal sealed class UiSampleScreenshotHost(IUiAutomationHost host, Func<CancellationToken, ValueTask<byte[]>> capture) : IUiAutomationHost, IUiAutomationScreenshotHost
{
    public TimeProvider TimeProvider => host.TimeProvider;

    public ValueTask<T> RunOnUiThreadAsync<T>(Func<UiSurface, T> action, CancellationToken cancellationToken = default) => host.RunOnUiThreadAsync(action, cancellationToken);

    public ValueTask NextFrameAsync(CancellationToken cancellationToken = default) => host.NextFrameAsync(cancellationToken);

    public ValueTask<IUiAutomationInputLease> AcquireInputAsync(CancellationToken cancellationToken = default) => host.AcquireInputAsync(cancellationToken);

    public ValueTask<byte[]> CapturePngAsync(CancellationToken cancellationToken = default) => capture(cancellationToken);
}
