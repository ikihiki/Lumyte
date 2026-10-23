namespace Lumyte.Execution;

/// <summary>Temporary declaration authority for one module.</summary>
public sealed class ModuleBuilder
{
    private readonly ModuleDefinition _module;
    private readonly HashSet<string> _names = new(StringComparer.Ordinal);
    private bool _active = true;

    internal ModuleBuilder(ModuleDefinition module)
    {
        _module = module;
    }

    /// <summary>Declares an owner.</summary>
    /// <param name="name">The local unique name.</param>
    /// <param name="requirement">The ownership constraint.</param>
    /// <returns>The owner key.</returns>
    public OwnerKey Owner(string name, OwnerRequirement requirement)
    {
        ClaimName(name);
        if (!Enum.IsDefined(requirement))
        {
            throw new ArgumentOutOfRangeException(nameof(requirement));
        }

        var owner = new OwnerDefinition(_module, name, requirement);
        _module.Runtime.Owners.Add(owner);
        return new OwnerKey(owner);
    }

    /// <summary>Declares a FIFO group shared by different command types.</summary>
    /// <param name="name">The unique name.</param>
    /// <param name="owner">The local owner.</param>
    /// <returns>The group key.</returns>
    public OrderGroupKey OrderGroup(string name, OwnerKey owner)
    {
        ClaimName(name);
        OwnerDefinition definition = Verify(owner.Definition);
        var group = new GroupDefinition(definition, name);
        definition.Groups.Add(group);
        return new OrderGroupKey(group);
    }

    /// <summary>Declares a closed gate.</summary>
    /// <param name="name">The unique name.</param>
    /// <returns>The gate key.</returns>
    public GateKey Gate(string name)
    {
        ClaimName(name);
        var gate = new GateDefinition(_module, name);
        return new GateKey(gate);
    }

    /// <summary>Declares a dependency without depending on DI enumeration order.</summary>
    /// <typeparam name="TModule">The exact registered implementation type.</typeparam>
    public void DependsOn<TModule>()
        where TModule : IExecutionModule
    {
        CheckActive();
        _module.DependencyTypes.Add(typeof(TModule));
    }

    /// <summary>Declares a typed command and its fixed endpoint.</summary>
    /// <typeparam name="TCommand">The command payload type.</typeparam>
    /// <typeparam name="TResult">The handler return type.</typeparam>
    /// <typeparam name="THandler">The handler.</typeparam>
    /// <param name="name">The unique name.</param>
    /// <param name="handler">The retained handler.</param>
    /// <param name="options">Target and authority.</param>
    /// <param name="port">An optional static endpoint.</param>
    /// <returns>The typed work key.</returns>
    public WorkKey<TCommand, TResult> Command<TCommand, TResult, THandler>(string name, THandler handler, WorkOptions options, CommandPort<TCommand, TResult>? port = null)
        where TCommand : struct
        where THandler : ICommandHandler<TCommand, TResult>
    {
        ClaimName(name);
        if (!options.Target.IsValid || !Enum.IsDefined(options.Admission))
        {
            throw new ArgumentException("Invalid work options.", nameof(options));
        }

        if (options.Target.Owner is { } owner)
        {
            Verify(owner);
            if (options.OrderGroup.Definition?.Owner != owner)
            {
                throw new ArgumentException("Owner work needs a group on that owner.", nameof(options));
            }
        }
        else if (options.OrderGroup.Definition is not null)
        {
            throw new ArgumentException("CPU work cannot have an owner group.", nameof(options));
        }

        var work = new TypedWorkDefinition<TCommand, TResult, THandler>(_module, name, options, Gates(options.RequiredGates), handler, port ?? new CommandPort<TCommand, TResult>());
        _module.Runtime.Works.Add(work);
        return new WorkKey<TCommand, TResult>(work);
    }

    /// <summary>Declares an asynchronous CPU workflow with bounded synchronous steps.</summary>
    /// <typeparam name="TCommand">The operation input.</typeparam>
    /// <typeparam name="TResult">The operation result.</typeparam>
    /// <typeparam name="THandler">The retained handler type.</typeparam>
    /// <param name="name">The unique name.</param>
    /// <param name="handler">The retained handler.</param>
    /// <param name="port">The fixed endpoint.</param>
    public void Workflow<TCommand, TResult, THandler>(string name, THandler handler, WorkflowPort<TCommand, TResult> port)
        where TCommand : struct
        where THandler : IWorkflowHandler<TCommand, TResult>
    {
        ClaimName(name);
        _module.Runtime.Works.Add(new TypedWorkflowDefinition<TCommand, TResult, THandler>(_module, name, handler, port));
    }

    /// <summary>Declares a CPU range handler.</summary>
    /// <typeparam name="TContext">The range input type.</typeparam>
    /// <typeparam name="THandler">The handler.</typeparam>
    /// <param name="name">The unique name.</param>
    /// <param name="handler">The retained handler.</param>
    /// <returns>The range key.</returns>
    public RangeWorkKey<TContext> Range<TContext, THandler>(string name, THandler handler)
        where TContext : struct
        where THandler : IRangeWorkHandler<TContext>
    {
        ClaimName(name);
        var range = new TypedRangeDefinition<TContext, THandler>(_module, name, handler);
        _module.Runtime.RangeClearers.Add(range.Clear);
        return new RangeWorkKey<TContext>(range);
    }

    /// <summary>Declares a directly driven owner update.</summary>
    /// <typeparam name="THandler">The handler.</typeparam>
    /// <param name="name">The unique name.</param>
    /// <param name="owner">The local owner.</param>
    /// <param name="handler">The retained handler.</param>
    /// <param name="requiredGates">Copied gate keys.</param>
    /// <returns>The update key.</returns>
    public UpdateKey Update<THandler>(string name, OwnerKey owner, THandler handler, ReadOnlyMemory<GateKey> requiredGates = default)
        where THandler : IUpdateHandler
    {
        ClaimName(name);
        var update = new TypedUpdateDefinition<THandler>(_module, name, Verify(owner.Definition), Gates(requiredGates), handler);
        _module.Runtime.Updates.Add(update);
        return new UpdateKey(update);
    }

    internal void Close() => _active = false;

    private void CheckActive()
    {
        if (!_active)
        {
            throw new InvalidOperationException("Declaration builder is no longer active.");
        }
    }

    private void ClaimName(string name)
    {
        CheckActive();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!_names.Add(name))
        {
            throw new ArgumentException("Duplicate declaration name.", nameof(name));
        }
    }

    private OwnerDefinition Verify(OwnerDefinition? owner)
    {
        if (owner?.Module != _module)
        {
            throw new ArgumentException("The owner belongs to another module or is invalid.");
        }

        return owner;
    }

    private GateDefinition[] Gates(ReadOnlyMemory<GateKey> keys)
    {
        var gates = new GateDefinition[keys.Length];
        for (int i = 0; i < gates.Length; i++)
        {
            GateDefinition? gate = keys.Span[i].Definition;
            if (gate?.Module.Runtime != _module.Runtime)
            {
                throw new ArgumentException("A gate belongs to another runtime or is invalid.", nameof(keys));
            }

            gates[i] = gate;
        }

        return gates;
    }
}
