namespace Lumyte.Graphics.Presentation.Benchmarks;

// Deliberately benchmark-owned. Does not call or impersonate DesktopDriver.
internal sealed class DemandSchedule
{
    private bool _pending;

    internal long InputId { get; private set; }

    internal int Reservations { get; private set; }

    internal static void Check()
    {
        var schedule = new DemandSchedule();
        Require(!schedule.TryReserve());
        schedule.Invalidate(1);
        Require(schedule.TryReserve());
        for (int i = 0; i < 1000; i++)
        {
            Require(!schedule.TryReserve());
        }

        schedule.Invalidate(2);
        schedule.Invalidate(3);
        Require(schedule.TryReserve());
        Require(!schedule.TryReserve() && schedule.InputId == 3 && schedule.Reservations == 2);
    }

    internal void Invalidate(long inputId)
    {
        if (inputId <= InputId)
        {
            throw new ArgumentException("Input IDs must increase.");
        }

        InputId = inputId;
        _pending = true;
    }

    internal bool TryReserve()
    {
        if (!_pending)
        {
            return false;
        }

        _pending = false;
        Reservations++;
        return true;
    }

    private static void Require(bool value)
    {
        if (!value)
        {
            throw new InvalidOperationException("Benchmark policy contract failed.");
        }
    }
}
