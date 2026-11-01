namespace Lumyte.Diagnostics.UI.Automation;

/// <summary>A detached scalar operation receipt.</summary>
/// <param name="ArtifactId">The ArtifactId value.</param>
/// <param name="ContentType">The ContentType value.</param>
/// <param name="TotalBytes">The TotalBytes value.</param>
/// <param name="Sha256">The Sha256 value.</param>
/// <param name="Offset">The Offset value.</param>
/// <param name="Bytes">The Bytes value.</param>
/// <param name="Base64">The Base64 value.</param>
public sealed record UiAutomationArtifactReceipt(string ArtifactId, string ContentType, long TotalBytes, string Sha256, long Offset, long Bytes, [property: DiagnosticMember(MaxLength = 131072)] string Base64);
