namespace Lumyte.Graphics.Abstractions;

/// <summary>Provides a non-owning recording scope.</summary>
public interface IRenderEncoder
{
    /// <summary>Ends this pass once without submitting or waiting.</summary>
    void End();
}
