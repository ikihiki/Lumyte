using System.Text;
using System.Text.Json;
using Lumyte.Diagnostics;

namespace Lumyte.UI.Application.Navigation.Diagnostics;

/// <summary>Observes committed navigation metadata on the existing UI diagnostic owner.</summary>
public sealed partial class UiNavigationDiagnostics
{
    private readonly UiNavigationRegistry _registry;

    internal UiNavigationDiagnostics(UiNavigationRegistry registry)
    {
        _registry = registry;
    }

    [DiagnosticOperation(DiagnosticPermission.Observe, Id = "state")]
    private DiagnosticResult<UiNavigationStateReceipt> State([DiagnosticArgument(MaxLength = 36)] string windowContextId)
    {
        Guid? id = null;
        if (windowContextId.Length != 0)
        {
            if (!Guid.TryParseExact(windowContextId, "D", out Guid parsed))
            {
                return DiagnosticResult<UiNavigationStateReceipt>.Reject("invalid-argument", "Use a window context Guid in D format.");
            }

            id = parsed;
        }

        IReadOnlyList<UiNavigationWindowSnapshot> windows = _registry.Capture(id);
        if (id is not null && windows.Count == 0)
        {
            return DiagnosticResult<UiNavigationStateReceipt>.Reject("not-found", "The navigation window is unavailable.");
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteStartArray("windows");
            foreach (UiNavigationWindowSnapshot window in windows)
            {
                writer.WriteStartObject();
                writer.WriteString("windowContextId", window.WindowContextId);
                writer.WriteString("windowManagerId", window.Window.ManagerId);
                writer.WriteNumber("windowValue", window.Window.Value);
                writer.WriteNumber("revision", window.Revision);
                if (window.Current is not { } current)
                {
                    writer.WriteNull("current");
                }
                else
                {
                    writer.WriteStartObject("current");
                    writer.WriteString("navigatorId", current.NavigatorId);
                    writer.WriteString("entryId", current.EntryId);
                    writer.WriteString("activationId", current.ActivationId);
                    writer.WriteString("route", current.Route);
                    writer.WriteString("surfaceId", current.SurfaceId);
                    if (current.TargetId is { } target)
                    {
                        writer.WriteString("targetId", target);
                    }
                    else
                    {
                        writer.WriteNull("targetId");
                    }

                    writer.WriteEndObject();
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        string json = Encoding.UTF8.GetString(stream.GetBuffer(), 0, checked((int)stream.Length));
        return json.Length > 32768 ? DiagnosticResult<UiNavigationStateReceipt>.Reject("result-too-large", "Navigation metadata exceeds 32 KiB.") : DiagnosticResult<UiNavigationStateReceipt>.Success(new(json));
    }
}
