using Lumyte.Diagnostics.Transport;
using Microsoft.AspNetCore.Components;

namespace Lumyte.Diagnostics.Server;

/// <summary>The selected game and shell callbacks cascaded to extension components.</summary>
/// <param name="Resource">The selected game snapshot; absent when disconnected.</param>
/// <param name="Events">The bounded events belonging to this game.</param>
/// <param name="Execute">Executes through the authenticated shell without automatic retries.</param>
public sealed record DiagnosticPageContext(SessionSnapshot? Resource, IReadOnlyList<DiagnosticEvent> Events, EventCallback<OperationInvocation> Execute);
