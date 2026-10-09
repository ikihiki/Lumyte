namespace Lumyte.StateMachines.Sample;

internal sealed class ConnectionInput
{
    /// <summary>Gets or sets a value indicating whether configured.</summary>
    public bool Configured { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether ready.</summary>
    public bool Ready { get; set; }

    /// <summary>Gets the log.</summary>
    public List<string> Log { get; } = [];
}
