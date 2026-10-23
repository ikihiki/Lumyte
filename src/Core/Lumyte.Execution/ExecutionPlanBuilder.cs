namespace Lumyte.Execution;

/// <summary>Temporary placement authority for a single module.</summary>
public sealed class ExecutionPlanBuilder
{
    private readonly ModuleDefinition _module;
    private readonly HashSet<string> _names = new(StringComparer.Ordinal);
    private bool _active = true;

    internal ExecutionPlanBuilder(ModuleDefinition module)
    {
        _module = module;
    }

    /// <summary>Declares a host phase.</summary>
    /// <param name="name">The local name.</param>
    /// <returns>The phase key.</returns>
    public PhaseKey HostPhase(string name)
    {
        Claim(name);
        var phase = new PhaseDefinition(_module, name);
        _module.Runtime.Phases.Add(phase);
        return new PhaseKey(phase);
    }

    /// <summary>Declares a thread without starting it.</summary>
    /// <param name="name">The local name.</param>
    /// <returns>The thread key.</returns>
    public ThreadKey DedicatedThread(string name)
    {
        Claim(name);
        var thread = new ThreadDefinition(_module, name);
        _module.Runtime.Threads.Add(thread);
        return new ThreadKey(thread);
    }

    /// <summary>Binds a local owner to a host phase.</summary>
    /// <param name="owner">The owner.</param>
    /// <param name="phase">The phase.</param>
    public void BindHost(OwnerKey owner, PhaseKey phase)
    {
        OwnerDefinition definition = Verify(owner.Definition);
        Check(phase.Definition?.Module.Runtime);
        EnsureUnbound(definition);
        definition.Phase = phase.Definition;
        phase.Definition!.Groups.AddRange(definition.Groups);
    }

    /// <summary>Binds a local owner to a dedicated thread.</summary>
    /// <param name="owner">The owner.</param>
    /// <param name="thread">The thread.</param>
    public void BindDedicated(OwnerKey owner, ThreadKey thread)
    {
        OwnerDefinition definition = Verify(owner.Definition);
        Check(thread.Definition?.Module.Runtime);
        EnsureUnbound(definition);
        if (definition.Requirement == OwnerRequirement.HostOnly)
        {
            throw new InvalidOperationException("Host-only owners cannot move to dedicated threads.");
        }

        definition.Thread = thread.Definition;
        thread.Definition!.Owners.Add(definition);
        thread.Definition.Groups.AddRange(definition.Groups);
    }

    /// <summary>Drives a local update in its owner's host phase.</summary>
    /// <param name="update">The update.</param>
    /// <param name="phase">The phase.</param>
    public void DriveOnHost(UpdateKey update, PhaseKey phase)
    {
        UpdateDefinition definition = Verify(update.Definition);
        Check(phase.Definition?.Module.Runtime);
        EnsureUndriven(definition);
        definition.Phase = phase.Definition;
        phase.Definition!.Updates.Add(definition);
    }

    /// <summary>Drives a local update periodically on its owner thread.</summary>
    /// <param name="update">The update.</param>
    /// <param name="options">The periodic budgets.</param>
    public void DrivePeriodic(UpdateKey update, ThreadLoopOptions options)
    {
        UpdateDefinition definition = Verify(update.Definition);
        EnsureUndriven(definition);
        definition.Options = options;
    }

    internal void Close() => _active = false;

    private static void EnsureUnbound(OwnerDefinition owner)
    {
        if (owner.Phase is not null || owner.Thread is not null)
        {
            throw new InvalidOperationException("Owner already has a placement.");
        }
    }

    private static void EnsureUndriven(UpdateDefinition update)
    {
        if (update.Phase is not null || update.Options is not null)
        {
            throw new InvalidOperationException("Update already has a driver.");
        }
    }

    private void Claim(string name)
    {
        Check(_module.Runtime);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!_names.Add(name))
        {
            throw new ArgumentException("Duplicate placement name.", nameof(name));
        }
    }

    private void Check(ExecutionRuntime? runtime)
    {
        if (!_active || runtime != _module.Runtime)
        {
            throw new InvalidOperationException("Placement builder expired or the key belongs to another runtime.");
        }
    }

    private OwnerDefinition Verify(OwnerDefinition? owner)
    {
        Check(owner?.Module.Runtime);
        if (owner!.Module != _module)
        {
            throw new ArgumentException("The owner belongs to another module.");
        }

        return owner;
    }

    private UpdateDefinition Verify(UpdateDefinition? update)
    {
        Check(update?.Module.Runtime);
        if (update!.Module != _module)
        {
            throw new ArgumentException("The update belongs to another module.");
        }

        return update;
    }
}
