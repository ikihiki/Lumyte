using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Lumyte.Interaction;

public static class InteractionDiagnostics
{
    public const string ActivitySourceName = "Lumyte.Interaction";

    public const string MeterName = "Lumyte.Interaction";

    public static ActivitySource Activities { get; } = new(ActivitySourceName);

    public static Meter Metrics { get; } = new(MeterName);

    internal static Counter<long> ActionPhaseChanges { get; } =
        Metrics.CreateCounter<long>("lumyte.interaction.action.phase_changes");

    internal static Counter<long> BindingOverrides { get; } =
        Metrics.CreateCounter<long>("lumyte.interaction.binding_overrides");

    internal static Counter<long> DeviceAssignments { get; } =
        Metrics.CreateCounter<long>("lumyte.interaction.device.assignments");

    internal static Counter<long> GesturesRecognized { get; } =
        Metrics.CreateCounter<long>("lumyte.interaction.gestures.recognized");

    internal static Counter<long> KeybindingResolutions { get; } =
        Metrics.CreateCounter<long>("lumyte.interaction.keybinding.resolutions");

    internal static Counter<long> PlayerJoinRequests { get; } =
        Metrics.CreateCounter<long>("lumyte.interaction.player_join.requests");

    internal static Counter<long> SourceChanges { get; } =
        Metrics.CreateCounter<long>("lumyte.interaction.active_source.changes");
}
