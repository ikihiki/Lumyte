using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Samples;
using Lumyte.Graphics.Shared;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Exercises ordinary nonpartial unmanaged values without a transfer interface.</summary>
public sealed class UnmanagedShaderTransferTests
{
    /// <summary>Padding, explicit offsets and element stride remain the actual managed representation.</summary>
    [Fact]
    public void OrdinaryStructRetainsPaddingAndStride()
    {
        var values = new PaddedValue[2];
        Span<byte> bytes = MemoryMarshal.AsBytes(values.AsSpan());
        bytes.Fill(0xA5);
        values[0].Header = 7;
        values[0].Position = new(11, 12, 13);
        values[0].Tail = 19;
        values[1].Header = 23;
        byte[] expected = bytes[..32].ToArray();
        ShaderValueSnapshot snapshot = ShaderTransfer.Capture(in values[0]);
        bytes.Clear();

        Assert.False(typeof(IShaderArguments).IsAssignableFrom(typeof(PaddedValue)));
        Assert.Equal(32, Unsafe.SizeOf<PaddedValue>());
        Assert.Equal(64, bytes.Length);
        Assert.Equal(expected, snapshot.Data.ToArray());
        Assert.Equal(0xA5, snapshot.Data.Span[4]);
        Assert.Equal(0xA5, snapshot.Data.Span[31]);
        Assert.Empty(snapshot.Values);
        var layout = new BufferLayout<PaddedValue>(32, 32, 4, 4);
        Assert.Equal(64UL, layout.GetSizeInBytes(2));
        Assert.Empty(snapshot.Fields);
        Assert.Null(snapshot.DiagnosticFields);
        Assert.Null(snapshot.DiagnosticSource);
        Assert.Equal(typeof(PaddedValue), snapshot.DiagnosticType);
        IReadOnlyList<ShaderTransferField> fields = snapshot.GetDiagnosticFields();
        Assert.Contains(fields, field => field.Path == "Position" && field.Offset == 8 && field.Size == 12);
        Assert.Same(ShaderTransfer.Fields<PaddedValue>(), fields);
        Assert.Same(ShaderTransfer.Fields(typeof(PaddedValue)), fields);
    }

    /// <summary>External System.Numerics types need neither registration nor generated implementations.</summary>
    [Fact]
    public void ExternalNumericValuesAreCopiedWithoutConversion()
    {
        var value = Matrix4x4.CreateTranslation(11, 12, 13);
        ShaderValueSnapshot snapshot = ShaderTransfer.Capture(in value, "camera", "CameraMatrix");
        Assert.Equal("camera", snapshot.RootParameter);
        Assert.Equal("CameraMatrix", snapshot.ShaderTypeName);
        Assert.Equal(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in value, 1)).ToArray(), snapshot.Data.ToArray());
        Assert.Equal("float3", ShaderTransfer.TypeName<Vector3>());
        Assert.Equal("float4x4", ShaderTransfer.TypeName<Matrix4x4>());
    }

    /// <summary>Normal SetArguments overload resolution selects the constrained raw path.</summary>
    [Fact]
    public void ComputeEncoderAcceptsOrdinaryStructAndCapturesImmediately()
    {
        var target = new CaptureEncoder();
        IComputeEncoder encoder = target;
        var value = new PaddedValue { Header = 17 };
        encoder.SetArguments(in value);
        value.Header = 29;
        Assert.Equal(17u, MemoryMarshal.Read<uint>(target.Storage!.Bytes));
        Assert.Equal("PaddedValue", target.Root!.ShaderTypeName);
        encoder.SetArguments(in value, rootParameter: "scene", shaderTypeName: "Scene");
        Assert.Equal("scene", target.Root!.RootParameter);
        Assert.Equal("Scene", target.Root!.ShaderTypeName);
    }

    /// <summary>External numeric elements traverse explicit upload and readback on Vulkan.</summary>
    /// <returns>The completed transfer check.</returns>
    [GpuFact]
    public async Task VulkanExternalNumericElementsTransferWithoutGeneratorAsync()
    {
        using var device = VulkanDevice.Create();
        await TransferExternalElementsAsync(device);
    }

    /// <summary>External numeric elements traverse explicit upload and readback on Wgpu.</summary>
    /// <returns>The completed transfer check.</returns>
    [GpuFact]
    public async Task WgpuExternalNumericElementsTransferWithoutGeneratorAsync()
    {
        using var device = WgpuDevice.Create();
        await TransferExternalElementsAsync(device);
    }

    /// <summary>Enabled managed diagnostics permit a valid Vulkan upload and readback.</summary>
    /// <returns>The completed transfer check.</returns>
    [GpuFact]
    public async Task VulkanValidatedExternalElementsAsync()
    {
        using var device = VulkanDevice.Create();
        device.EnableValidation();
        await TransferExternalElementsAsync(device);
    }

    /// <summary>Enabled managed diagnostics permit a valid Wgpu upload and readback.</summary>
    /// <returns>The completed transfer check.</returns>
    [GpuFact]
    public async Task WgpuValidatedExternalElementsAsync()
    {
        using var device = WgpuDevice.Create();
        device.EnableValidation();
        await TransferExternalElementsAsync(device);
    }

    private static async Task TransferExternalElementsAsync(IGraphicDevice device)
    {
        var artifact = ShaderArtifact.LoadEmbedded(typeof(BindingVector).Assembly, "Lumyte.Shaders.binding-layout.lshader");
        using IGraphicsBuffer<Vector3> upload = device.CreateShaderDataBuffer<Vector3>(artifact, 2, MemoryPreference.Upload);
        using IGraphicsBuffer<Vector3> storage = device.CreateShaderDataBuffer<Vector3>(artifact, 2, usage: BufferUsage.CopyDestination | BufferUsage.CopySource | BufferUsage.ShaderRead);
        using IGraphicsBuffer<Vector3> readback = device.CreateBuffer<Vector3>(new() { Count = 2, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        Vector3[] values = [new(11, 12, 13), new(21, 22, 23)];
        byte[] expected = MemoryMarshal.AsBytes(values.AsSpan()).ToArray();
        Assert.Equal(12UL, storage.Layout.ElementStrideInBytes);
        Assert.Equal(24UL, storage.SizeInBytes);
        Assert.False(storage is IShaderDataSource);
        await upload.MapAsync();
        upload.CopyFrom(values);
        values.AsSpan().Clear();
        upload.Unmap();
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        commands.Barrier(new BufferBarrierDesc<Vector3> { Buffer = upload.SliceElements(0, 2), Before = new(PipelineStage.Host, ResourceAccess.HostWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
        commands.CopyBuffer(upload.SliceElements(0, 2), storage.SliceElements(0, 2));
        commands.Barrier(new BufferBarrierDesc<Vector3> { Buffer = storage.SliceElements(0, 2), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
        commands.CopyBuffer(storage.SliceElements(0, 2), readback.SliceElements(0, 2));
        commands.Barrier(new BufferBarrierDesc<Vector3> { Buffer = readback.SliceElements(0, 2), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Host, ResourceAccess.HostRead) });
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        await submission.WaitAsync();
        await readback.MapAsync();
        readback.CopyTo(values);
        readback.Unmap();
        Assert.Equal(expected, MemoryMarshal.AsBytes(values.AsSpan()).ToArray());
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct PaddedValue
    {
        [FieldOffset(0)]
        public uint Header;

        [FieldOffset(8)]
        public Vector3 Position;

        [FieldOffset(24)]
        public uint Tail;
    }

    private sealed class CaptureEncoder : IComputeEncoder
    {
        public ShaderRootArguments? Root { get; private set; }

        public TestRootStorage? Storage { get; private set; }

        public void SetArguments<T>(in T arguments)
            where T : IShaderArguments => throw new InvalidOperationException("The numeric path must not call an interface serializer.");

        public void SetArguments<T>(in T arguments, string rootParameter = "arguments", string? shaderTypeName = null)
            where T : unmanaged => Root = ShaderRootArguments.CreateUnmanaged(in arguments, rootParameter, shaderTypeName, size => Storage = new TestRootStorage(size));

        public void SetArgumentValidation(Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>? prepare)
        {
        }

        public void SetArgumentTable(IArgumentTable table) => throw new NotSupportedException();

        public void SetPipeline(IGraphicsComputePipeline pipeline) => throw new NotSupportedException();

        public void Dispatch(uint groupCountX, uint groupCountY = 1, uint groupCountZ = 1) => throw new NotSupportedException();

        public void DispatchIndirect(BufferSlice<DispatchIndirectArguments> arguments) => throw new NotSupportedException();

        public void End() => throw new NotSupportedException();
    }
}
