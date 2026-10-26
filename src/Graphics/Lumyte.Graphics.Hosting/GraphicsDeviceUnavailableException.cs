namespace Lumyte.Graphics.Hosting;

/// <summary>Reports a service that has no usable device.</summary>
/// <param name="state">The unavailable state snapshot.</param>
public sealed class GraphicsDeviceUnavailableException(GraphicsDeviceState state) : InvalidOperationException(state.Name.Length == 0
    ? $"Default graphics device is {state.Status}."
    : $"Graphics device '{state.Name}' is {state.Status}.")
{
    /// <summary>Gets the unavailable state.</summary>
    public GraphicsDeviceState State { get; } = state;
}
