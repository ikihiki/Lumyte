namespace Lumyte.Graphics.Pipelines;

/// <summary>Exposes device dependency metadata only to trusted graphics preflight.</summary>
internal interface IGraphicsDeviceValue
{
    /// <summary>Gets the captured device value or null for an invalid default.</summary>
    DeviceValue? Value { get; }
}
