namespace Lumyte.Diagnostics;

/// <summary>The schema and access requirement of an operation.</summary>
/// <param name="Id">The Id argument.</param>
/// <param name="DisplayName">The DisplayName argument.</param>
/// <param name="RequiredPermission">The RequiredPermission argument.</param>
/// <param name="Arguments">The Arguments argument.</param>
/// <param name="Results">The Results argument.</param>
/// <param name="RequiresRevision">The RequiresRevision argument.</param>
public sealed record OperationDescriptor(
    string Id, string DisplayName, DiagnosticPermission RequiredPermission, DiagnosticField[] Arguments, DiagnosticField[] Results, bool RequiresRevision = false);
