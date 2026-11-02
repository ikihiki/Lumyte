using Lumyte.UI.Windowing;

namespace Lumyte.UI.Application;

/// <summary>Owns the UI loop and resources from startup through ordered shutdown.</summary>
public sealed partial class UiApplication
{
    private async Task StopCoreAsync()
    {
        ReleaseWindowSignals();
        _dispatcher.BeginStop();
        _startupCancellation.Cancel();
        foreach (Entry entry in _entries.Values.ToArray())
        {
            BeginStop(entry);
        }

        if (!_participantsStopping)
        {
            _participantsStopping = true;
            foreach (IUiApplicationParticipant participant in _participants)
            {
                participant.BeginStop();
            }
        }

        if (_startup is not null)
        {
            try
            {
                await _startup;
            }
            catch (Exception)
            {
                // Startup has its own failure observation; cleanup owns every successfully published partial resource.
            }
        }

        if (_reconciling is not null)
        {
            try
            {
                await _reconciling;
            }
            catch (Exception)
            {
                // The partially created entry remains in the owned entry set.
            }
        }

        foreach (Entry entry in _entries.Values.OrderByDescending(Depth).ThenBy(entry => entry.Declaration.Key == _primaryKey).ToArray())
        {
            await CloseEntryAsync(entry);
        }

        _popups?.BeginStop();
        while ((_popups?.IsStopped == false) || _participants.Any(participant => !participant.IsStopped))
        {
            await NextTurnAsync();
        }

        foreach (IUiApplicationParticipant participant in _participants.ToArray())
        {
            try
            {
                await participant.DisposeAsync();
            }
            catch (Exception error)
            {
                RecordFailure(new UiApplicationException("cleanup-failed", error));
            }
            finally
            {
                _participants.Remove(participant);
            }
        }

        _popups?.Dispose();
        _popups = null;
        if (!_managersStopped)
        {
            _windows?.BeginStop();
            if (!ReferenceEquals(_windows, _defaultWindows))
            {
                _defaultWindows?.BeginStop();
            }

            while (_windows?.IsStopped == false || _defaultWindows?.IsStopped == false)
            {
                _defaultWindows?.Advance();
                await NextTurnAsync();
            }

            _windows?.Dispose();
            if (!ReferenceEquals(_windows, _defaultWindows))
            {
                _defaultWindows?.Dispose();
            }

            _managersStopped = true;
            _windows = null;
            _defaultWindows = null;
        }

        if (_resources is not null)
        {
            await _resources.DisposeAsync();
            _resources = null;
        }

        await Driver.RetireResourcesAsync();
        await StopExecutionAsync();
        if (!_driverReleased)
        {
            await Driver.DisposeAsync();
            _driverReleased = true;
        }

        if (!_providerReleased)
        {
            _providerReleased = true;
            try
            {
                await _services.DisposeAsync();
            }
            catch (Exception error)
            {
                RecordFailure(new UiApplicationException("cleanup-failed", error));
            }
        }
    }

    private void BeginStop(Entry entry)
    {
        if (entry.Stopping)
        {
            return;
        }

        entry.Stopping = true;
        entry.Context.Chrome?.BeginStop();
        entry.Slot.BeginStop();
        entry.Content?.BeginStop();
        if (entry.Opened)
        {
            foreach (IUiApplicationParticipant participant in _participants)
            {
                participant.CloseWindow(entry.Context);
            }
        }
    }

    private async Task CloseEntryAsync(Entry entry, bool keepPrimaryTarget = false)
    {
        BeginStop(entry);
        while (entry.Content?.IsStopped == false || !entry.Slot.IsStopped || entry.Initialization is { IsCompleted: false } || entry.Context.Chrome?.IsStopped == false || _participants.Any(participant => !participant.IsWindowStopped(entry.Context)))
        {
            await NextTurnAsync();
        }

        Entry? root = _entries.Values.FirstOrDefault(candidate => ReferenceEquals(candidate.Native, entry.Root));
        if (root?.Drawing is { } drawing)
        {
            try
            {
                await drawing;
            }
            catch (Exception error)
            {
                RecordFailure(error);
            }
            finally
            {
                entry.Root.Source.CompleteFrame(drawing.IsCompletedSuccessfully && drawing.Result);
                root.Drawing = null;
            }
        }

        await Driver.DrainWindowAsync(entry.Root);
        if (entry.Slot.CurrentSurface is { } surface)
        {
            entry.Root.Source.Pause(surface);
        }

        if (entry.Context.Chrome is { } chrome)
        {
            await chrome.DetachAsync();
        }

        if (!entry.Retired)
        {
            if (entry.Content is not null)
            {
                await entry.Content.RetireAsync();
            }

            entry.Retired = true;
        }

        if (!entry.Detached)
        {
            if (entry.Slot.CurrentSurface is { } retiringSurface)
            {
                _inactiveFocus.Remove(retiringSurface);
            }

            await entry.Slot.DetachAsync();
            entry.Detached = true;
        }

        if (!entry.Released)
        {
            entry.Released = true;
            if (entry.Content is not null)
            {
                try
                {
                    await entry.Content.ReleaseAsync();
                }
                catch (Exception error)
                {
                    RecordFailure(new UiApplicationException("cleanup-failed", error));
                }
            }
        }

        if (entry.Registration is { } registration)
        {
            registration.Dispose();
            while (!registration.IsReleased)
            {
                await NextTurnAsync();
            }
        }
        else
        {
            UiWindowRequestId close = Windows.Close(entry.Context.Window);
            while (!Windows.TryGetRequest(close, out UiWindowRequestResult? result) || result?.Status == UiWindowRequestStatus.Pending)
            {
                await NextTurnAsync();
            }

            Windows.Release(entry.Context.Window);
        }

        if (!keepPrimaryTarget && entry.Native is { } native)
        {
            await Driver.CloseWindowAsync(native);
        }

        entry.Context.Close();
        _entries.Remove(entry.Declaration.Key);
        entry.Declaration.OnClosed?.Invoke(new(entry.Declaration.Key, entry.Context.Id, entry.Error is UiApplicationException applicationError ? applicationError.Code : entry.Error is null ? "closed" : "content-initialize-failed", entry.Error));
    }
}
