using System.Runtime.CompilerServices;
using Lumyte.Resources;

namespace Lumyte.Maps;

// Manager build views pin their entire publication snapshot, not only the requested tile.
// Pause all Maps admission and drain terminal retains before reclaiming retired generations.
// This is an ownership barrier over the existing Manager, not another resource executor.
internal sealed class MapMaintenanceCoordinator
{
    private static readonly ConditionalWeakTable<ResourceManager, MapMaintenanceCoordinator> _coordinators = new();
    private readonly object _gate = new();
    private readonly HashSet<object> _requests = [];
    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _retains;
    private bool _trimming;

    internal static MapMaintenanceCoordinator For(ResourceManager manager) => _coordinators.GetValue(manager, static _ => new());

    internal bool TryBeginRetain()
    {
        lock (_gate)
        {
            if (_trimming || _requests.Count != 0)
            {
                return false;
            }

            _retains++;
            return true;
        }
    }

    internal void EndRetain()
    {
        lock (_gate)
        {
            if (_retains <= 0)
            {
                throw new InvalidOperationException("A Maps retain must be owned until terminal collection.");
            }

            _retains--;
            if (_retains == 0)
            {
                Signal();
            }
        }
    }

    internal void Request(object owner)
    {
        lock (_gate)
        {
            if (_requests.Add(owner))
            {
                Signal();
            }
        }
    }

    internal bool TryBeginTrim()
    {
        lock (_gate)
        {
            if (_trimming || _retains != 0)
            {
                return false;
            }

            _trimming = true;
            return true;
        }
    }

    internal async Task BeginTrimAsync(object owner)
    {
        Request(owner);
        while (true)
        {
            Task changed;
            lock (_gate)
            {
                if (!_trimming && _retains == 0)
                {
                    _trimming = true;
                    return;
                }

                changed = _changed.Task;
            }

            await changed.ConfigureAwait(false);
        }
    }

    internal void EndTrim(object owner, bool stillRequired)
    {
        lock (_gate)
        {
            _trimming = false;
            if (!stillRequired)
            {
                _requests.Remove(owner);
            }

            Signal();
        }
    }

    private void Signal()
    {
        TaskCompletionSource previous = _changed;
        _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        previous.TrySetResult();
    }
}
