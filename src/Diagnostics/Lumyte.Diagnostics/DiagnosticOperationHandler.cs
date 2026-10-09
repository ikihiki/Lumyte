namespace Lumyte.Diagnostics;

/// <summary>A validated synchronous operation.</summary>
/// <param name="context">The context argument.</param>
/// <param name="arguments">The arguments argument.</param>
/// <returns>The computed result.</returns>
public delegate DiagnosticOperationResult DiagnosticOperationHandler(DiagnosticOperationContext context, DiagnosticArguments arguments);
