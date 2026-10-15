namespace Lumyte.Graphics.Abstractions;

/// <summary>Retains a prepared graphics program selected by its package's stable ABI identifier.</summary>
/// <remarks>External providers resolve preloaded resources without I/O or recovery waits. Freeze the publication view for one drawing preparation. Providers may share native objects, but must retain the exact requested device until each lease is returned.</remarks>
public interface IGraphicsProgramProvider
{
    /// <summary>Acquires one owned lease over a compatible program on the exact device.</summary>
    /// <param name="device">The borrowed physical device for this preparation.</param>
    /// <param name="programId">The package-defined identifier, including its ABI version.</param>
    /// <returns>A fresh lease; the caller releases it after GPU use and command-plan disposal.</returns>
    GraphicsProgramLease Acquire(IGraphicDevice device, string programId);
}
