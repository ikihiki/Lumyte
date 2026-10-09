namespace Lumyte.Diagnostics.Sample;

/// <summary>One opt-in attribute; argument and output schemas are generated.</summary>
/// <param name="input">The shared scoped input service.</param>
public sealed partial class InputDiagnostics(InputOverrideService input)
{
    private readonly InputOverrideService _input = input;

    [DiagnosticOperation(DiagnosticPermission.OverrideInput)]
    private DiagnosticResult<ButtonOverrideReceipt> OverrideButton(
        DiagnosticOperationContext context, string button, bool pressed, long durationMs)
        => _input.Override(context, button, pressed, durationMs);
}
