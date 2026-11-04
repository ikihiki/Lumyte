namespace Lumyte.Platform.TextInput.Abstractions;

/// <summary>Defines itext input quiescence.</summary>
public interface ITextInputQuiescence
{
    /// <summary>Gets a value indicating whether this option is enabled.</summary>
    bool IsInputIdle { get; }
}
