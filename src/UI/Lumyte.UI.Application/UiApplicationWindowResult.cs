using Lumyte.UI.Windowing;

namespace Lumyte.UI.Application;

/// <summary>Reports one concrete window instance opening or finishing.</summary>
/// <param name="Key">The declaration key.</param>
/// <param name="InstanceId">The non-reusable instance identity.</param>
/// <param name="Code">A stable result code.</param>
/// <param name="Error">The original failure, when present.</param>
/// <param name="Presentation">The actual presentation, when prepared.</param>
public sealed record UiApplicationWindowResult(string Key, Guid InstanceId, string Code, Exception? Error = null, UiWindowPresentation? Presentation = null);
