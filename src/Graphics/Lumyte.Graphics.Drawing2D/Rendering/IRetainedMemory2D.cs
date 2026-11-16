namespace Lumyte.Graphics.Drawing2D;

/// <summary>Enumerates immutable allocation owners without charging shared resources twice.</summary>
internal interface IRetainedMemory2D
{
    /// <summary>Gets a value indicating whether owner identities and private charges are fixed after preparation.</summary>
    /// <remarks>Each shared identity must have one consistent charge across assets.
    /// Shared DrawingArgumentPlan2D charges are independently revalidated by the ledger.</remarks>
    bool HasStableOwners => false;

    /// <summary>Adds private and shared conservative byte charges, keyed by owner identity.</summary>
    /// <param name="owners">The identity-keyed accounting destination.</param>
    void AccountMemory(Dictionary<object, long> owners);
}
