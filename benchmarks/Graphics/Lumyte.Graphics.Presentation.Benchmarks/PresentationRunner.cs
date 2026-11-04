extern alias UiBenchmarks;

using System.Diagnostics;
using System.Numerics;
using System.Runtime.Versioning;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Incremental.Benchmarks;
using Lumyte.Graphics.Wgpu;
using Lumyte.Platform.Linux;
using Lumyte.Windowing.Abstractions;
using UiProfile = UiBenchmarks::Lumyte.Graphics.UiScenarios.UiProfile;
using UiScenario = UiBenchmarks::Lumyte.Graphics.UiScenarios.UiScenario;

namespace Lumyte.Graphics.Presentation.Benchmarks;

[SupportedOSPlatform("linux")]
internal static class PresentationRunner
{
    internal static int Run(Options options)
    {
        object provenance = Lumyte.Graphics.Gpu.Benchmarks.Provenance.Capture();
        using var ownership = new RetirementScope();
        LinuxPlatform platform = ownership.Own(new LinuxPlatform());
        IWindow window = ownership.Own(platform.CreateWindow(new WindowDescriptor
        {
            Title = "Lumyte ADR0027 real presentation (benchmark policy)",
            ClientSize = new(ClearFixture.Width, ClearFixture.Height),
            IsResizable = false,
        }));
        window.Show();
        long visibilityDeadline = Stopwatch.GetTimestamp() + (5 * Stopwatch.Frequency);
        while (!window.State.IsVisible && Stopwatch.GetTimestamp() < visibilityDeadline)
        {
            platform.PumpEvents();
            Thread.Sleep(5);
        }

        CheckWindow(window);
        IDisposable retained = ownership.Own(window.RetainNativeTarget());
        WgpuDevice device = ownership.Own(WgpuDevice.Create());
        object nativeAdapter = NativeIdentity.Capture(device);
        var native = (LinuxWindow)window;
        IGraphicsSurface surface = ownership.Own(device.CreateSurface(Ahjo.Wgpu.SurfaceSource.XlibWindow(native.XDisplay, native.XWindow)));
        SurfaceCapabilities caps = surface.GetCapabilities();

        // No format/present-policy fallback. BGRA8-sRGB is an explicit presentation variant.
        const TextureFormat Format = TextureFormat.Bgra8Srgb;
        if (!caps.Formats.Contains(Format) || !caps.PresentModes.Contains(options.Mode) || !caps.AlphaModes.Contains(SurfaceAlphaMode.Opaque))
        {
            throw new NotSupportedException("Exact BGRA8-sRGB/opaque/present-mode configuration unavailable.");
        }

        UiProfile profile = UiProfile.Standard.Single(p => p.Id == "U03" && p.Variant == "forced-draw");
        bool isUi = options.Scenario != "forced-clear";
        if (!isUi)
        {
            ClearFixture.VerifyHeadless(device, Format);
        }

        object[] uiChecks = isUi ? [UiPixelGate.Verify(device, profile, Format, 0, ownership), UiPixelGate.Verify(device, profile, Format, options.Frames - 1, ownership)] : [];
        Renderer2D renderer = ownership.Own(new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault()));
        UiScenario? fixture = isUi ? ownership.Own(new UiScenario(profile)) : null;
        UiCompositePass? compositor = isUi ? ownership.Own(new UiCompositePass(device, ownership)) : null;
        IGraphicsSwapchain swapchain = ownership.Own(surface.CreateSwapchain(new SwapchainDesc
        {
            Width = ClearFixture.Width,
            Height = ClearFixture.Height,
            Format = Format,
            PresentMode = options.Mode,
            AlphaMode = SurfaceAlphaMode.Opaque,
        }));
        Slot[] slots = Enumerable.Range(0, options.Depth).Select(_ => new Slot
        {
            Intermediate = compositor is null ? null : ownership.Own(compositor.CreateTarget(device, ownership)),
        }).ToArray();
        var samples = new List<FrameSample>(options.Frames);
        var schedule = new DemandSchedule();
        long activeTicks = 0;
        long idleStart = 0;
        long idleEnd = 0;
        long resumeInputTick = 0;
        long previousPresent = 0;
        int idleReservations = 0;
        object? measuredCounterBaseline = null;
        try
        {
            for (int i = 0; i < options.Warmup; i++)
            {
                platform.PumpEvents();
                CheckWindow(window);
                Frame(slots[i % slots.Length], swapchain, device, fixture, renderer, compositor, i, i + 1, 0);
            }

            foreach (Slot slot in slots)
            {
                slot.Retire();
            }

            fixture?.Update(0);
            fixture?.Layout();
            measuredCounterBaseline = fixture?.Counters();
            for (int i = 0; i < options.Frames; i++)
            {
                // Native pumping and configuration validation are deliberately outside host intervals.
                platform.PumpEvents();
                CheckWindow(window);
                if (options.Scenario == "benchmark-idle-u03" && i == options.Frames / 2)
                {
                    long drainStart = Stopwatch.GetTimestamp();
                    foreach (Slot pending in slots)
                    {
                        pending.Retire();
                    }

                    activeTicks += Stopwatch.GetTimestamp() - drainStart;
                    int before = schedule.Reservations;
                    idleStart = Stopwatch.GetTimestamp();

                    // A controlled managed wake,
                    // NOT native-input integration or product-host scheduling.
                    using var wake = new ManualResetEventSlim(false);
                    using var timer = new Timer(_ => wake.Set(), null, options.IdleMs, Timeout.Infinite);
                    wake.Wait();
                    idleEnd = Stopwatch.GetTimestamp();
                    idleReservations = schedule.Reservations - before;
                    platform.PumpEvents();
                    CheckWindow(window);
                    resumeInputTick = Stopwatch.GetTimestamp();
                }

                long start = Stopwatch.GetTimestamp();
                schedule.Invalidate(i + 1);
                if (!schedule.TryReserve())
                {
                    throw new InvalidOperationException("Frame reservation lost.");
                }

                Slot slot = slots[i % slots.Length];
                FrameSample sample = Frame(slot, swapchain, device, fixture, renderer, compositor, i, schedule.InputId, previousPresent);
                activeTicks += Stopwatch.GetTimestamp() - start;
                sample.InputAppliedTick = start;
                sample.UiCounters = fixture?.Counters();
                sample.LogicalInputSha256 = fixture?.Input.InputId;
                previousPresent = sample.PresentReturnTick;
                samples.Add(sample);
            }
        }
        finally
        {
            long drainStart = Stopwatch.GetTimestamp();

            // Also drains the partially constructed slot when acquire/encode/present throws.
            var cleanupErrors = new List<Exception>();
            foreach (Slot slot in slots)
            {
                try
                {
                    slot.Retire();
                }
                catch (Exception error)
                {
                    cleanupErrors.Add(error);
                }
            }

            activeTicks += Stopwatch.GetTimestamp() - drainStart;
            if (cleanupErrors.Count != 0)
            {
                ownership.Quarantine(slots);
                throw new AggregateException("Final drain failed; native owners quarantined, run invalid.", cleanupErrors);
            }
        }

        object? finalUiGate = fixture is null ? null : UiPixelGate.VerifyFixture(device, fixture, Format, options.Frames - 1, ownership);
        Program.Emit(new
        {
            status = "presentation-host-observations-completed",
            provenance,
            options,
            nativeAdapter,
            adapterAccess = "benchmark-only version-sensitive UnsafeAccessor; no production IVT/API change",
            productHostProof = false,
            fullHardwareLatencyClaim = false,
            measuredMonitorRefreshHz = (double?)null,
            reservationCount = schedule.Reservations,
            measuredCounterBaseline,
            counterScope = "cumulative actual UiScenario counters; subtract baseline outside timing; per-frame snapshots after host interval",
            fixture = isUi ? "UiProfile.Standard U03/forced-draw; mounted UiSurface/layout/native text/Canvas/Renderer2D" : "clear-only control; not U03",
            fontSha256 = fixture?.FontHash,
            compositeShaders = compositor?.ShaderProvenance,
            intermediateFormat = isUi ? "Rgba8Srgb" : null,
            compositeRoute = isUi ? "Renderer2D RGBA8-sRGB -> product present shader -> acquired BGRA8-sRGB, included in encode/draw counters" : null,
            uiChecks,
            finalUiGate,
            finalUiCounters = fixture?.Counters(),
            gpuSpanMs = (double?)null,
            gpuSpanReason = "native timestamp queries not instrumented; host completion is not GPU duration",
            extent = new { width = ClearFixture.Width, height = ClearFixture.Height, dpr = 1, mappedVisible = true, occlusion = "unknown" },
            format = Format.ToString(),
            presentMode = options.Mode.ToString(),
            vsyncPolicy = options.Mode == PresentMode.Fifo ? "FIFO synchronization requested" : "non-FIFO requested; compositor/display pacing unknown",
            refresh = new { hz = options.RefreshHz, source = "operator assumption, not measured" },
            analyticGate = isUi ? "fresh-mount full-image equality plus red/blue clip/order oracle; NOT compositor/screen capture" : "full-image red/green clear control; NOT U03",
            stopwatchFrequency = Stopwatch.Frequency,
            measuredHostWorkIncludingFinalDrainTicks = activeTicks,
            throughput = "not reported: summed host intervals exclude event pumping/checks, not continuous display throughput",
            idle = new { idleStart, idleEnd, resumeInputTick, idleReservations, source = "benchmark-managed timer; X11 events are not pumped during this idle wait" },
            completion = "host observation at slot retirement, not GPU timestamp; includes queue/callback/host delay",
            presentation = "Present call/return only; no scanout or photon latency, no measured refresh",
            samples,
        });

        // Completion certifies this host-observation run only, never photon latency/product-host scheduling.
        return 0;
    }

    private static FrameSample Frame(Slot slot, IGraphicsSwapchain swapchain, WgpuDevice device, UiScenario? fixture, Renderer2D renderer, UiCompositePass? compositor, int logicalFrame, long inputId, long previousPresent)
    {
        long waitStart = Stopwatch.GetTimestamp();
        slot.Retire();
        long acquireStart = Stopwatch.GetTimestamp();
        SurfaceAcquireResult acquired = swapchain.AcquireNextFrameAsync().AsTask().GetAwaiter().GetResult();
        slot.Frame = acquired.Frame;
        long encodeStart = Stopwatch.GetTimestamp();
        if (acquired.Status != SurfaceStatus.Success || slot.Frame is null)
        {
            throw new InvalidOperationException("Exact surface acquisition required; no retry/fallback: " + acquired.Status);
        }

        long updateStart = Stopwatch.GetTimestamp();
        fixture?.Update(logicalFrame);
        long layoutStart = Stopwatch.GetTimestamp();
        fixture?.Layout();
        long recordStart = Stopwatch.GetTimestamp();
        IReadOnlyList<DrawList2D>? lists = fixture?.Record();
        long prepareStart = Stopwatch.GetTimestamp();
        slot.View = slot.Frame.Texture.CreateView();
        if (lists is not null)
        {
            foreach (DrawList2D list in lists)
            {
                slot.Prepared.Add(renderer.PrepareAsync(list, new()
                {
                    View = slot.Intermediate!.View,
                    LoadOp = slot.Prepared.Count == 0 ? AttachmentLoadOp.Clear : AttachmentLoadOp.Load,
                }).AsTask().GetAwaiter().GetResult());
            }
        }

        long commandStart = Stopwatch.GetTimestamp();
        slot.Commands = new PublicRetainedApiProbeCommands(device.CreateCommandBuffer(new()));
        if (lists is null)
        {
            ClearFixture.Record(slot.Commands, slot.View, inputId);
        }
        else
        {
            slot.Intermediate!.Begin(slot.Commands);
            for (int layer = 0; layer < slot.Prepared.Count; layer++)
            {
                if (layer != 0)
                {
                    slot.Intermediate.BetweenLayers(slot.Commands);
                }

                slot.Prepared[layer].Record(slot.Commands);
            }

            compositor!.Record(slot.Commands, slot.Intermediate, slot.Frame.Texture, slot.View, present: true);
        }

        slot.Commands.Finish();
        long submitStart = Stopwatch.GetTimestamp();
        slot.Uncertain = true;
        slot.Submission = device.Queue.Submit([slot.Commands.Inner]);
        long submitEnd = Stopwatch.GetTimestamp();
        var sample = new FrameSample
        {
            LogicalFrame = logicalFrame,
            InputId = inputId,
            ContentId = fixture is null ? ((inputId & 1) == 0 ? "clear-v1:1920x1080:green:opaque" : "clear-v1:1920x1080:red:opaque") : "U03/static-ui-v1",
            UpdateTicks = layoutStart - updateStart,
            LayoutTicks = recordStart - layoutStart,
            RecordTicks = prepareStart - recordStart,
            PrepareTicks = commandStart - prepareStart,
            IssuedDrawCalls = slot.Commands.DrawCalls,
            CompositeDrawCalls = fixture is null ? 0 : 1,
            RecordedCommandCount = lists?.Sum(list => list.Count) ?? 0,
            SlotWaitAndRetirementTicks = acquireStart - waitStart,
            AcquireTicks = encodeStart - acquireStart,
            EncodeTicks = submitStart - commandStart,
            SubmitStartTick = submitStart,
            SubmitTicks = submitEnd - submitStart,
            HostFrameToSubmitTicks = submitEnd - encodeStart,
        };
        slot.Sample = sample;
        long presentStart = Stopwatch.GetTimestamp();
        SurfaceStatus status = slot.Frame.Present();
        long presentEnd = Stopwatch.GetTimestamp();
        sample.PresentTicks = presentEnd - presentStart;
        sample.PresentReturnTick = presentEnd;
        sample.PresentReturnIntervalTicks = previousPresent == 0 ? null : presentEnd - previousPresent;
        if (status != SurfaceStatus.Success)
        {
            throw new InvalidOperationException("Present returned " + status);
        }

        return sample;
    }

    private static void CheckWindow(IWindow window)
    {
        WindowState state = window.State;
        if (state.Lifetime != WindowLifetime.Open || !state.IsVisible || state.PixelSize != new WindowPixelSize((int)ClearFixture.Width, (int)ClearFixture.Height) || state.Scale != Vector2.One)
        {
            throw new InvalidOperationException("Window must remain open, mapped, 1920x1080 and DPR 1; resized/hidden/closed results are invalid.");
        }
    }

    private sealed class Slot
    {
        internal IGraphicsSurfaceFrame? Frame { get; set; }

        internal IGraphicsTextureView? View { get; set; }

        internal UiCompositeTarget? Intermediate { get; init; }

        internal PublicRetainedApiProbeCommands? Commands { get; set; }

        internal List<PreparedDraw2D> Prepared { get; } = [];

        internal IGraphicsSubmission? Submission { get; set; }

        internal FrameSample? Sample { get; set; }

        internal bool Uncertain { get; set; }

        internal void Retire()
        {
            if (Submission is not null)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                Submission.WaitAsync(timeout.Token).AsTask().GetAwaiter().GetResult();
                Uncertain = false;
                if (Sample is not null)
                {
                    Sample.CompletionObservedTick = Stopwatch.GetTimestamp();
                }

                Submission.Dispose();
                Submission = null;
            }

            if (Uncertain)
            {
                throw new InvalidOperationException("Unknown Submit acceptance; native owners must be quarantined.");
            }

            using var releaseTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            Frame?.WaitForReleaseAsync(releaseTimeout.Token).AsTask().GetAwaiter().GetResult();
            Commands?.Dispose();
            Commands = null;
            foreach (PreparedDraw2D draw in Prepared)
            {
                draw.Dispose();
            }

            Prepared.Clear();
            View?.Dispose();
            View = null;
            Frame?.Dispose();
            Frame = null;
            Sample = null;
        }
    }
}
