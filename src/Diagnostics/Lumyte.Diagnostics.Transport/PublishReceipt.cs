namespace Lumyte.Diagnostics.Transport;

/// <summary>Acknowledges server acceptance; it does not imply persistence.</summary>
/// <param name="MessageId">The MessageId value.</param>
/// <param name="Accepted">The Accepted value.</param>
/// <param name="ErrorCode">The ErrorCode value.</param>
public sealed record PublishReceipt(Guid MessageId, bool Accepted, string? ErrorCode = null);
