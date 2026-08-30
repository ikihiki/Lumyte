namespace Lumyte.Resources;

/// <summary>Reports a failure encountered while resolving or loading a resource.</summary>
public abstract class ResourceException : Exception
{
    protected ResourceException(
        string message,
        Exception? innerException = null)
        : base(RequireMessage(message), innerException)
    {
    }

    private static string RequireMessage(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return message;
    }
}
