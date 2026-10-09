namespace Lumyte.Diagnostics;

/// <summary>A terminal operation result.</summary>
/// <param name="Status">The Status argument.</param>
/// <param name="Values">The Values argument.</param>
/// <param name="Revision">The Revision argument.</param>
/// <param name="Code">The Code argument.</param>
/// <param name="Message">The Message argument.</param>
public sealed record DiagnosticOperationResult(
    string Status, IReadOnlyDictionary<string, DiagnosticValue>? Values = null, long? Revision = null, string? Code = null, string? Message = null)
{
    /// <summary>Returns success.</summary>
    /// <param name="values">The values argument.</param>
    /// <param name="revision">The revision argument.</param>
    /// <returns>The computed result.</returns>
    public static DiagnosticOperationResult Success(IReadOnlyDictionary<string, DiagnosticValue> values, long? revision = null)
        => new("success", values, revision);

    /// <summary>Returns rejection.</summary>
    /// <param name="code">The code argument.</param>
    /// <param name="message">The message argument.</param>
    /// <returns>The computed result.</returns>
    public static DiagnosticOperationResult Reject(string code, string message) => new("rejected", Code: code, Message: message);

    /// <summary>Returns a conflict.</summary>
    /// <param name="currentRevision">The currentRevision argument.</param>
    /// <returns>The computed result.</returns>
    public static DiagnosticOperationResult Conflict(long currentRevision) => new("conflict", Revision: currentRevision);
}
