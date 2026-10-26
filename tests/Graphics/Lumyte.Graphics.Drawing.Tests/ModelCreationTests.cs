using System.Reflection;
using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Drawing.Tests;

/// <summary>Checks failed asynchronous creation keeps partial native allocations available for retryable cleanup.</summary>
public sealed class ModelCreationTests
{
    /// <summary>Checks the retained cleanup retries the failed disposal without losing or repeating completed work.</summary>
    /// <returns>The asynchronous failure check.</returns>
    [Fact]
    public async Task PartialCreationCleanupRemainsOwnedAfterDisposalFailureAsync()
    {
        IGraphicDevice device = DispatchProxy.Create<IGraphicDevice, DeviceProxy>();
        IGraphicsBuffer<ModelVertex> buffer = DispatchProxy.Create<IGraphicsBuffer<ModelVertex>, AllocationProxy>();
        ((DeviceProxy)device).Buffer = buffer;
        IDisposable? cleanup = null;
        await Assert.ThrowsAsync<InvalidOperationException>(() => ModelMesh.CreatePackedAsync(
            device, new byte[64], new ushort[] { 0, 0, 0 }, DrawingShaders.LoadModelVertex(), owner => cleanup = owner));
        Assert.NotNull(cleanup);
        Assert.Equal(0, ((AllocationProxy)buffer).Disposals);
        Assert.Throws<InvalidOperationException>(() => cleanup.Dispose());
        cleanup.Dispose();
        cleanup.Dispose();
        Assert.Equal(2, ((AllocationProxy)buffer).Disposals);
    }

    /// <summary>Fails the second allocation without requiring a GPU.</summary>
    public class DeviceProxy : DispatchProxy
    {
        private int _allocations;

        /// <summary>Gets or sets the first allocation retained by the mesh factory.</summary>
        public IGraphicsBuffer<ModelVertex> Buffer { get; set; } = null!;

        /// <inheritdoc/>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == "get_Caps")
            {
                return new DeviceCaps { ShaderTarget = ShaderTarget.Wgsl };
            }

            if (targetMethod?.Name == "CreateBuffer" && ++_allocations == 1)
            {
                return Buffer;
            }

            throw new InvalidOperationException("Injected allocation failure.");
        }
    }

    /// <summary>Fails one cleanup attempt and then succeeds.</summary>
    public class AllocationProxy : DispatchProxy
    {
        /// <summary>Gets the number of disposal attempts.</summary>
        public int Disposals { get; private set; }

        /// <inheritdoc/>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == "Dispose")
            {
                Disposals++;
                if (Disposals == 1)
                {
                    throw new InvalidOperationException("Injected cleanup failure.");
                }

                return null;
            }

            throw new NotSupportedException();
        }
    }
}
