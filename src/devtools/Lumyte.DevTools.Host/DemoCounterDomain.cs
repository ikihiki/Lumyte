namespace Lumyte.DevTools.Host;

public sealed class DemoCounterDomain : IDisposable
{
    public static readonly DevToolsDomain Domain = new("demo");
    public static readonly DevToolsQuery<GetCounterRequest, CounterState> GetCounter = new("getCounter");
    public static readonly DevToolsCommand<ChangeCounterRequest, CounterState> ChangeCounter = new("changeCounter");
    public static readonly DevToolsEvent<CounterState> CounterChanged = new("counterChanged");

    private readonly Lock sync = new();
    private readonly IDisposable queryRegistration;
    private readonly IDisposable commandRegistration;
    private readonly DevToolsEventPublisher<CounterState> publisher;
    private int value;
    private int disposed;

    public DemoCounterDomain(DevToolsHub hub)
    {
        ArgumentNullException.ThrowIfNull(hub);
        publisher = hub.RegisterEvent(Domain, CounterChanged);
        queryRegistration = hub.RegisterQuery(Domain, GetCounter, GetAsync);
        commandRegistration = hub.RegisterCommand(Domain, ChangeCounter, ChangeAsync);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        commandRegistration.Dispose();
        queryRegistration.Dispose();
        publisher.Dispose();
    }

    private ValueTask<CounterState> GetAsync(GetCounterRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            return ValueTask.FromResult(new CounterState(value));
        }
    }

    private async ValueTask<CounterState> ChangeAsync(ChangeCounterRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CounterState state;
        lock (sync)
        {
            value = checked(value + request.Delta);
            state = new CounterState(value);
        }

        await publisher.PublishAsync(state, cancellationToken).ConfigureAwait(false);
        return state;
    }
}

public sealed record GetCounterRequest;

public sealed record ChangeCounterRequest(int Delta);

public sealed record CounterState(int Value);
