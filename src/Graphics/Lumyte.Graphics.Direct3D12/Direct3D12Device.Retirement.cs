using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Direct3D12;

/// <summary>Provides the device retirement capability boundary.</summary>
public sealed partial class Direct3D12Device
{
    /// <summary>Gets the device-owned authority; it never destroys or forces loss of the borrowed device.</summary>
    public IGraphicsDeviceRetirementAuthority RetirementAuthority { get; }

    private sealed class RetirementIssuer
    {
    }

    private sealed class RetirementAuthorityAdapter : IGraphicsDeviceRetirementAuthority
    {
        private readonly Direct3D12Device _owner;
        private readonly RetirementIssuer _issuer = new();

        internal RetirementAuthorityAdapter(Direct3D12Device owner)
        {
            _owner = owner;
            Generation = GraphicsDeviceGeneration.CreateTrusted(owner, this, _issuer);
        }

        public IGraphicDevice Device => _owner;

        public GraphicsDeviceGeneration Generation { get; }

        public async ValueTask<GraphicsDeviceRetirementProof> WaitForRetirementAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // The cached loss is populated only by this backend's native lifecycle.
            // Ready/Disposed are not retirement; in particular idle is not loss.
            if (_owner.Status != GraphicsDeviceStatus.Lost)
            {
                throw new InvalidOperationException("Trusted retirement requires confirmed native device loss.");
            }

            await _owner.WaitForRetirementAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (_owner.Status != GraphicsDeviceStatus.Lost)
            {
                throw new InvalidOperationException("Native retirement is no longer confirmed.");
            }

            return GraphicsDeviceRetirementProof.Issue(Generation, _issuer);
        }
    }
}
