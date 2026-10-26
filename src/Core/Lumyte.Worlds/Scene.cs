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

    /// <summary>Runs one fixed simulation step selected by the game loop.</summary>
    /// <param name="step">The positive fixed step duration.</param>
    public virtual void FixedUpdate(TimeSpan step)
    {
    }

    /// <summary>Updates using elapsed time supplied by the game loop.</summary>
    /// <param name="elapsed">The current frame duration.</param>
    public virtual void Update(TimeSpan elapsed)
    {
    }

    /// <summary>Runs after every enabled scene has finished its ordinary update.</summary>
    /// <param name="elapsed">The current frame duration.</param>
    public virtual void LateUpdate(TimeSpan elapsed)
    {
    }

    /// <summary>Registers scene rendering without submitting or presenting GPU work.</summary>
    /// <param name="context">The borrowed context, valid only during this call.</param>
    public virtual void Draw(DrawContext context)
    {
    }

    /// <summary>Handles input.</summary>
    /// <returns>True if input was consumed.</returns>
    public virtual bool HandleInput() => false;
}
