using Lumyte.Diagnostics;

namespace Lumyte.Input.Diagnostics;

/// <summary>Publishes the scoped Input inspection snapshot.</summary>
/// <param name="inspection">The shared Input and Action observer.</param>
public sealed partial class InputDiagnostics(InputInspectionSession inspection)
{
    private readonly InputInspectionSession _inspection = inspection;

    [DiagnosticOperation(DiagnosticPermission.Observe, Id = "get-snapshot", DisplayName = "Input snapshot", Traffic = DiagnosticOperationTraffic.Stream)]
    private DiagnosticResult<InputSnapshotReceipt> GetSnapshot()
    {
        string json = _inspection.CreateSnapshot();
        return json.Length <= 1048576
            ? DiagnosticResult<InputSnapshotReceipt>.Success(new(json), _inspection.Revision)
            : DiagnosticResult<InputSnapshotReceipt>.Reject("snapshot-too-large", "The Input snapshot exceeds one mebibyte.");
    }
}
