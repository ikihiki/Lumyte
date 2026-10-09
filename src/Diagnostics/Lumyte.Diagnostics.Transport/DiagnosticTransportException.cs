namespace Lumyte.Diagnostics.Transport;

/// <summary>A transport failure exposed without HTTP or gRPC types.</summary>
public sealed class DiagnosticTransportException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="DiagnosticTransportException"/> class.</summary>
    /// <param name="message">The failure description.</param>
    /// <param name="innerException">The physical transport error.</param>
    public DiagnosticTransportException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
