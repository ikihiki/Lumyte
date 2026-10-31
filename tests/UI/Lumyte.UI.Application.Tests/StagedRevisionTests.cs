using System.Numerics;
using Lumyte.Signals;
using Lumyte.UI.Windowing;
using Xunit;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Application.Tests;

/// <summary>Checks staged drawing does not use root reference identity as a declaration version.</summary>
public sealed class StagedRevisionTests
{
    /// <summary>Checks unapplied dependencies invalidate prepared commands even when the root and input remain unchanged.</summary>
    /// <returns>The staged presentation verification.</returns>
    [Fact]
    public async Task PendingSignalChangesInvalidatePreparedCommandsAsync()
    {
        var width = Signal.Create<float?>(10);
        using var surface = new UiSurface(Panel()[Rectangle(width: width)]);
        surface.SetViewport(new(100, 60));
        surface.SetSignalUpdatesEnabled(false);
        var state = new UiWindowSourceSnapshot(1, null, new(100, 60), Vector2.One, true, true, false, Guid.NewGuid());
        var source = new UiHostedDrawingSource(() => state);
        var view = new UiWindowPresentationView(new(source.SourceId, 1), source.SourceId, UiWindowRole.Normal, new(0, 0, 100, 60), new(0, 0, 100, 60), Vector2.One, 1, true);
        await using IUiStagedWindowPresentation staged = source.BeginReplacement(null, view, surface);
        await staged.PrepareAsync(default);
        Assert.True(staged.TryValidateCommit(out _));
        Lumyte.UI.Compose.Definitions.Widget root = surface.Root;
        object? input = surface.InputVersionForWindowing;
        width.Value = 20;
        Assert.Same(root, surface.Root);
        Assert.Same(input, surface.InputVersionForWindowing);
        Assert.False(staged.TryValidateCommit(out string code));
        Assert.Equal("candidate-stale", code);
        await staged.DiscardAsync(default);
    }
}
