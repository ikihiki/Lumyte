namespace Lumyte.Graphics.Abstractions;

/// <summary>Confirms retirement through a backend's trusted lifecycle service.</summary>
public interface IGraphicsDeviceRetirementAuthority
{
    /// <summary>Gets the borrowed device.</summary>
    IGraphicDevice Device { get; }

    /// <summary>Gets the trusted backend generation.</summary>
    GraphicsDeviceGeneration Generation { get; }

    /// <summary>Waits for GPU use to end and safe child resource reclamation.</summary>
    /// <param name="cancellationToken">Cancels only this wait.</param>
    /// <returns>A trusted proof; arbitrary implementations cannot issue one.</returns>
    ValueTask<GraphicsDeviceRetirementProof> WaitForRetirementAsync(CancellationToken cancellationToken = default);
}
