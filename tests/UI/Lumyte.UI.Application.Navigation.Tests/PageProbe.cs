using Lumyte.UI.Navigation;

namespace Lumyte.UI.Application.Navigation.Tests;

internal sealed class PageProbe
{
    internal List<PageVisit> Visits { get; } = [];

    internal List<PageService> Services { get; } = [];

    internal string? FailureStage { get; set; }

    internal IUiPageLeaveGuard? Guard { get; set; }

    internal bool FailRetire { get; set; }

    internal bool FailDispose { get; set; }

    internal TaskCompletionSource? Preparation { get; set; }
}
