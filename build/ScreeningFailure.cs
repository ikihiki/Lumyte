namespace Lumyte.Build;

internal sealed class ScreeningFailure(string status, string message) : Exception(message)
{
    public string Status { get; } = status;
}
