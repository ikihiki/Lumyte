namespace Lumyte.Diagnostics.Transport.MagicOnion;

/// <summary>Maps explicit numeric-key wire data without changing subsystem declarations.</summary>
public static class WireMapper
{
    /// <summary>Maps a game hello.</summary>
    /// <param name="value">The common value.</param>
    /// <returns>The wire value.</returns>
    public static WireHello ToWire(ClientHello value) => new()
    {
        InstanceId = value.InstanceId.ToString("D"),
        ProtocolVersion = value.ProtocolVersion,
        Catalog = value.Catalog.Select(subsystem => new WireSubsystem
        {
            Id = subsystem.Subsystem.Id,
            DisplayName = subsystem.Subsystem.DisplayName,
            SchemaVersion = subsystem.Subsystem.SchemaVersion,
            Operations = subsystem.Operations.Select(operation => new WireOperation
            {
                Id = operation.Id,
                DisplayName = operation.DisplayName,
                Permission = (int)operation.RequiredPermission,
                Arguments = operation.Arguments.Select(Field).ToArray(),
                Results = operation.Results.Select(Field).ToArray(),
                RequiresRevision = operation.RequiresRevision,
            }).ToArray(),
        }).ToArray(),
    };

    /// <summary>Maps a game hello.</summary>
    /// <param name="value">The wire value.</param>
    /// <returns>The common value.</returns>
    public static ClientHello FromWire(WireHello value) => new(Guid.Parse(value.InstanceId), value.ProtocolVersion, value.Catalog.Select(subsystem => new DiagnosticSubsystemCatalog(new(subsystem.Id, subsystem.DisplayName, subsystem.SchemaVersion), subsystem.Operations.Select(operation => new OperationDescriptor(operation.Id, operation.DisplayName, (DiagnosticPermission)operation.Permission, operation.Arguments.Select(field => new DiagnosticField(field.Id, (DiagnosticValueKind)field.Kind, field.Minimum, field.Maximum, field.MaxLength)).ToArray(), operation.Results.Select(field => new DiagnosticField(field.Id, (DiagnosticValueKind)field.Kind, field.Minimum, field.Maximum, field.MaxLength)).ToArray(), operation.RequiresRevision)).ToArray())).ToArray());

    /// <summary>Maps a session.</summary>
    /// <param name="value">The common value.</param>
    /// <returns>The wire value.</returns>
    public static WireWelcome ToWire(SessionWelcome value) => new() { SessionId = value.SessionId.ToString("D"), SessionSecret = value.SessionSecret, Permissions = value.Permissions.Select(permission => (int)permission).ToArray() };

    /// <summary>Maps a session.</summary>
    /// <param name="value">The wire value.</param>
    /// <returns>The common value.</returns>
    public static SessionWelcome FromWire(WireWelcome value) => new(Guid.Parse(value.SessionId), value.SessionSecret, value.Permissions.Select(permission => (DiagnosticPermission)permission).ToArray());

    /// <summary>Maps a command.</summary>
    /// <param name="value">The common value.</param>
    /// <returns>The wire value.</returns>
    public static WireCommand ToWire(DiagnosticCommand value) => new() { RequestId = value.RequestId.ToString("D"), SubsystemId = value.SubsystemId, OperationId = value.OperationId, ActorId = value.ActorId, ExpectedRevision = value.ExpectedRevision, ExpiresUnixMilliseconds = value.ExpiresUnixMilliseconds, Arguments = Values(value.Arguments) };

    /// <summary>Maps a command.</summary>
    /// <param name="value">The wire value.</param>
    /// <returns>The common value.</returns>
    public static DiagnosticCommand FromWire(WireCommand value) => new(Guid.Parse(value.RequestId), value.SubsystemId, value.OperationId, value.ActorId, value.ExpectedRevision, value.ExpiresUnixMilliseconds, Values(value.Arguments));

    /// <summary>Maps a receipt.</summary>
    /// <param name="value">The common value.</param>
    /// <returns>The wire value.</returns>
    public static WireReceipt ToWire(PublishReceipt value) => new() { MessageId = value.MessageId.ToString("D"), Accepted = value.Accepted, ErrorCode = value.ErrorCode };

    /// <summary>Maps a receipt.</summary>
    /// <param name="value">The wire value.</param>
    /// <returns>The common value.</returns>
    public static PublishReceipt FromWire(WireReceipt value) => new(Guid.Parse(value.MessageId), value.Accepted, value.ErrorCode);

    private static WireField Field(DiagnosticField value) => new() { Id = value.Id, Kind = (int)value.Kind, Minimum = value.Minimum, Maximum = value.Maximum, MaxLength = value.MaxLength };

    private static WireValue Scalar(DiagnosticValue value) => new() { Kind = (int)value.Kind, Boolean = value.Boolean, Integer = value.Int64, Number = value.Double, Text = value.String };

    private static DiagnosticValue Scalar(WireValue value) => new((DiagnosticValueKind)value.Kind, value.Boolean, value.Integer, value.Number, value.Text);

    private static Dictionary<string, WireValue> Values(IReadOnlyDictionary<string, DiagnosticValue> values) => values.ToDictionary(pair => pair.Key, pair => Scalar(pair.Value), StringComparer.Ordinal);

    private static Dictionary<string, DiagnosticValue> Values(Dictionary<string, WireValue> values) => values.ToDictionary(pair => pair.Key, pair => Scalar(pair.Value), StringComparer.Ordinal);
}
