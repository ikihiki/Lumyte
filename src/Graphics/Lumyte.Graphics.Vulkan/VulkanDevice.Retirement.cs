using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Vulkan;

/// <summary>Provides the device retirement capability boundary.</summary>
public sealed unsafe partial class VulkanDevice
{
    /// <summary>Gets the device-owned authority; it never destroys or forces loss of the borrowed device.</summary>
    public IGraphicsDeviceRetirementAuthority RetirementAuthority { get; }

    private sealed class RetirementIssuer
    {
    }

    private sealed class RetirementAuthorityAdapter : IGraphicsDeviceRetirementAuthority
    {
        private readonly VulkanDevice _owner;
        private readonly RetirementIssuer _issuer = new();

        internal RetirementAuthorityAdapter(VulkanDevice owner)
        {
            _owner = owner;
            Generation = GraphicsDeviceGeneration.CreateTrusted(owner, this, _issuer);
        }

        public IGraphicDevice Device => _owner;

        public GraphicsDeviceGeneration Generation { get; }

        public ValueTask<GraphicsDeviceRetirementProof> WaitForRetirementAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // The cached loss is populated only by this backend's native lifecycle.
            // Ready/Disposed are not retirement; in particular idle is not loss.
            if (_owner.Status != GraphicsDeviceStatus.Lost)
            {
                throw new InvalidOperationException("Trusted retirement requires confirmed native device loss.");
            }

            _owner.WaitForRetirementAsync(cancellationToken).GetAwaiter().GetResult();
            cancellationToken.ThrowIfCancellationRequested();
            if (_owner.Status != GraphicsDeviceStatus.Lost)
            {
                throw new InvalidOperationException("Native retirement is no longer confirmed.");
            }

            return ValueTask.FromResult(GraphicsDeviceRetirementProof.Issue(Generation, _issuer));
        }
    }
}
