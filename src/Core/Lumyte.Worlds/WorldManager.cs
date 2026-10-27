namespace Lumyte.Worlds;

/// <summary>Owns worlds and commits stack and destruction requests at frame boundaries.</summary>
public sealed class WorldManager
{
    private readonly List<World> _worlds = [];
    private readonly HashSet<World> _destroyRequests = [];

    /// <summary>Initializes a new instance of the <see cref="WorldManager"/> class.</summary>
    public WorldManager()
    {
        Worlds = _worlds.AsReadOnly();
        Stack = new WorldStack(this);
    }

    /// <summary>Gets the non-owning world stack.</summary>
    public WorldStack Stack { get; }

    /// <summary>Gets the live worlds, including worlds outside the stack.</summary>
    public IReadOnlyList<World> Worlds { get; }

    internal bool IsApplying { get; private set; }

    /// <summary>Creates an inactive world.</summary>
    /// <returns>The new world.</returns>
    public World CreateWorld()
    {
        if (IsApplying)
        {
            throw new InvalidOperationException("World creation during boundary notifications is not supported.");
        }

        var world = new World(this);
        _worlds.Add(world);
        return world;
    }

    /// <summary>Requests destruction at the next boundary; duplicate requests are harmless.</summary>
    /// <param name="world">A world owned by this manager.</param>
    public void RequestDestroy(World world)
    {
        Validate(world);
        _destroyRequests.Add(world);
    }

    /// <summary>Commits a batch before frame input, update and drawing. Reentry is forbidden.</summary>
    public void BeginFrame()
    {
        if (IsApplying || Stack.IsDispatching)
        {
            throw new InvalidOperationException("Frame boundary application cannot be reentered.");
        }

        IsApplying = true;
        try
        {
            World[] destroyed = _destroyRequests.ToArray();
            Stack.Commit(destroyed);
            _destroyRequests.ExceptWith(destroyed);
            WorldExecutionState[] previous = _worlds.Select(world => world.ExecutionState).ToArray();
            foreach (World world in _worlds)
            {
                world.ExecutionState = default;
            }

            Stack.ApplyStates();
            var errors = new List<Exception>();
            for (int index = 0; index < _worlds.Count; index++)
            {
                _worlds[index].Notify(previous[index], errors);
            }

            foreach (World world in destroyed)
            {
                _destroyRequests.Remove(world);
                _worlds.Remove(world);
                world.Destroy();
            }

            if (errors.Count > 0)
            {
                throw new AggregateException("Execution state notifications failed after the batch was committed.", errors);
            }
        }
        finally
        {
            IsApplying = false;
        }
    }

    internal void Validate(World world)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (world.Owner != this)
        {
            throw new ArgumentException("The world belongs to another manager.", nameof(world));
        }

        ObjectDisposedException.ThrowIf(world.IsDestroyed, world);
    }
}
