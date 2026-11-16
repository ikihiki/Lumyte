using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Validation;

/// <summary>Bridges a logical reference to its native writer.</summary>
internal interface IValidationReference : IValidatedShaderReference, IShaderReference
{
    /// <summary>Gets the owning validation domain.</summary>
    ValidationDevice Device { get; }

    /// <summary>Writes the native registration once.</summary>
    /// <typeparam name="TWriter">The native writer type.</typeparam>
    /// <param name="writer">The destination writer.</param>
    void WriteNative<TWriter>(ref TWriter writer)
        where TWriter : IShaderValueWriter, allows ref struct;
}
