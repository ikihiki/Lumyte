namespace Lumyte.UI.Application;

internal sealed class UiFrameSubmissionQueue(SemaphoreSlim submissionGate, int maximumFrames)
{
    private readonly SemaphoreSlim _slots = new(maximumFrames, maximumFrames);
    private int _slotsHeld;
    private int _gatesHeld;

    internal async ValueTask<Flight> EnterAsync()
    {
        using (UiApplicationProfiling.Scope? profile = UiApplicationProfiling.Begin(17))
        {
            try
            {
                await _slots.WaitAsync();
            }
            catch (Exception error)
            {
                profile?.Failed(error);
                throw;
            }
        }

        Interlocked.Increment(ref _slotsHeld);
        UiApplicationProfiling.Scope? flight = UiApplicationProfiling.Begin(20);
        try
        {
            using UiApplicationProfiling.Scope? profile = UiApplicationProfiling.Begin(18);
            try
            {
                await submissionGate.WaitAsync();
                Interlocked.Increment(ref _gatesHeld);
                profile?.Counter(17, Volatile.Read(ref _gatesHeld));
                profile?.Dispose();
                flight?.Detach();
                return new(this, submissionGate, _slots, flight);
            }
            catch (Exception error)
            {
                profile?.Failed(error);
                throw;
            }
        }
        catch (Exception error)
        {
            _slots.Release();
            Interlocked.Decrement(ref _slotsHeld);
            flight?.Failed(error);
            flight?.Counter(16, Volatile.Read(ref _slotsHeld));
            flight?.Dispose();
            throw;
        }
    }

    internal sealed class Flight(UiFrameSubmissionQueue owner, SemaphoreSlim gate, SemaphoreSlim slots, UiApplicationProfiling.Scope? profile) : IDisposable
    {
        private readonly UiApplicationProfiling.Key _key = UiApplicationProfiling.ReadKey();
        private bool _held = true;
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_held)
            {
                gate.Release();
                Interlocked.Decrement(ref owner._gatesHeld);
            }

            slots.Release();
            Interlocked.Decrement(ref owner._slotsHeld);
            profile?.Counter(16, Volatile.Read(ref owner._slotsHeld));
            profile?.Counter(17, Volatile.Read(ref owner._gatesHeld));
            profile?.Dispose();
        }

        internal void Submitted()
        {
            if (_held)
            {
                _held = false;
                gate.Release();
                Interlocked.Decrement(ref owner._gatesHeld);
            }
        }

        internal async ValueTask ResumeAsync()
        {
            if (!_held)
            {
                using UiApplicationProfiling.Scope? wait = UiApplicationProfiling.Begin(19, _key);
                try
                {
                    await gate.WaitAsync();
                    _held = true;
                    Interlocked.Increment(ref owner._gatesHeld);
                    wait?.Counter(17, Volatile.Read(ref owner._gatesHeld));
                }
                catch (Exception error)
                {
                    wait?.Failed(error);
                    throw;
                }
            }
        }
    }
}
