namespace Lumyte.UI.Application;

/// <summary>A stable application failure code with its original cause.</summary>
/// <param name="code">The stable failure code.</param>
/// <param name="innerException">The underlying failure.</param>
public sealed class UiApplicationException(string code, Exception? innerException = null) : Exception(code, innerException)
{
    /// <summary>Gets the stable failure code.</summary>
    public string Code { get; } = code;
}
