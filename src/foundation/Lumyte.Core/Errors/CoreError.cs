namespace Lumyte.Core.Errors;

/// <summary>A stable machine-readable error code paired with a human-readable message.</summary>
public sealed record CoreError
{
    public string Code { get; }

    public string Message { get; }

    public CoreError(string code, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Code = code;
        Message = message;
    }
}
