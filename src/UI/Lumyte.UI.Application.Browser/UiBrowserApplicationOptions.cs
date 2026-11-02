namespace Lumyte.UI.Application.Browser;

/// <summary>Identifies an existing canvas and published browser modules.</summary>
public sealed record UiBrowserApplicationOptions
{
    /// <summary>Gets the borrowed canvas element ID.</summary>
    public string CanvasId { get; init; } = "lumyte";

    /// <summary>Gets the application bridge module URL.</summary>
    public string ModulePath { get; init; } = "./lumyte-ui-application.js";
}
