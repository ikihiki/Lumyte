namespace Lumyte.Graphics.Abstractions;

/// <summary>Provides immutable capabilities of a backend-owned graphics device.</summary>
public interface IGraphicDevice
{
    /// <summary>Gets the immutable device capabilities.</summary>
    public DeviceCaps Caps { get; }
}
