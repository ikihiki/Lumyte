namespace Lumyte.Diagnostics.Server;

internal sealed class DiagnosticUiChanges
{
    private readonly object _gate = new();
    private TaskCompletionSource<long>? _next;
    private long _version;

    internal long Version
    {
        get
        {
            lock (_gate)
            {
                return _version;
            }
        }
    }

    internal void Notify()
    {
        lock (_gate)
        {
            TaskCompletionSource<long>? previous = _next;
            _next = null;
            _version++;
            previous?.TrySetResult(_version);
        }
    }

    internal Task<long> WaitAsync(long version, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (version != _version)
            {
                return Task.FromResult(_version);
            }

            _next ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
            return _next.Task.WaitAsync(cancellationToken);
        }
    }
}
