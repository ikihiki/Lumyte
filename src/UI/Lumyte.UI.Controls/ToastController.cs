using System.Runtime.ExceptionServices;
using Lumyte.Core.Time;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Controls;

/// <summary>A bounded FIFO borrowed by one host, advanced only on its owner loop.</summary>
public sealed class ToastController : IDisposable
{
    private readonly IMonotonicClock _clock;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private List<Entry> _entries = [];
    private List<Entry>? _proposal;
    private ToastHostElement? _host;
    private ToastHostElement? _reservation;
    private bool _changing;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="ToastController"/> class.</summary>
    /// <param name="clock">The borrowed monotonic clock.</param>
    /// <param name="capacity">Maximum visible plus queued entries, from 1 through 1024.</param>
    public ToastController(IMonotonicClock clock, int capacity = 16)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(capacity, 1024);
        _clock = clock;
        Capacity = capacity;
    }

    /// <summary>Gets the maximum accepted queue length, including the one visible entry.</summary>
    public int Capacity { get; }

    internal bool HasExpiryWork => !_disposed && _entries.FirstOrDefault()?.Duration.Ticks > 0;

    /// <summary>Gets the accepted visible and queued identities without retaining widget payloads.</summary>
    /// <returns>The bounded identity snapshot.</returns>
    public IReadOnlyList<ToastSnapshot> CaptureSnapshot()
    {
        Check();
        return Array.AsReadOnly(_entries.Select((entry, index) => new ToastSnapshot(entry.Id, index == 0 && _host?.IsPresented == true)).ToArray());
    }

    /// <summary>Enqueues one fresh-content factory; zero duration is sticky, positive duration expires from the first visible owner update.</summary>
    /// <param name="createContent">Invoked once when this entry reaches an attached visible host.</param>
    /// <param name="duration">Zero or a positive duration up to one day.</param>
    /// <param name="onClosed">Invoked once after accepted removal, never during publication.</param>
    /// <returns>The stable identity. Full queues throw without invoking the factory.</returns>
    public ToastId Enqueue(Func<Widget> createContent, TimeSpan duration, Action<ToastId>? onClosed = null)
    {
        Check();
        ArgumentNullException.ThrowIfNull(createContent);
        if (duration < TimeSpan.Zero || duration > TimeSpan.FromDays(1))
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        if (_entries.Count == Capacity)
        {
            throw new InvalidOperationException("The notification queue is full.");
        }

        var entry = new Entry(new(Guid.NewGuid()), createContent, duration, onClosed);
        Change([.. _entries, entry]);
        return entry.Id;
    }

    /// <summary>Marshals enqueue through an explicitly borrowed owner dispatcher.</summary>
    /// <param name="dispatcher">The host owner dispatcher.</param>
    /// <param name="createContent">The fresh content factory, executed only on the owner.</param>
    /// <param name="duration">The accepted lifetime.</param>
    /// <param name="onClosed">The closure callback.</param>
    /// <param name="cancellationToken">Cancels before owner execution.</param>
    /// <returns>The enqueue completion and identity.</returns>
    public async ValueTask<ToastId> EnqueueAsync(IUiDispatcher dispatcher, Func<Widget> createContent, TimeSpan duration, Action<ToastId>? onClosed = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ToastId result = default;
        await dispatcher.InvokeAsync(() => result = Enqueue(createContent, duration, onClosed), cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <summary>Removes an accepted identity. Unknown or already removed identities do nothing.</summary>
    /// <param name="id">The identity.</param>
    /// <returns>Whether an entry was removed.</returns>
    public bool Dismiss(ToastId id)
    {
        Check();
        Entry? entry = _entries.Find(item => item.Id == id);
        if (entry is null)
        {
            return false;
        }

        Change(_entries.Where(item => item != entry).ToList(), () => entry.Closed?.Invoke(entry.Id));
        return true;
    }

    /// <summary>Marshals dismissal through the explicitly supplied owner dispatcher.</summary>
    /// <param name="dispatcher">The owner dispatcher.</param>
    /// <param name="id">The identity.</param>
    /// <param name="cancellationToken">Cancels before owner execution.</param>
    /// <returns>The operation completion.</returns>
    public ValueTask DismissAsync(IUiDispatcher dispatcher, ToastId id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        return dispatcher.InvokeAsync(() => { Dismiss(id); }, cancellationToken);
    }

    /// <summary>Removes a bounded snapshot before invoking closure callbacks; callbacks may enqueue new entries.</summary>
    public void Clear()
    {
        Check();
        Entry[] removed = _entries.ToArray();
        Change([], () =>
        {
            List<Exception>? errors = null;
            foreach (Entry entry in removed)
            {
                try
                {
                    entry.Closed?.Invoke(entry.Id);
                }
                catch (Exception error)
                {
                    (errors ??= []).Add(error);
                }
            }

            if (errors is not null)
            {
                throw new AggregateException(errors);
            }
        });
    }

    /// <summary>Advances at most one expiry per owner boundary. Hidden hosts defer mutation until visible again.</summary>
    public void Advance()
    {
        Check();
        if (_host is not { IsPresented: true } || _entries.FirstOrDefault() is not { Duration.Ticks: > 0 } entry)
        {
            return;
        }

        long now = _clock.Now.Ticks;
        if (!ReferenceEquals(_entries.FirstOrDefault(), entry))
        {
            return;
        }

        if (entry.Started is not { } start)
        {
            entry.Started = now;
            return;
        }

        if (now >= start && (decimal)now - start >= entry.Duration.Ticks)
        {
            Dismiss(entry.Id);
        }
    }

    /// <summary>Stops commands and frame work without disposing the borrowed surface or clock; the already attached host may reconcile as empty, but new hosts are rejected.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Check();
        Change([]);
        _disposed = true;
        _reservation = null;
        _host?.Stopped();
        _host = null;
    }

    internal Widget Prepare(ToastHostElement host, UiElementBuildContext context, out Action commit)
    {
        if (_disposed && host.Borrows(this))
        {
            CheckThread();
            commit = static () => { };
            return Lumyte.UI.Compose.SizedBox(hitTestVisible: false);
        }

        Check();
        if ((_host is not null && _host != host) || (_reservation is not null && _reservation != host))
        {
            throw new InvalidOperationException("A notification controller can be borrowed by only one mounted host.");
        }

        context.AfterPreparation(() =>
        {
            if (_reservation == host)
            {
                _reservation = null;
            }
        });
        _reservation = host;
        List<Entry> entries = _proposal ?? _entries;
        Entry? first = entries.FirstOrDefault();
        Widget root;
        if (first is null)
        {
            root = Lumyte.UI.Compose.SizedBox(hitTestVisible: false);
        }
        else
        {
            if (first.Content is null)
            {
                if (first.Attempted)
                {
                    throw new InvalidOperationException("The notification content factory previously failed; dismiss its identity before retrying.");
                }

                first.Attempted = true;
                Widget candidate = first.Factory() ?? throw new InvalidOperationException("A notification factory returned null.");
                candidate.ValidateFreshItemRoot();
                if (candidate.HasBeenMounted)
                {
                    throw new InvalidOperationException("Notification content must be newly constructed, not a detached old root.");
                }

                Compose.Definitions.DialogContent generation = Compose.DialogContent(key: first.Id.Value.ToString("N"));
                generation.ContentChildren.Initialize(candidate);
                first.Content = generation;
            }

            root = first.Content;
        }

        commit = () =>
        {
            _host = host;
            _entries = entries;
        };
        return root;
    }

    internal void Detach(ToastHostElement host)
    {
        if (_host == host)
        {
            _host = null;

            // Accepted payloads cannot be remounted elsewhere. Their lifetime ends with their host.
            _entries.Clear();
        }
    }

    private void Change(List<Entry> next, Action? afterCommit = null)
    {
        if (_changing || _reservation is not null)
        {
            throw new InvalidOperationException("Notification commands cannot reenter content preparation.");
        }

        Exception? failure = null;
        _changing = true;
        _proposal = next;
        try
        {
            if (_host is { } host)
            {
                host.Refresh();
                if (!ReferenceEquals(_entries, next))
                {
                    throw new InvalidOperationException("Notification changes require an idle owner update boundary.");
                }
            }
            else
            {
                _entries = next;
            }
        }
        catch (Exception error)
        {
            failure = error;
        }
        finally
        {
            _proposal = null;
            _changing = false;
        }

        if (ReferenceEquals(_entries, next))
        {
            try
            {
                afterCommit?.Invoke();
            }
            catch (Exception error)
            {
                failure = failure is null ? error : new AggregateException(failure, error);
            }
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private void Check()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CheckThread();
    }

    private void CheckThread()
    {
        if (Environment.CurrentManagedThreadId != _thread)
        {
            throw new InvalidOperationException("Notification commands require their creating owner thread; use an explicit dispatcher otherwise.");
        }
    }

    private sealed class Entry(ToastId id, Func<Widget> factory, TimeSpan duration, Action<ToastId>? closed)
    {
        internal ToastId Id { get; } = id;

        internal Func<Widget> Factory { get; } = factory;

        internal TimeSpan Duration { get; } = duration;

        internal Action<ToastId>? Closed { get; } = closed;

        internal Widget? Content { get; set; }

        internal bool Attempted { get; set; }

        internal long? Started { get; set; }
    }
}
