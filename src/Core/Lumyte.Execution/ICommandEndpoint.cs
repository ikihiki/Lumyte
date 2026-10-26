namespace Lumyte.Execution;

/// <summary>An endpoint controlled by atomic preparation and shutdown.</summary>
internal interface ICommandEndpoint
{
    /// <summary>Gets a value indicating whether this endpoint has ever been connected.</summary>
    bool HasConnected { get; }

    /// <summary>Publishes a typed definition after successful validation.</summary>
    /// <param name="work">The matching closed generic definition.</param>
    void Connect(WorkDefinition work);

    /// <summary>Clears runtime references without permitting reconnection.</summary>
    void Close();
}
