namespace Lumyte.DevTools;

public sealed class DevToolsEventPublisher<T> : IDisposable
{
    private readonly DevToolsHub hub;
    private readonly DevToolsDomain domain;
    private readonly DevToolsEvent<T> @event;
    private readonly long registrationId;
    private int disposed;

    internal DevToolsEventPublisher(DevToolsHub hub, DevToolsDomain domain, DevToolsEvent<T> @event, long registrationId)
    {
        this.hub = hub;
        this.domain = domain;
        this.@event = @event;
        this.registrationId = registrationId;
    }

    public ValueTask PublishAsync(T value, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed != 0, this);
        return hub.PublishAsync(domain, @event, registrationId, value, cancellationToken);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
        {
            hub.Unregister(domain, DevToolsFeatureKind.Event, @event.Name, registrationId);
        }
    }
}
