namespace Lumyte.Diagnostics.UI.Automation.Client;

/// <summary>A typed operator client using the existing authenticated diagnostic service.</summary>
public sealed partial class UiAutomationDiagnosticsClient
{
    private static readonly string _controlResults = "control-id:s state:s remaining-milliseconds:i revision:i next-sequence:i";
    private static readonly string _jobResults = "job-id:s state:s code:s input-may-have-been-sent:b result-artifact-id:s trace-artifact-id:s revision:i";

    private static bool Supports(DiagnosticSubsystemCatalog? catalog)
    {
        if (catalog?.Subsystem.SchemaVersion != 1 || catalog.Subsystem.Id != "ui.automation")
        {
            return false;
        }

        return Match("targets", DiagnosticPermission.Observe, DiagnosticOperationTraffic.Standard, string.Empty, "json:s", maximumResult: 32768)
            && Match("control-acquire", DiagnosticPermission.OverrideInput, DiagnosticOperationTraffic.Control, "target-id:s duration-ms:i", _controlResults)
            && Match("control-get", DiagnosticPermission.OverrideInput, DiagnosticOperationTraffic.Control, "control-id:s", _controlResults)
            && Match("control-renew", DiagnosticPermission.OverrideInput, DiagnosticOperationTraffic.Control, "control-id:s duration-ms:i", _controlResults, revision: true)
            && Match("control-release", DiagnosticPermission.OverrideInput, DiagnosticOperationTraffic.Control, "control-id:s", _controlResults, revision: true)
            && Match("action-start", DiagnosticPermission.OverrideInput, DiagnosticOperationTraffic.Standard, "target-id:s control-id:s sequence:i plan-json:s job-timeout-ms:i", _jobResults)
            && Match("observe-start", DiagnosticPermission.Observe, DiagnosticOperationTraffic.Standard, "target-id:s plan-json:s job-timeout-ms:i", _jobResults)
            && new[] { "get", "cancel", "forget" }.All(operation => Match("action-" + operation, DiagnosticPermission.OverrideInput, operation == "get" ? DiagnosticOperationTraffic.Stream : DiagnosticOperationTraffic.Control, "job-id:s", _jobResults)
                && Match("observe-" + operation, DiagnosticPermission.Observe, operation == "get" ? DiagnosticOperationTraffic.Stream : DiagnosticOperationTraffic.Control, "job-id:s", _jobResults))
            && Match("artifact-read", DiagnosticPermission.Observe, DiagnosticOperationTraffic.Standard, "job-id:s artifact-id:s offset:i count:i", "artifact-id:s content-type:s total-bytes:i sha256:s offset:i bytes:i base64:s", maximumResult: 131072);

        bool Match(string id, DiagnosticPermission permission, DiagnosticOperationTraffic traffic, string arguments, string results, bool revision = false, int? maximumResult = null)
        {
            OperationDescriptor[] matches = catalog.Operations.Where(operation => operation.Id == id).ToArray();
            if (matches.Length != 1)
            {
                return false;
            }

            OperationDescriptor operation = matches[0];
            if (operation.RequiredPermission != permission || operation.Traffic != traffic || operation.RequiresRevision != revision || !Fields(operation.Arguments, arguments) || !Fields(operation.Results, results))
            {
                return false;
            }

            foreach (DiagnosticField field in operation.Arguments)
            {
                if ((field.Id == "duration-ms" && (field.Minimum != 1000 || field.Maximum != 5000))
                    || (field.Id == "job-timeout-ms" && (field.Minimum != 100 || field.Maximum != 10000))
                    || (field.Id == "plan-json" && field.MaxLength != 32768)
                    || (field.Id == "count" && (field.Minimum != 1 || field.Maximum != 65536))
                    || (field.Id == "offset" && field.Minimum != 0))
                {
                    return false;
                }
            }

            return maximumResult is null || operation.Results.Single(field => field.Id == (id == "targets" ? "json" : "base64")).MaxLength == maximumResult;
        }
    }

    private static bool Fields(DiagnosticField[] actual, string schema)
    {
        string[] fields = schema.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return actual.Length == fields.Length && fields.All(field => actual.Count(candidate => candidate.Id == field[..^2] && candidate.Kind == (field[^1] == 's' ? DiagnosticValueKind.String : field[^1] == 'i' ? DiagnosticValueKind.Int64 : DiagnosticValueKind.Boolean)) == 1);
    }
}
