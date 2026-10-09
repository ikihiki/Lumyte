namespace Lumyte.Diagnostics.Server;

internal sealed record DiagnosticUiState(DiagnosticUiSession[] Sessions, Guid? SelectedSessionId, DiagnosticEvent[] Events);
