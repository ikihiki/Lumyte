namespace Lumyte.Diagnostics;

/// <summary>Explicitly exposes one synchronous operation.</summary>
/// <param name="permission">The permission argument.</param>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class DiagnosticOperationAttribute(DiagnosticPermission permission = DiagnosticPermission.Edit) : Attribute
{
    /// <summary>Gets or sets an optional stable wire name.</summary>
    public string? Id { get; set; }

    /// <summary>Gets the permission.</summary>
    public DiagnosticPermission Permission { get; } = permission;

    /// <summary>Gets or sets the display name.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Gets or sets a value indicating whether a revision must be supplied.</summary>
    public bool RequiresRevision { get; set; }
}
