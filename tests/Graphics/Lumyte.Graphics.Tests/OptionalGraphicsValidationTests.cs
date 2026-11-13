using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shared;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Exercises paired enabled and disabled diagnostics at a fake native boundary.</summary>
public sealed class OptionalGraphicsValidationTests
{
    /// <summary>Checks explicit diagnostic opt-in without issuing invalid native GPU commands.</summary>
    /// <param name="enabled">Whether the optional layer is enabled.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LifetimeAndMappingAreOptIn(bool enabled)
    {
        GraphicsValidation? validation = enabled ? new() : null;
        object device = new(), resource = new(), command = new();
        validation?.Register(resource, device);
        validation?.Use(command, resource);
        void Release() => validation?.Release(resource);
        void Map() => validation?.Map(resource);
        if (enabled)
        {
            Assert.Throws<InvalidOperationException>(Release);
            Assert.Throws<InvalidOperationException>(Map);
            Assert.Throws<InvalidOperationException>(() => validation!.Release(device));
        }
        else
        {
            Release();
            Map();
            Assert.Null(validation);
        }

        validation?.Submit(command);
        if (enabled)
        {
            Assert.Throws<InvalidOperationException>(() => validation!.Discard(command));
        }

        validation?.Complete(command);
        Release();
        validation?.Release(device);
    }

    /// <summary>Checks explicit diagnostic opt-in without issuing invalid native GPU commands.</summary>
    /// <param name="enabled">Whether the optional layer is enabled.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CopyHazardsAndScopesAreOptIn(bool enabled)
    {
        GraphicsValidation? validation = enabled ? new() : null;
        object buffer = new(), command = new();
        BarrierScope write = new(PipelineStage.Copy, ResourceAccess.CopyWrite);
        BarrierScope read = new(PipelineStage.Copy, ResourceAccess.CopyRead);
        validation?.Access(command, buffer, write);
        void Missing() => validation?.Access(command, buffer, read);
        void Wrong() => validation?.Barrier(command, buffer, read, read);
        if (enabled)
        {
            Assert.Throws<InvalidOperationException>(Missing);
            Assert.Throws<InvalidOperationException>(Wrong);
        }
        else
        {
            Missing();
            Wrong();
        }

        validation?.Barrier(command, buffer, write, read);
        validation?.Access(command, buffer, read);
    }

    /// <summary>Checks explicit diagnostic opt-in without issuing invalid native GPU commands.</summary>
    /// <param name="enabled">Whether the optional layer is enabled.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BinarySemaphoreMisuseIsOptIn(bool enabled)
    {
        GraphicsValidation? validation = enabled ? new() : null;
        using var semaphore = new FakeSemaphore();
        var signal = new QueueSubmitDesc { CommandBuffers = [], SignalSemaphores = [semaphore] };
        var wait = new QueueSubmitDesc { CommandBuffers = [], WaitSemaphores = [new() { Semaphore = semaphore, Stages = PipelineStage.Copy }] };
        object first = new(), second = new();
        void Unsignaled() => validation?.ValidateSemaphores(wait);
        if (enabled)
        {
            Assert.Throws<InvalidOperationException>(Unsignaled);
        }
        else
        {
            Unsignaled();
        }

        validation?.ValidateSemaphores(signal);
        validation?.SubmittedSemaphores(first, signal);
        if (enabled)
        {
            Assert.Throws<InvalidOperationException>(() => validation!.ValidateSemaphores(signal));
        }

        validation?.ValidateSemaphores(wait);
        validation?.SubmittedSemaphores(second, wait);
        if (enabled)
        {
            Assert.Throws<InvalidOperationException>(() => validation!.Release(semaphore));
        }

        validation?.CompleteSemaphores(first);
        validation?.CompleteSemaphores(second);
        validation?.ValidateSemaphores(signal);
        validation?.Release(semaphore);
    }

    /// <summary>Checks explicit diagnostic opt-in without issuing invalid native GPU commands.</summary>
    /// <param name="enabled">Whether the optional layer is enabled.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeviceStateIsNullUntilExplicitlyEnabled(bool enabled)
    {
        var device = (Lumyte.Graphics.Vulkan.VulkanDevice)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Lumyte.Graphics.Vulkan.VulkanDevice));
        Assert.Null(device.Validation);
        if (enabled)
        {
            device.EnableValidation();
        }

        Assert.Equal(enabled, device.Validation != null);
        Assert.Equal(enabled, ((IGraphicDevice)device).ValidationEnabled);
    }

    /// <summary>Checks repeated texture Before states without a native GPU call.</summary>
    /// <param name="enabled">Whether the optional layer is enabled.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TextureStateMismatchIsOptIn(bool enabled)
    {
        GraphicsValidation? validation = enabled ? new() : null;
        var texture = (IGraphicsTexture)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Lumyte.Graphics.Vulkan.VulkanDevice).Assembly.GetType("Lumyte.Graphics.Vulkan.VulkanTexture")!);
        object command = new();
        var barrier = new TextureBarrierDesc { Texture = texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.Undefined, AfterState = TextureState.CopyDestination, Before = new(PipelineStage.None, ResourceAccess.None), After = new(PipelineStage.Copy, ResourceAccess.CopyWrite) };
        validation?.TextureBarrier(command, barrier);
        if (enabled)
        {
            Assert.Throws<InvalidOperationException>(() => validation!.TextureBarrier(command, barrier));
        }
        else
        {
            validation?.TextureBarrier(command, barrier);
        }
    }

    /// <summary>Checks actual backend registration identity without native calls.</summary>
    /// <param name="enabled">Whether diagnostics are enabled.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReleasedRegistrationIsDiagnosed(bool enabled)
    {
        Type deviceType = typeof(Lumyte.Graphics.Vulkan.VulkanDevice);
        object device = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(deviceType);
        Type tableType = deviceType.Assembly.GetType("Lumyte.Graphics.Vulkan.VulkanArgumentTable")!;
        object table = Activator.CreateInstance(tableType, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic, null, [device, new ArgumentTableDesc { BufferCapacity = 1 }], null)!;
        System.Reflection.MethodInfo method = tableType.GetMethod("ValidateRegistration", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        if (enabled)
        {
            System.Reflection.TargetInvocationException error = Assert.Throws<System.Reflection.TargetInvocationException>(() => method.Invoke(table, [new object(), 0u, new object()]));
            Assert.IsType<InvalidOperationException>(error.InnerException);
        }
        else
        {
            Assert.NotNull(table);
        }
    }

    /// <summary>Checks copy metadata mutation before submission using an entirely fake native boundary.</summary>
    /// <param name="enabled">Whether diagnostics are enabled.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CopyRevisionChecksAreOptIn(bool enabled)
    {
        var source = new FakeDataSource();
        var destination = new FakeDataSource();
        var state = new ShaderDataTransferState();
        if (enabled)
        {
            state.EnableValidation();
        }

        state.Record(source, 0, destination, 0, 1, () => { });
        source.Value = source.Value.Copy();
        if (enabled)
        {
            Assert.Throws<InvalidOperationException>(state.ValidateSubmit);
        }
        else
        {
            state.ValidateSubmit();
        }
    }

    /// <summary>Rejects enabling diagnostics after resources have escaped the untracked device.</summary>
    [Fact]
    public void LateEnableIsRejectedBeforeCreatingTracker()
    {
        var device = (Lumyte.Graphics.Vulkan.VulkanDevice)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Lumyte.Graphics.Vulkan.VulkanDevice));
        typeof(Lumyte.Graphics.Vulkan.VulkanDevice).GetField("_hasCreatedResources", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(device, true);
        Assert.Throws<InvalidOperationException>(device.EnableValidation);
        Assert.Null(device.Validation);
    }

    /// <summary>Checks logical queue semaphore ownership at the real managed entry without reaching native code.</summary>
    [Fact]
    public void LogicalQueueRejectsForeignSemaphoreBeforeNativeCall()
    {
        Type deviceType = typeof(Lumyte.Graphics.Wgpu.WgpuDevice);
        var owner = (Lumyte.Graphics.Wgpu.WgpuDevice)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(deviceType);
        var foreign = (Lumyte.Graphics.Wgpu.WgpuDevice)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(deviceType);
        owner.EnableValidation();
        const System.Reflection.BindingFlags Constructors = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        var queue = (IGraphicsQueue)Activator.CreateInstance(deviceType.Assembly.GetType("Lumyte.Graphics.Wgpu.WgpuQueue")!, Constructors, null, [owner], null)!;
        var semaphore = (IGraphicsSemaphore)Activator.CreateInstance(deviceType.Assembly.GetType("Lumyte.Graphics.Wgpu.WgpuSemaphore")!, Constructors, null, [foreign], null)!;
        Assert.Throws<ArgumentException>(() => queue.Submit(new QueueSubmitDesc { CommandBuffers = [], SignalSemaphores = [semaphore] }));
        semaphore.Dispose();

        // The wrappers are identities only: never dispose either uninitialized native device.
    }

    /// <summary>Rejected pending-map disposal must not publish a diagnostic release.</summary>
    /// <param name="enabled">Whether diagnostics are enabled.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PendingMapDisposalDoesNotInvalidateBuffer(bool enabled)
    {
        var owner = (Lumyte.Graphics.Wgpu.WgpuDevice)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Lumyte.Graphics.Wgpu.WgpuDevice));
        if (enabled)
        {
            owner.EnableValidation();
        }

        Type bufferType = owner.GetType().Assembly.GetType("Lumyte.Graphics.Wgpu.WgpuBuffer`1")!.MakeGenericType(typeof(uint));
        object buffer = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(bufferType);
        bufferType.GetField("_owner", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(buffer, owner);
        bufferType.GetField("_pending", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(buffer, true);
        Assert.Throws<InvalidOperationException>(() => ((IDisposable)buffer).Dispose());
        owner.Validation?.Use(new object(), buffer);
    }

    private sealed class FakeDataSource : IShaderDataSource
    {
        public ShaderValueSnapshot Value { get; set; } = new("arguments", "Fake", new byte[4], [], []);

        public IShaderDataLayout Layout => throw new NotSupportedException();

        public ulong SizeInBytes => 4;

        public MemoryPreference Memory => MemoryPreference.Upload;

        public object ShaderHandle => this;

        public ShaderValueSnapshot Read(ulong index) => Value;

        public void SetTransferredValue(ulong index, ShaderValueSnapshot value) => Value = value;

        public void ValidateAlive()
        {
        }
    }

    private sealed class FakeSemaphore : IGraphicsSemaphore
    {
        public void Dispose()
        {
        }
    }
}
