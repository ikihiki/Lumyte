namespace Lumyte.Diagnostics.Transport;

/// <summary>A transport failure exposed without HTTP or gRPC types.</summary>
/// <param name="message">The failure description.</param>
/// <param name="innerException">The physical transport error.</param>
public sealed class DiagnosticTransportException(string message, Exception? innerException = null) : Exception(message, innerException)
{
}
