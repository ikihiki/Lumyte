namespace Lumyte.Diagnostics;

/// <summary>A typed result before generated scalar encoding.</summary>
/// <typeparam name="T">The output shape.</typeparam>
public sealed class DiagnosticResult<T>
    where T : notnull
{
    private readonly T? _value;
    private readonly DiagnosticOperationResult? _failure;

    private DiagnosticResult(T? value, long? revision, DiagnosticOperationResult? failure)
    {
        _value = value;
        Revision = revision;
        _failure = failure;
    }

    /// <summary>Gets a value indicating whether the operation succeeded.</summary>
    public bool IsSuccess => _failure is null;

    /// <summary>Gets the successful value.</summary>
    public T Value => IsSuccess ? _value! : throw new InvalidOperationException("No successful value.");

    /// <summary>Gets the revision.</summary>
    public long? Revision { get; }

    /// <summary>Returns success.</summary>
    /// <param name="value">The value argument.</param>
    /// <param name="revision">The revision argument.</param>
    /// <returns>The computed result.</returns>
    public static DiagnosticResult<T> Success(T value, long? revision = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(value, revision, null);
    }

    /// <summary>Returns rejection.</summary>
    /// <param name="code">The code argument.</param>
    /// <param name="message">The message argument.</param>
    /// <returns>The computed result.</returns>
    public static DiagnosticResult<T> Reject(string code, string message) => new(default, null, DiagnosticOperationResult.Reject(code, message));

    /// <summary>Returns a conflict.</summary>
    /// <param name="currentRevision">The currentRevision argument.</param>
    /// <returns>The computed result.</returns>
    public static DiagnosticResult<T> Conflict(long currentRevision) => new(default, null, DiagnosticOperationResult.Conflict(currentRevision));

    /// <summary>Encodes only the successful value.</summary>
    /// <param name="encode">The encode argument.</param>
    /// <returns>The computed result.</returns>
    public DiagnosticOperationResult ToOperationResult(Func<T, IReadOnlyDictionary<string, DiagnosticValue>> encode)
        => _failure ?? DiagnosticOperationResult.Success(encode(Value), Revision);
}
