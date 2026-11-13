namespace Lumyte.Graphics.Abstractions;

/// <summary>Synchronously emits references without retaining a wire view or cursor.</summary>
public interface IShaderReferenceEmitter
{
    /// <summary>Emits references in physically ascending, non-overlapping wire-offset order.</summary>
    /// <typeparam name="TCursor">The synchronous reference consumer.</typeparam>
    /// <param name="cursor">The reference consumer.</param>
    void Emit<TCursor>(ref TCursor cursor)
        where TCursor : IShaderReferenceCursor, allows ref struct;
}
