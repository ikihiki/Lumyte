using System.Diagnostics;
using Lumyte.Graphics.Drawing2D;
using Lumyte.UI;
using Lumyte.UI.Application;
using Lumyte.UI.Application.Desktop;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.Graphics.Presentation.Benchmarks;

// Observes the actual desktop owner without replacing its scheduling policy.
internal static class ProductHostProbe
{
    internal static int Run(int idleMs, string displayKind) => RunAsync(idleMs, displayKind).GetAwaiter().GetResult();

    private static async Task<int> RunAsync(int idleMs, string displayKind)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(idleMs);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(idleMs) + TimeSpan.FromSeconds(30));
        var content = new ProbeContent();
        UiApplicationBuilder builder = UiApplication.CreateBuilder().UseDesktop();
        builder.UseContent(_ => content);
        await using UiApplication app = builder.Build();
        Task running = app.RunAsync(deadline.Token);
        object observation;
        try
        {
            await AwaitObservationAsync(content.Ready.Task, running, deadline.Token);
            await AwaitObservationAsync(content.FirstRecord.Task, running, deadline.Token);

            // Settle initialization without manually pumping, recording or reserving frames.
            await Task.Delay(250, deadline.Token);
            long idleStart = Stopwatch.GetTimestamp();
            long recordsBefore = content.RecordCount;
            await Task.Delay(idleMs, deadline.Token);
            long recordsAfter = content.RecordCount;
            long idleEnd = Stopwatch.GetTimestamp();
            long updateRequested = Stopwatch.GetTimestamp();
            long updateApplied = 0;
            await app.Dispatcher.InvokeAsync(
                () =>
                {
                    updateApplied = Stopwatch.GetTimestamp();
                    content.Context.View.CurrentSurface!.Rebuild(content.MakeRoot(1));
                },
                deadline.Token);
            long updatedRecord = await AwaitObservationAsync(content.UpdatedRecord.Task, running, deadline.Token);
            observation = new
            {
                status = "product-host-observed",
                productHostProof = true,
                productHostProofScope = "actual UseDesktop owner: CPU recording callbacks and controlled model-update delivery only",
                driver = "standard DesktopDriver; application owns platform, wgpu surface and retirement",
                fixture = "custom PanelElement.OnRecord with visible red rectangle rebuilt green; no text/native-input claim",
                nativeAdapter = (object?)null,
                nativeIdentityScope = "not queried in this fixture; do not substitute a separate device identity",
                stopwatchFrequency = Stopwatch.Frequency,
                frameRequests = (long?)null,
                gpuDrawCalls = (long?)null,
                presents = (long?)null,
                unavailableCounters = "no public standard-host reservation, GPU draw or Present observation hook",
                demandIdleSupported = false,
                demandIdleScope = "current product host is periodic; zero-work demand-idle is not an implemented contract",
                idle = new
                {
                    requestedMs = idleMs,
                    idleStart,
                    idleEnd,
                    recordsBefore,
                    recordsAfter,
                    recordCallbackDelta = recordsAfter - recordsBefore,
                    scope = "CPU callback delta; UI owner continues native pumping; not a GPU/presentation counter",
                },
                update = new
                {
                    previousRevision = 0,
                    recordedRevision = 1,
                    updateRequested,
                    updateApplied,
                    updatedRecord,
                    requestToRecordMs = (updatedRecord - updateRequested) * 1000d / Stopwatch.Frequency,
                    applyToRecordMs = (updatedRecord - updateApplied) * 1000d / Stopwatch.Frequency,
                    scope = "managed dispatcher update to CPU recording, not resumption from demand-idle or input-to-photon latency",
                },
                monitorRefreshHz = (double?)null,
                displayKind,
                displayClassificationScope = "caller-supplied classification, not physical display proof",
                physicalDisplayProof = false,
            };
        }
        finally
        {
            app.RequestStop();

            // RunAsync certifies the application's ordered shutdown attempt, not just stop admission.
            await running.WaitAsync(TimeSpan.FromSeconds(20));
        }

        if (app.State != UiApplicationState.Stopped)
        {
            throw new InvalidOperationException($"Product host did not stop normally: {app.State}", app.Failure);
        }

        Program.Emit(observation);
        return 0;
    }

    private static async Task<T> AwaitObservationAsync<T>(Task<T> observed, Task running, CancellationToken cancellationToken)
    {
        Task winner = await Task.WhenAny(observed, running).WaitAsync(cancellationToken);
        if (ReferenceEquals(winner, running))
        {
            await running;
            throw new InvalidOperationException("Product host stopped before the expected recording observation.");
        }

        return await observed;
    }

    private sealed class ProbeContent : IUiApplicationContent
    {
        private long _records;

        public bool IsStopped { get; private set; }

        internal UiApplicationWindowContext Context { get; private set; } = null!;

        internal TaskCompletionSource<bool> Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource<long> FirstRecord { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource<long> UpdatedRecord { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal long RecordCount => Interlocked.Read(ref _records);

        public async ValueTask InitializeAsync(UiApplicationWindowContext context, CancellationToken cancellationToken)
        {
            Context = context;
            await context.View.SetInitialRootAsync(MakeRoot(0), cancellationToken);
            Ready.TrySetResult(true);
        }

        public void Advance()
        {
        }

        public void BeginStop() => IsStopped = true;

        public ValueTask RetireAsync() => ValueTask.CompletedTask;

        public ValueTask ReleaseAsync() => ValueTask.CompletedTask;

        internal Widget MakeRoot(int revision)
        {
            var root = new ProbePanel(this, revision);
            root.Children.Initialize(Lumyte.UI.Compose.Rectangle(width: 240, height: 160, fill: revision == 0 ? new Color2D(1, 0, 0, 1) : new Color2D(0, 1, 0, 1)));
            return root;
        }

        internal void Observe(int revision)
        {
            long tick = Stopwatch.GetTimestamp();
            Interlocked.Increment(ref _records);
            FirstRecord.TrySetResult(tick);
            if (revision == 1)
            {
                UpdatedRecord.TrySetResult(tick);
            }
        }
    }

    private sealed class ProbePanel(ProbeContent content, int revision) : Lumyte.UI.Compose.Definitions.Panel
    {
        internal ProbeContent Content { get; } = content;

        internal int Revision { get; } = revision;

        protected override UiElement CreateElement() => new ProbePanelElement(this);
    }

    private sealed class ProbePanelElement(ProbePanel widget) : PanelElement(widget)
    {
        protected override void OnRecord(DrawingCanvas2D canvas)
        {
            var current = (ProbePanel)Widget;
            current.Content.Observe(current.Revision);
        }
    }
}
