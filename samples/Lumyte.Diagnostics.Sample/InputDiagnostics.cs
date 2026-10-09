namespace Lumyte.Diagnostics.Sample;

/// <summary>One opt-in attribute; argument and output schemas are generated.</summary>
public sealed partial class InputDiagnostics
{
    private readonly InputOverrideService _input;

    /// <summary>Initializes a new instance of the <see cref="InputDiagnostics"/> class.</summary>
    /// <param name="input">The shared scoped input service.</param>
    public InputDiagnostics(InputOverrideService input)
    {
        _input = input;
    }

    [DiagnosticOperation(DiagnosticPermission.OverrideInput)]
    private DiagnosticResult<ButtonOverrideReceipt> OverrideButton(
        DiagnosticOperationContext context, string button, bool pressed, long durationMs)
        => _input.Override(context, button, pressed, durationMs);
}
