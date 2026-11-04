namespace Lumyte.UI.Automation;

/// <summary>Overrides an operation's single shared deadline.</summary>
public record UiWaitOptions
{
    /// <summary>Gets the positive timeout, or null to use the page default.</summary>
    public TimeSpan? Timeout { get; init; }
}
