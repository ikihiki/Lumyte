namespace Lumyte.Execution;

/// <summary>Thread-safe control of a module gate, valid until successful shutdown.</summary>
public sealed class GateControl
{
    private readonly GateDefinition _gate;

    internal GateControl(GateDefinition gate)
    {
        _gate = gate;
    }

    /// <summary>Opens the gate and wakes its consumers.</summary>
    public void Open()
    {
        lock (_gate.Module.Runtime.Sync)
        {
            Verify();
            if (_gate.Failure is not null)
            {
                throw new InvalidOperationException("A failed gate cannot reopen.");
            }

            _gate.Open = true;
            _gate.Module.Runtime.Notify();
        }
    }

    /// <summary>Closes the gate without interrupting started handlers.</summary>
    public void Close()
    {
        lock (_gate.Module.Runtime.Sync)
        {
            Verify();
            _gate.Open = false;
        }
    }

    /// <summary>Permanently fails the gate and wakes affected consumers.</summary>
    /// <param name="error">The cause.</param>
    public void Fail(Exception error)
    {
        lock (_gate.Module.Runtime.Sync)
        {
            Verify();
            _gate.Failure ??= error;
            _gate.Open = false;
            _gate.Module.Runtime.Notify();
        }
    }

    private void Verify()
    {
        ExecutionRuntime.VerifyOutsideHandler();
        if (_gate.Module.Runtime.Status == ExecutionStatus.Stopped)
        {
            throw new InvalidOperationException("Runtime has stopped.");
        }
    }
}
