namespace Lumyte.Interaction;

public sealed record KeybindingConfigurationResult(
    IReadOnlyList<KeybindingOverride> Overrides,
    IReadOnlyList<KeybindingConfigurationDiagnostic> Diagnostics);
