namespace Lumyte.Execution.Benchmark;

/// <summary>Finite receiver-owned observable storage.</summary>
public sealed class SenderState
{
    /// <summary>Gets or sets the last command value.</summary>
    public int Value { get; set; }

    /// <summary>Performs the identical receiver mutation used by every sender control.</summary>
    /// <param name="value">The new receiver value.</param>
    /// <returns>The command result.</returns>
    public int Play(int value)
    {
        Value = value;
        return value * 2;
    }
}
