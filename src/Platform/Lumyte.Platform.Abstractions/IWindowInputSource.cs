using Lumyte.Input.Abstractions;
using Lumyte.Windowing.Abstractions;

namespace Lumyte.Platform.Abstractions;

/// <summary>Resolves live native presentation windows in an ordered input stream.</summary>
public interface IWindowInputSource : IOrderedInputSource
{
    /// <summary>Resolves a live borrowed window without using its OS handle as identity.</summary>
    /// <param name="windowId">The borrowed presentation window.</param>
    /// <param name="inputWindowId">Its input connection identity.</param>
    /// <returns>Whether the window belongs to this live source.</returns>
    bool TryGetInputWindowId(WindowId windowId, out InputWindowId inputWindowId);
}
