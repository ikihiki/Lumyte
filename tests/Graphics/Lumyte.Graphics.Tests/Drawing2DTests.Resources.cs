using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Benchmarks.Testing;
using Lumyte.Graphics.Drawing2D;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Provides serial, per-method resource reuse for repeated pixel readbacks.</summary>
public sealed partial class Drawing2DTests
{
    /// <summary>Resource reuse is lazy, path-specific and begins after each independent fresh boundary.</summary>
    [Fact]
    public void SerialResourcesKeepFirstRenderFreshAndSeparatePaths()
    {
        using var device = new CpuGraphicsDevice();
        Assert.Null(_serialResources.Value);
        using var scope = new SerialDrawingResources(device);
        Assert.Null(scope.Get(device, defaults: true));
        Assert.Null(scope.Get(device, defaults: false));
        DrawingResourceState2D defaults = scope.Get(device, defaults: true)!;
        DrawingResourceState2D reference = scope.Get(device, defaults: false)!;
        Assert.Same(defaults, scope.Get(device, defaults: true));
        Assert.Same(reference, scope.Get(device, defaults: false));
        Assert.NotSame(defaults, reference);
        Assert.NotNull(defaults.Records());
        Assert.Null(reference.Records());
        scope.Dispose();
        Assert.Null(_serialResources.Value);
        Assert.Throws<ObjectDisposedException>(() => scope.Get(device, defaults: true));
    }

    // Created inside opted-in methods only. Never shares devices across tests or assemblies.
    private sealed class SerialDrawingResources : IDisposable
    {
        private readonly IGraphicDevice _device;
        private readonly SerialDrawingResources? _previous;
        private DrawingResourceState2D? _defaults;
        private DrawingResourceState2D? _reference;
        private ExternalDrawingResources? _external;
        private bool _seenDefault;
        private bool _seenReference;
        private bool _disposed;

        internal SerialDrawingResources(IGraphicDevice device)
        {
            _device = device;
            _previous = _serialResources.Value;
            _serialResources.Value = this;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _defaults?.Dispose();
            _reference?.Dispose();
            if (_external is not null)
            {
                Assert.Equal(1, _external.Disposals);
            }

            _serialResources.Value = _previous;
            _disposed = true;
        }

        internal DrawingResourceState2D? Get(IGraphicDevice device, bool defaults)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Assert.Same(_device, device);

            // Keep the first render of each path as a fresh construction/ownership boundary.
            if (defaults)
            {
                if (!_seenDefault)
                {
                    _seenDefault = true;
                    return null;
                }

                return _defaults ??= new(device, numericPoolBudget: 0);
            }

            if (!_seenReference)
            {
                _seenReference = true;
                return null;
            }

            if (_reference is null)
            {
                _external = new(device);
                _reference = new(device, _external.Resources, _external, numericPoolBudget: 0);
            }

            Assert.Equal(0, _external!.Disposals);
            return _reference;
        }
    }
}
