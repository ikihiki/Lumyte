namespace Lumyte.Graphics.Browser;

/// <summary>Connects logical shader-data registrations to the raw allocation used by copies and barriers.</summary>
internal interface IValidatedShaderDataStorage
{
    /// <summary>Gets the native buffer allocation used for range diagnostics.</summary>
    object ValidationStorage { get; }
}
