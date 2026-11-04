namespace Lumyte.Graphics.Presentation.Benchmarks;

// Managed fault injection only: no native device, window, driver fault or elapsed thresholds.
internal static class LifetimeChecks
{
    internal static void Check()
    {
        var released = new List<string>();
        var scope = new RetirementScope();
        Probe device = scope.Own(new Probe("device", released));
        Probe prepared = scope.Own(new Probe("prepared", released));
        Probe command = scope.Own(new Probe("command", released));
        scope.Release(command);
        scope.Release(prepared);
        scope.Dispose();
        scope.Dispose();
        Require(released.SequenceEqual(new[] { "command", "prepared", "device" }));
        Require(device.Attempts == 1 && prepared.Attempts == 1 && command.Attempts == 1);

        var parent = new RetirementScope();
        Probe lower = parent.Own(new Probe("lower-owner", released));
        RetirementScope child = parent.Own(new RetirementScope { Parent = parent });
        Probe failed = child.Own(new Probe("failed-child", released, fail: true));
        bool rejected = false;
        try
        {
            parent.Dispose();
        }
        catch (InvalidOperationException error) when (error.Message == "injected managed cleanup failure")
        {
            rejected = true;
        }

        Require(rejected && failed.Attempts == 1 && lower.Attempts == 0);
        parent.Dispose();
        child.Dispose();
        Require(failed.Attempts == 1 && lower.Attempts == 0);
    }

    private static void Require(bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Managed retirement ownership check failed.");
        }
    }

    private sealed class Probe(string name, List<string> released, bool fail = false) : IDisposable
    {
        internal int Attempts { get; private set; }

        public void Dispose()
        {
            Attempts++;
            if (fail)
            {
                throw new InvalidOperationException("injected managed cleanup failure");
            }

            released.Add(name);
        }
    }
}
