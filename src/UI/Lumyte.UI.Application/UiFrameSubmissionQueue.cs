namespace Lumyte.UI.Application;

internal sealed class UiFrameSubmissionQueue(SemaphoreSlim submissionGate, int maximumFrames)
{
    private readonly SemaphoreSlim _slots = new(maximumFrames, maximumFrames);

    internal async ValueTask<Flight> EnterAsync()
    {
        await _slots.WaitAsync();
        try
        {
            await submissionGate.WaitAsync();
            return new(submissionGate, _slots);
        }
        catch
        {
            _slots.Release();
            throw;
        }
    }

    internal sealed class Flight(SemaphoreSlim gate, SemaphoreSlim slots) : IDisposable
    {
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
            }

            slots.Release();
        }

        internal void Submitted()
        {
            if (_held)
            {
                _held = false;
                gate.Release();
            }
        }

        internal async ValueTask ResumeAsync()
        {
            if (!_held)
            {
                await gate.WaitAsync();
                _held = true;
            }
        }
    }
}
