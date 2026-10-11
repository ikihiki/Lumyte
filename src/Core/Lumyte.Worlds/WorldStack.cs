namespace Lumyte.Worlds;

/// <summary>A non-owning stack with independently blocked update, drawing and input.</summary>
public sealed class WorldStack
{
    private readonly WorldManager _owner;
    private readonly List<World> _worlds = [];
    private List<Layer> _layers = [];
    private List<Action<List<Layer>>> _requests = [];

    internal WorldStack(WorldManager owner)
    {
        _owner = owner;
        Worlds = _worlds.AsReadOnly();
    }

    /// <summary>Gets worlds in bottom-to-top order.</summary>
    public IReadOnlyList<World> Worlds { get; }

    internal bool IsDispatching { get; private set; }

    /// <summary>Queues a push. Duplicate worlds cause the boundary batch to fail.</summary>
    /// <param name="world">The live world to push.</param>
    /// <param name="policy">The lower-layer blocking policy.</param>
    public void Push(World world, WorldLayerPolicy policy = default)
    {
        _owner.Validate(world);
        _requests.Add(layers => Add(layers, world, policy));
    }

    /// <summary>Queues a pop without destroying the world. Empty stacks are unchanged.</summary>
    public void Pop() => _requests.Add(RemoveTop);

    /// <summary>Queues replacement of the top layer, or a push when empty.</summary>
    /// <param name="world">The replacement world.</param>
    /// <param name="policy">The lower-layer blocking policy.</param>
    public void Replace(World world, WorldLayerPolicy policy = default)
    {
        _owner.Validate(world);
        _requests.Add(layers =>
        {
            RemoveTop(layers);
            Add(layers, world, policy);
        });
    }

    /// <summary>Updates enabled worlds from bottom to top without accumulating paused time.</summary>
    /// <param name="elapsed">The frame duration.</param>
    public void Update(TimeSpan elapsed)
    {
        EnsureDispatchable();
        IsDispatching = true;
        try
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(elapsed, TimeSpan.Zero);
            foreach (Layer layer in _layers)
            {
                if (layer.World.ExecutionState.UpdateEnabled)
                {
                    foreach (Scene scene in layer.World.Scenes)
                    {
                        scene.Update(elapsed);
                    }
                }
            }
        }
        finally
        {
            IsDispatching = false;
        }
    }

    /// <summary>Draws enabled worlds from bottom to top.</summary>
    public void Draw()
    {
        EnsureDispatchable();
        IsDispatching = true;
        try
        {
            foreach (Layer layer in _layers)
            {
                if (layer.World.ExecutionState.DrawEnabled)
                {
                    foreach (Scene scene in layer.World.Scenes)
                    {
                        scene.Draw();
                    }
                }
            }
        }
        finally
        {
            IsDispatching = false;
        }
    }

    /// <summary>Dispatches input top to bottom until blocked or consumed.</summary>
    public void HandleInput()
    {
        EnsureDispatchable();
        IsDispatching = true;
        try
        {
            for (int index = _layers.Count - 1; index >= 0; index--)
            {
                World world = _layers[index].World;
                if (world.ExecutionState.InputEnabled)
                {
                    foreach (Scene scene in world.Scenes)
                    {
                        if (scene.HandleInput())
                        {
                            return;
                        }
                    }
                }
            }
        }
        finally
        {
            IsDispatching = false;
        }
    }

    internal void Commit(World[] destroyed)
    {
        List<Action<List<Layer>>> requests = _requests;
        _requests = [];
        var next = new List<Layer>(_layers);
        foreach (Action<List<Layer>> request in requests)
        {
            request(next);
        }

        next.RemoveAll(layer => destroyed.Contains(layer.World));
        _layers = next;
        _worlds.Clear();
        _worlds.AddRange(next.Select(layer => layer.World));
    }

    internal void ApplyStates()
    {
        bool update = true;
        bool draw = true;
        bool input = true;
        for (int index = _layers.Count - 1; index >= 0; index--)
        {
            Layer layer = _layers[index];
            layer.World.ExecutionState = new WorldExecutionState(update, draw, input);
            update &= !layer.Policy.BlocksUpdateBelow;
            draw &= !layer.Policy.BlocksDrawBelow;
            input &= !layer.Policy.BlocksInputBelow;
        }
    }

    private static void Add(List<Layer> layers, World world, WorldLayerPolicy policy)
    {
        ObjectDisposedException.ThrowIf(world.IsDestroyed, world);
        if (layers.Any(layer => layer.World == world))
        {
            throw new InvalidOperationException("A world cannot occur twice in the stack.");
        }

        layers.Add(new Layer(world, policy));
    }

    private static void RemoveTop(List<Layer> layers)
    {
        if (layers.Count > 0)
        {
            layers.RemoveAt(layers.Count - 1);
        }
    }

    private void EnsureDispatchable()
    {
        if (_owner.IsApplying || IsDispatching)
        {
            throw new InvalidOperationException("Cannot dispatch during boundary notifications.");
        }
    }

    private sealed record Layer(World World, WorldLayerPolicy Policy);
}
