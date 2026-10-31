using Lumyte.Core.Time;
using Lumyte.Graphics.Drawing2D;

namespace Lumyte.UI.Application;

/// <summary>Controls the shared owner queue and frame budgets.</summary>
public sealed record UiApplicationOptions
{
    /// <summary>Gets the application clock.</summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>Gets the borrowed monotonic animation clock, or null to use TimeProvider timestamps.</summary>
    public IMonotonicClock? AnimationClock { get; init; }

    /// <summary>Gets the opaque background cleared once per presentation.</summary>
    public Color2D Background { get; init; } = new(1, 1, 1, 1);

    /// <summary>Gets candidate presentation policy; system UI is the default.</summary>
    public Lumyte.UI.Windowing.TextInput.UiImeCandidateOptions ImeCandidates { get; init; } = new();

    /// <summary>Gets the maximum frame frequency interval.</summary>
    public TimeSpan FrameInterval { get; init; } = TimeSpan.FromSeconds(1d / 60);

    /// <summary>Gets the work item budget per owner turn.</summary>
    public int MaximumWorkItemsPerTurn { get; init; } = 256;

    /// <summary>Gets the synchronous work time budget per turn.</summary>
    public TimeSpan MaximumWorkTimePerTurn { get; init; } = TimeSpan.FromMilliseconds(4);

    /// <summary>Gets the maximum pending external work count.</summary>
    public int MaximumQueuedUiWork { get; init; } = 4096;

    /// <summary>Gets the GPU frame upper bound.</summary>
    public int MaximumFramesInFlight { get; init; } = 2;

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(TimeProvider);
        ArgumentNullException.ThrowIfNull(ImeCandidates);
        UiApplicationBuilder.ValidateSize(ImeCandidates.ClientSize);
        if (Background.A != 1 || FrameInterval <= TimeSpan.Zero || MaximumWorkTimePerTurn <= TimeSpan.Zero
            || MaximumWorkItemsPerTurn < 1 || MaximumQueuedUiWork < 1 || MaximumFramesInFlight < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(UiApplicationOptions));
        }
    }
}
