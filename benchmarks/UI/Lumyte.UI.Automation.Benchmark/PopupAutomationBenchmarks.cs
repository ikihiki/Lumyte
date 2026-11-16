using System.Numerics;
using BenchmarkDotNet.Attributes;
using Lumyte.Input.Abstractions;
using Lumyte.UI.Windowing;
using Lumyte.UI.Windowing.Automation;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Automation.Benchmark;

/// <summary>Actual hosted popup routing and adapter lifetime, CPU geometry acknowledgments only.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Screening", "Comparison")]
public class PopupAutomationBenchmarks
{
    private readonly SemaphoreSlim _input = new(1, 1);
    private readonly List<AutomationFixture> _hosts = [];
    private HostedUiWindowManager _windows = null!;
    private UiPopupManager _popups = null!;
    private Source _source = null!;
    private UiSurface _surface = null!;
    private UiWindowRegistration _registration = null!;
    private UiWindowBinding _binding = null!;
    private UiPopupOwner _owner = null!;
    private UiPopupAutomationSession _session = null!;
    private UiPopupHandle _popup = null!;

    /// <summary>Gets callback completions.</summary>
    public int Clicks { get; private set; }

    /// <summary>Gets actual manager frame advances.</summary>
    public int FrameOrdinal { get; private set; }

    /// <summary>Builds a real hosted manager, binding, popup and routed session.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _source = new();
        _windows = new();
        _popups = new(_windows);
        _surface = new(Button(width: 20, height: 20, testId: "anchor"));
        _registration = _windows.Register(_source);
        Task<UiWindowBinding> binding = _windows.BindPresentationAsync(_registration.Window, _surface).AsTask();
        Pump();
        _binding = binding.GetAwaiter().GetResult();
        _owner = _popups.RegisterOwner(_registration.Window, _surface);
        _session = new(_popups, Host(_surface), Host);
        _popup = _popups.RequestOpen(_owner, new(_surface.RootElement, new(80, 60), () => Button(width: 60, height: 20, testId: "item", onClick: () => Clicks++)));
        Pump();
        if (_popup.State != UiPopupState.Open)
        {
            throw new InvalidOperationException(_popup.FailureCode);
        }
    }

    /// <summary>Copies actual detached popup records through the public session adapter.</summary>
    /// <returns>The live popup list.</returns>
    [Benchmark]
    public Task<IReadOnlyList<UiPopupInfo>> UiPopupAutomationSessionList() => _session.ListPopupsAsync();

    /// <summary>Routes stable pointer geometry, lease input and callback through the actual menu scope.</summary>
    /// <returns>The completed click and input lease return.</returns>
    [Benchmark]
    public Task UiPopupAutomationSessionClick() => _session.Popup(_popup.Id).GetByTestId("item").ClickAsync();

    /// <summary>Cancels a partially pressed popup key using the actual adapter-owned routing lease.</summary>
    /// <returns>Whether any popup press remains after cleanup.</returns>
    public async Task<bool> CancelPartialInputAsync()
    {
        IUiAutomationHost host = _session.PopupHost(_popup.Id);
        await _session.Popup(_popup.Id).GetByTestId("item").FocusAsync();
        IUiAutomationInputLease lease = await host.AcquireInputAsync();
        await host.RunOnUiThreadAsync(surface => lease.Dispatch(surface, new UiKeyInput(Key.Enter, true)));
        await lease.DisposeAsync();
        return await host.RunOnUiThreadAsync(surface => surface.CaptureAutomationSnapshot().Nodes.Any(node => node.HasActivePress));
    }

    /// <summary>Closes the popup and drains bound automation adapters.</summary>
    /// <returns>Remaining live popup count.</returns>
    public async Task<int> CloseAsync()
    {
        _popup.Close();
        Pump();
        return (await _session.ListPopupsAsync()).Count;
    }

    /// <summary>Disposes adapters before borrowed surfaces and manager shutdown.</summary>
    /// <returns>Complete borrowed-owner cleanup.</returns>
    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        await _session.DisposeAsync();
        _popups.BeginStop();
        Pump();
        _popups.Dispose();
        Task unbind = _binding.DisposeAsync().AsTask();
        for (int i = 0; i < 12; i++)
        {
            _windows.Advance();
            _source.Present();
        }

        await unbind;
        _registration.Dispose();
        _windows.Advance();
        _windows.BeginStop();
        _windows.Advance();
        _windows.Dispose();
        foreach (AutomationFixture host in _hosts)
        {
            host.Dispose();
        }

        _surface.Dispose();
        _input.Dispose();
    }

    private AutomationFixture Host(UiSurface surface)
    {
        var host = new AutomationFixture(surface, _input, Pump);
        _hosts.Add(host);
        return host;
    }

    private void Pump()
    {
        for (int i = 0; i < 12; i++)
        {
            _windows.Advance();
            _popups.Advance();
            _source.Present();
            FrameOrdinal++;
        }
    }

    private sealed class Source : IUiWindowSource
    {
        private readonly List<Presentation> _presentations = [];
        private readonly InputWindowId _origin = new(Guid.NewGuid());
        private readonly Guid _scope = Guid.NewGuid();

        public Guid SourceId { get; } = Guid.NewGuid();

        public UiWindowSourceSnapshot CaptureState() => new(1, _origin, new(200, 150), Vector2.One, true, true, false, _scope);

        public ValueTask<IUiWindowPresentation> PreparePresentationAsync(UiWindowPresentationView view, UiSurface surface, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var presentation = new Presentation(view);
            _presentations.Add(presentation);
            return ValueTask.FromResult<IUiWindowPresentation>(presentation);
        }

        public bool TryTransformPoint(IUiWindowSource destination, Vector2 position, out Vector2 transformed)
        {
            transformed = position;
            return ReferenceEquals(this, destination);
        }

        internal void Present()
        {
            foreach (Presentation presentation in _presentations)
            {
                presentation.Acknowledge();
            }
        }

        private sealed class Presentation(UiWindowPresentationView view) : IUiWindowPresentation
        {
            private UiWindowPresentationView _view = view;

            public ulong PresentedGeometryRevision { get; private set; }

            public void Update(UiWindowPresentationView next) => _view = next;

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;

            internal void Acknowledge() => PresentedGeometryRevision = _view.GeometryRevision;
        }
    }
}
