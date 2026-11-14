namespace Lumyte.Graphics.Shared;

/// <summary>Backend registration identity diagnostics, never invoked while validation is disabled.</summary>
public interface IValidatedShaderReference
{
    /// <summary>Rejects released or replaced registrations.</summary>
    void ValidateRegistration();
}
