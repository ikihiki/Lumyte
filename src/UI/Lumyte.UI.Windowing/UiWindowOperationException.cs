namespace Lumyte.UI.Windowing;

/// <summary>A presentation failure with a stable machine-readable code.</summary>
/// <param name="code">The stable failure code.</param>
/// <param name="innerException">The original failure.</param>
public sealed class UiWindowOperationException(string code, Exception? innerException = null) : Exception(code, innerException)
{
    /// <summary>Gets the stable failure code.</summary>
    public string Code { get; } = code;
}
