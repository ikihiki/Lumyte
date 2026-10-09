namespace Lumyte.Diagnostics;

/// <summary>Generated or explicit operation registration.</summary>
public interface IDiagnosticContributor
{
    /// <summary>Declares operations during activation.</summary>
    /// <param name="builder">The builder argument.</param>
    void Configure(DiagnosticBuilder builder);
}
