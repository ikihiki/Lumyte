namespace Lumyte.Platform.TextInput.Abstractions;

/// <summary>Temporarily excludes physical editing while automation owns the input scope.</summary>
public interface ITextInputInputGate
{
    /// <summary>Gets a value indicating whether physical input and backend work are idle, allowing a current composition.</summary>
    bool IsCandidateInputIdle { get; }

    /// <summary>Acquires an owner-thread gate. Existing composition updates can optionally finish.</summary>
    /// <param name = "allowComposition">Whether the existing IME composition may continue.</param>
    /// <returns>A token released on the service owner.</returns>
    IDisposable AcquireInputGate(bool allowComposition = false);
}
