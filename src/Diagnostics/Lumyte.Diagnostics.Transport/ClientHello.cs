namespace Lumyte.Diagnostics.Transport;

/// <summary>The game identity, protocol version and initial catalogs.</summary>
/// <param name="InstanceId">The InstanceId value.</param>
/// <param name="ProtocolVersion">The ProtocolVersion value.</param>
/// <param name="Catalog">The Catalog value.</param>
public sealed record ClientHello(Guid InstanceId, int ProtocolVersion, DiagnosticSubsystemCatalog[] Catalog);
