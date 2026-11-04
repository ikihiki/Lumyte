using Lumyte.TextEditing;

namespace Lumyte.Platform.TextInput.Abstractions;

/// <summary>Defines itext input session.</summary>
public interface ITextInputSession : IDisposable
{
    /// <summary>Gets the status.</summary>
    TextInputSessionStatus Status { get; }

    /// <summary>Gets a value indicating whether this option is enabled.</summary>
    bool IsReleased { get; }

    /// <summary>Update Geometry.</summary>
    /// <param name = "geometry">The geometry.</param>
    void UpdateGeometry(TextInputGeometry geometry);

    /// <summary>Request End.</summary>
    /// <param name = "behavior">The behavior.</param>
    void RequestEnd(TextCompositionEnd behavior);
}
