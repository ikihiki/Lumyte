using System.Reflection;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Vulkan;
using Xunit;

namespace Lumyte.Graphics.Pipelines.Tests;

/// <summary>Tests the issuer boundary separately from pipeline scheduling.</summary>
public sealed class TrustedRetirementTests
{
    /// <summary>No public constructor or issuer capability is available.</summary>
    [Fact]
    public void IssuanceIsNotPublic()
    {
        Assert.Empty(typeof(GraphicsDeviceGeneration).GetConstructors());
        Assert.Empty(typeof(GraphicsDeviceRetirementProof).GetConstructors());
        Type issuer = typeof(VulkanDevice).GetNestedType("RetirementIssuer", BindingFlags.NonPublic)!;
        Assert.True(issuer.IsNestedPrivate);
        Assert.True(issuer.IsSealed);
        Assert.DoesNotContain(typeof(VulkanDevice).GetProperties(), property => property.PropertyType == issuer);
    }

    /// <summary>A ready native device neither confirms retirement nor issues a proof.</summary>
    /// <returns>Completion of the retirement check.</returns>
    [NativeFact]
    public async Task ReadyDeviceFailsClosedAsync()
    {
        using var device = VulkanDevice.Create();
        IGraphicsDeviceRetirementAuthority authority = device.RetirementAuthority;
        Assert.Same(authority, device.RetirementAuthority);
        Assert.Same(device, authority.Device);
        Assert.Same(authority.Generation, device.RetirementAuthority.Generation);
        await Assert.ThrowsAsync<InvalidOperationException>(() => authority.WaitForRetirementAsync().AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => device.WaitForRetirementAsync().AsTask());
        Assert.Equal(GraphicsDeviceStatus.Ready, device.Status);
        device.Dispose();
        await Assert.ThrowsAsync<InvalidOperationException>(() => authority.WaitForRetirementAsync().AsTask());
    }

    /// <summary>Friend-visible factories still reject arbitrary issuer objects and borrowed authority wrappers.</summary>
    [NativeFact]
    public void FakeAuthorityAndLocalGenerationCannotMint()
    {
        using var device = VulkanDevice.Create();
        IGraphicsDeviceRetirementAuthority authority = device.RetirementAuthority;
        var fake = new BorrowedAuthority(authority);
        InvokeRejected(typeof(GraphicsDeviceGeneration), "CreateTrusted", null, device, fake, new object());
        InvokeRejected(typeof(GraphicsDeviceGeneration), "CreateTrusted", null, device, authority, new object());
        InvokeRejected(typeof(GraphicsDeviceGeneration), "ValidateAuthority", authority.Generation, fake);
        InvokeRejected(typeof(GraphicsDeviceRetirementProof), "Issue", null, authority.Generation, new object());
        var local = (GraphicsDeviceGeneration)typeof(GraphicsDeviceGeneration).GetMethod("CreateLocal", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [device])!;
        InvokeRejected(typeof(GraphicsDeviceGeneration), "ValidateAuthority", local, authority);
        InvokeRejected(typeof(GraphicsDeviceRetirementProof), "Issue", null, local, new object());
        using var other = VulkanDevice.Create();
        InvokeRejected(typeof(GraphicsDeviceGeneration), "ValidateDevice", authority.Generation, other);
        InvokeRejected(typeof(GraphicsDeviceGeneration), "ValidateAuthority", authority.Generation, other.RetirementAuthority);
    }

    /// <summary>Loss notification plus actual Vulkan idle confirmation allows proof; no automatic device disposal occurs.</summary>
    /// <returns>Completion of native retirement confirmation.</returns>
    [NativeFact]
    public async Task NativeLossLifecycleConfirmsRetirementAsync()
    {
        using var device = VulkanDevice.Create();
        using IGraphicsBuffer<uint> child = device.CreateBuffer(new BufferDesc<uint> { Count = 4, Usage = BufferUsage.CopyDestination });
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new CommandBufferDesc());
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);

        // Fault injection exercises the backend's native-loss lifecycle seam. It does
        // not claim to induce actual hardware loss: DeviceWaitIdle still runs on a
        // real Vulkan device and must succeed before the adapter can issue proof.
        typeof(VulkanDevice).GetMethod("ReportLoss", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(device, ["Injected native loss lifecycle test."]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => device.RetirementAuthority.WaitForRetirementAsync(cancellation.Token).AsTask());
        GraphicsDeviceRetirementProof proof = await device.RetirementAuthority.WaitForRetirementAsync();
        Assert.Same(device.RetirementAuthority.Generation, proof.Generation);
        typeof(GraphicsDeviceRetirementProof).GetMethod("ValidateFor", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(proof, [device.RetirementAuthority, device, proof.Generation]);
        InvokeRejected(typeof(GraphicsDeviceRetirementProof), "ValidateFor", proof, new BorrowedAuthority(device.RetirementAuthority), device, proof.Generation);
        using var other = VulkanDevice.Create();
        InvokeRejected(typeof(GraphicsDeviceRetirementProof), "ValidateFor", proof, other.RetirementAuthority, other, other.RetirementAuthority.Generation);
        Assert.Equal(GraphicsDeviceStatus.Lost, device.Status);
        submission.Dispose();
        commands.Dispose();
        child.Dispose();
    }

    private static void InvokeRejected(Type type, string name, object? instance, params object[] arguments)
    {
        MethodInfo method = type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)!;
        TargetInvocationException error = Assert.Throws<TargetInvocationException>(() => method.Invoke(instance, arguments));
        Assert.IsType<ArgumentException>(error.InnerException);
    }

    private sealed class BorrowedAuthority(IGraphicsDeviceRetirementAuthority real) : IGraphicsDeviceRetirementAuthority
    {
        public IGraphicDevice Device => real.Device;

        public GraphicsDeviceGeneration Generation => real.Generation;

        public ValueTask<GraphicsDeviceRetirementProof> WaitForRetirementAsync(CancellationToken cancellationToken = default)
            => real.WaitForRetirementAsync(cancellationToken);
    }
}
