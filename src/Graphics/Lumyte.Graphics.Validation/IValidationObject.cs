namespace Lumyte.Graphics.Validation;

/// <summary>Identifies a live object in one validation domain.</summary>
internal interface IValidationObject
{
    /// <summary>Gets the owning validation domain.</summary>
    ValidationDevice Device { get; }

    /// <summary>Gets the borrowed native identity.</summary>
    object RawObject { get; }

    /// <summary>Gets a value indicating whether gets whether this wrapper has been released.</summary>
    bool IsDisposed { get; }

    /// <summary>Rejects released resources or domains.</summary>
    void ValidateAlive();
}
