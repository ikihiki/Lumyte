namespace Lumyte.Graphics.Abstractions;

/// <summary>Synchronously emits references without retaining a wire view or cursor.</summary>
public interface IShaderReferenceEmitter
{
    /// <summary>Emits references in wire-offset order or original declaration order.</summary>
    /// <typeparam name="TCursor">The synchronous reference consumer.</typeparam>
    /// <param name="cursor">The reference consumer.</param>
    /// <param name="ordered">Whether ascending, non-overlapping wire offsets are required.</param>
    void Emit<TCursor>(ref TCursor cursor, bool ordered)
        where TCursor : IShaderReferenceCursor, allows ref struct;
}
