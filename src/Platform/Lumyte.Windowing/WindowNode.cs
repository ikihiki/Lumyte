using System.Numerics;
using Lumyte.Windowing.Abstractions;

namespace Lumyte.Windowing;

/// <summary>Represents one native window or hosted region managed by a <see cref="WindowSystem"/>.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "SA1202", Justification = "Construction and identity are internal implementation details.")]
public sealed class WindowNode
{
    private readonly WindowSystem _system;

    internal WindowNode(WindowSystem system, WindowNodeId id)
    {
        _system = system;
        Id = id;
    }

    internal WindowNodeId Id { get; }

    /// <summary>Gets the current immutable state.</summary>
    public WindowNodeState State => _system.GetState(Id);

    /// <summary>Gets the original native window.</summary>
    /// <exception cref="InvalidOperationException">This node is a hosted region.</exception>
    public IWindow NativeWindow => _system.GetNativeWindow(Id);

    /// <summary>Gets hosted regions in back-to-front order for this native presentation target.</summary>
    /// <returns>The hosted regions.</returns>
    public IReadOnlyList<WindowNode> GetHostedOrder() => _system.GetHostedOrder(Id);

    /// <summary>Finds the frontmost visible region at a point in native client coordinates.</summary>
    /// <param name="point">The logical client point.</param>
    /// <returns>The frontmost target, or null.</returns>
    public WindowNode? HitTest(Vector2 point) => _system.HitTest(Id, point);

    /// <summary>Requests visibility.</summary>
    /// <returns>The request identifier.</returns>
    public WindowNodeRequestId Show() => _system.Show(Id);

    /// <summary>Requests that this node become hidden.</summary>
    /// <returns>The request identifier.</returns>
    public WindowNodeRequestId Hide() => _system.Hide(Id);

    /// <summary>Requests activation.</summary>
    /// <returns>The request identifier.</returns>
    public WindowNodeRequestId Activate() => _system.Activate(Id);

    /// <summary>Requests that this node move to the front of its layer.</summary>
    /// <returns>The request identifier.</returns>
    public WindowNodeRequestId BringToFront() => _system.BringToFront(Id);

    /// <summary>Requests anchor placement.</summary>
    /// <param name="placement">The requested placement.</param>
    /// <returns>The request identifier.</returns>
    public WindowNodeRequestId Place(WindowPlacement placement) => _system.Place(Id, placement);

    /// <summary>Changes hosted bounds.</summary>
    /// <param name="bounds">The bounds in parent coordinates.</param>
    /// <returns>The request identifier.</returns>
    public WindowNodeRequestId SetHostedBounds(WindowRect bounds) => _system.SetHostedBounds(Id, bounds);

    /// <summary>Closes this owned subtree.</summary>
    /// <returns>The request identifier.</returns>
    public WindowNodeRequestId Close() => _system.Close(Id);

    /// <summary>Detaches this borrowed native subtree.</summary>
    /// <returns>The request identifier.</returns>
    public WindowNodeRequestId Detach() => _system.Detach(Id);
}
