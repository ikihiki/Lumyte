namespace Lumyte.Graphics.Drawing2D;

/// <summary>Enumerates immutable allocation owners without charging shared resources twice.</summary>
internal interface IRetainedMemory2D
{
    /// <summary>Adds private and shared conservative byte charges, keyed by owner identity.</summary>
    /// <param name="owners">The identity-keyed accounting destination.</param>
    void AccountMemory(Dictionary<object, long> owners);
}
