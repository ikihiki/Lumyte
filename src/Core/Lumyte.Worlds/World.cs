namespace Lumyte.Worlds;

/// <summary>A runtime world whose lifetime is independent of stack membership.</summary>
public sealed class World
{
    private readonly List<Scene> _scenes = [];

    internal World(WorldManager owner)
    {
        Owner = owner;
        Scenes = _scenes.AsReadOnly();
    }

    /// <summary>Gets the committed execution state.</summary>
    public WorldExecutionState ExecutionState { get; internal set; }

    /// <summary>Gets a value indicating whether this world has been destroyed.</summary>
    public bool IsDestroyed { get; private set; }

    /// <summary>Gets the attached scenes.</summary>
    public IReadOnlyList<Scene> Scenes { get; }

    internal WorldManager Owner { get; }

    /// <summary>Adds a detached scene while the world is inactive.</summary>
    /// <param name="scene">The scene to attach.</param>
    public void AddScene(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        EnsureEditable();
        if (scene.World is not null)
        {
            throw new InvalidOperationException("The scene already belongs to a world.");
        }

        _scenes.Add(scene);
        scene.World = this;
    }

    /// <summary>Removes a scene while the world is inactive.</summary>
    /// <param name="scene">The scene to detach.</param>
    /// <returns>Whether the scene was attached.</returns>
    public bool RemoveScene(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        EnsureEditable();
        if (!_scenes.Remove(scene))
        {
            return false;
        }

        scene.World = null;
        return true;
    }

    internal void Notify(WorldExecutionState previous, List<Exception> errors)
    {
        if (previous == ExecutionState)
        {
            return;
        }

        foreach (Scene scene in _scenes)
        {
            try
            {
                scene.OnExecutionStateChanged(previous, ExecutionState);
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
        }
    }

    internal void Destroy()
    {
        foreach (Scene scene in _scenes)
        {
            scene.World = null;
        }

        _scenes.Clear();
        IsDestroyed = true;
    }

    private void EnsureEditable()
    {
        ObjectDisposedException.ThrowIf(IsDestroyed, this);
        if (ExecutionState != default || Owner.IsApplying)
        {
            throw new InvalidOperationException("Scenes can only be edited in an inactive world outside a frame boundary.");
        }
    }
}
