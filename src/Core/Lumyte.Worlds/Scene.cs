namespace Lumyte.Worlds;

/// <summary>A runtime scene belonging to one world.</summary>
public abstract class Scene
{
    /// <summary>Gets the owning world, or null when detached.</summary>
    public World? World { get; internal set; }

    /// <summary>Receives committed execution state changes.</summary>
    /// <param name="previous">The previous state.</param>
    /// <param name="current">The current state.</param>
    public virtual void OnExecutionStateChanged(WorldExecutionState previous, WorldExecutionState current)
    {
    }

    /// <summary>Updates using elapsed time supplied by the game loop.</summary>
    /// <param name="elapsed">The current frame duration.</param>
    public virtual void Update(TimeSpan elapsed)
    {
    }

    /// <summary>Draws the scene.</summary>
    public virtual void Draw()
    {
    }

    /// <summary>Handles input.</summary>
    /// <returns>True if input was consumed.</returns>
    public virtual bool HandleInput() => false;
}
