using System.Buffers.Binary;
using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Planning.Tests;

/// <summary>Checks typed captured payloads, native mapping boundaries and transitive table coherence.</summary>
public sealed class ShaderDataConsumerTests
{
    /// <summary>Declaration capture freezes values, patches genuine references and shares the exact payload/root table domain.</summary>
    /// <returns>The asynchronous completion.</returns>
    [Fact]
    public async Task CapturesAndPatchesDataBeforeRecordingAsync()
    {
        await using var host = new PlanningHost();
        var native = new ManagedDevice();
        await using var session = new SessionOwner(host.Module, native);
        await using var frame = new FrameOwner(session.CreateFrame());
        ComputeKernel<DataRoot> kernel = session.ImportComputeKernel<DataRoot>(new ManagedKernel(), new TestLease());
        PlanBuffer<int> source = frame.Plan.ImportBuffer(new ManagedBuffer<int>(4, []), new TestLease());
        PlanShaderData<DataElement> data = frame.Plan.CreateShaderData<DataElement>(Artifact(), 2);
        PlanPass upload = frame.Plan.AddPass("upload");
        data = upload.Write(data, data.Range, PlanWriteMode.Discard, new(PipelineStage.Copy, ResourceAccess.CopyWrite));
        PlanPass consume = frame.Plan.AddPass("consume");
        consume.Read(source, source.Range, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead));
        consume.Read(data, data.Range, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead));
        PlanGpuRef<int> leaf = consume.Buffer(source, source.Range);
        var probe = new CaptureProbe();
        DataElement[] values = [new(leaf, 37, probe), new(leaf, 53, probe)];
        upload.UploadShaderData(data, data.Range, values);
        values[0] = new(leaf, 999, probe);
        consume.Dispatch(kernel, new DataRoot(consume.ShaderData(data, data.Range)), 1);
        consume.KeepAlive();
        Assert.Equal(2, probe.Calls);
        Assert.Empty(native.Events);
        using CompiledGraphicsPlan compiled = await frame.CompileAsync();
        await using var prepared = new PreparedOwner(await compiled.PrepareAsync());
        Assert.Equal(2, probe.Calls);
        ManagedShaderDataBuffer<DataElement> staging = native.ShaderDataAllocations.OfType<ManagedShaderDataBuffer<DataElement>>().Single(b => b.Memory == MemoryPreference.Upload);
        Assert.False(staging.IsMapped);
        Assert.Equal(37u, BinaryPrimitives.ReadUInt32LittleEndian(staging.EncodedValues[0].Bytes));
        Assert.Equal(53u, BinaryPrimitives.ReadUInt32LittleEndian(staging.EncodedValues[1].Bytes));
        Assert.DoesNotContain(native.Events, e => e.StartsWith("copy-shaderdata:", StringComparison.Ordinal));
        await using var execution = new ExecutionOwner(session.CreateExecution(prepared.Value));
        int preparedEvents = native.Events.Count;
        await execution.Value.RecordAsync();
        Assert.DoesNotContain(native.Events.Skip(preparedEvents), e => e.StartsWith("allocate-", StringComparison.Ordinal) || e.StartsWith("register-", StringComparison.Ordinal) || e.Contains("map-shaderdata", StringComparison.Ordinal) || e == "write-shaderdata");
        Assert.Contains("copy-shaderdata:0:0:2", native.Events);
        IShaderReference root = Assert.IsAssignableFrom<IShaderReference>(Assert.Single(native.Commands!.References));
        Assert.Same(root.Table, Assert.Single(staging.EncodedValues[0].References).Table);
        Assert.Equal(2, prepared.Value.PhysicalBindings.Count(b => b.Operation == 1));
        Assert.True(native.Events.IndexOf("unmap-shaderdata") < native.Events.IndexOf("copy-shaderdata:0:0:2"));
    }

    /// <summary>Every payload-reachable target needs an explicit consumer access declaration.</summary>
    /// <returns>The asynchronous completion.</returns>
    [Fact]
    public async Task RejectsUndeclaredTransitiveTargetAsync()
    {
        await using var host = new PlanningHost();
        await using var session = new SessionOwner(host.Module, new ManagedDevice());
        await using var frame = new FrameOwner(session.CreateFrame());
        PlanBuffer<int> source = frame.Plan.ImportBuffer(new ManagedBuffer<int>(4, []), new TestLease());
        PlanPass referenceScope = frame.Plan.AddPass("reference-only");
        referenceScope.Read(source, source.Range, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead));
        PlanGpuRef<int> leaf = referenceScope.Buffer(source, source.Range);
        PlanShaderData<DataElement> data = frame.Plan.CreateShaderData<DataElement>(Artifact(), 1);
        PlanPass upload = frame.Plan.AddPass("upload");
        data = upload.Write(data, data.Range, PlanWriteMode.Discard, new(PipelineStage.Copy, ResourceAccess.CopyWrite));
        upload.UploadShaderData(data, data.Range, new DataElement[] { new(leaf, 37, null) });
        PlanPass consume = frame.Plan.AddPass("consume");
        consume.Read(data, data.Range, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead));
        Assert.Throws<InvalidOperationException>(() => consume.ShaderData(data, data.Range));
    }

    /// <summary>Actual mapping completion is retained and awaited outside the fixed owner before cancel/failure cleanup.</summary>
    /// <param name="cancel">Whether cancellation, rather than a typed transfer exception, ends preparation.</param>
    /// <returns>The asynchronous completion.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MappingDoesNotBlockOwnerAndCleanupUnmapsAsync(bool cancel)
    {
        await using var host = new PlanningHost();
        var native = new ManagedDevice { ShaderDataMapGate = new(TaskCreationOptions.RunContinuationsAsynchronously), ShaderDataWriteFailures = cancel ? 0 : 1 };
        await using var session = new SessionOwner(host.Module, native);
        await using var frame = new FrameOwner(session.CreateFrame());
        PlanShaderData<Number> data = frame.Plan.CreateShaderData<Number>(Artifact(), 1);
        PlanPass upload = frame.Plan.AddPass("upload");
        data = upload.Write(data, data.Range, PlanWriteMode.Discard, new(PipelineStage.Copy, ResourceAccess.CopyWrite));
        upload.UploadShaderData(data, data.Range, new Number[] { new(42) });
        frame.Plan.Export(data, new BarrierScope(PipelineStage.ComputeShader, ResourceAccess.ShaderRead));
        using CompiledGraphicsPlan compiled = await frame.CompileAsync();
        using var cancellation = new CancellationTokenSource();
        Task<PreparedGraphicsExecution> preparing = compiled.PrepareAsync(cancellation.Token).AsTask();
        await native.ShaderDataMapStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.Value.RetireAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(preparing.IsCompleted);
        Assert.DoesNotContain("dispose-shaderdata", native.Events);
        if (cancel)
        {
            cancellation.Cancel();
        }

        native.ShaderDataMapGate.TrySetResult();
        if (cancel)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await preparing);
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await preparing);
        }

        Assert.Contains("unmap-shaderdata", native.Events);
        Assert.True(native.Events.IndexOf("unmap-shaderdata") < native.Events.IndexOf("dispose-shaderdata"));
        Assert.DoesNotContain("submit", native.Events);
        Assert.All(native.ShaderDataAllocations.OfType<ManagedShaderDataBuffer<Number>>(), b => Assert.False(b.IsMapped));
    }

    /// <summary>Native typed GPU copies preserve original element identity and producer barriers.</summary>
    /// <returns>The asynchronous completion.</returns>
    [Fact]
    public async Task CopiesTypedDataThroughOriginalNativeBuffersAsync()
    {
        await using var host = new PlanningHost();
        var native = new ManagedDevice();
        await using var session = new SessionOwner(host.Module, native);
        await using var frame = new FrameOwner(session.CreateFrame());
        PlanShaderData<Number> a = frame.Plan.CreateShaderData<Number>(Artifact(), 2);
        PlanShaderData<Number> b = frame.Plan.CreateShaderData<Number>(Artifact(), 2);
        PlanPass upload = frame.Plan.AddPass("upload");
        a = upload.Write(a, a.Range, PlanWriteMode.Discard, new(PipelineStage.Copy, ResourceAccess.CopyWrite));
        upload.UploadShaderData(a, a.Range, new Number[] { new(1), new(2) });
        PlanPass copy = frame.Plan.AddPass("copy");
        copy.Read(a, a.Range, new(PipelineStage.Copy, ResourceAccess.CopyRead));
        b = copy.Write(b, b.Range, PlanWriteMode.Discard, new(PipelineStage.Copy, ResourceAccess.CopyWrite));
        copy.CopyShaderData(a, a.Range, b, b.Range);
        frame.Plan.Export(b, new BarrierScope(PipelineStage.ComputeShader, ResourceAccess.ShaderRead));
        using CompiledGraphicsPlan compiled = await frame.CompileAsync();
        Assert.Contains(compiled.Barriers, barrier => barrier.Before == upload && barrier.After == copy);
        await using var prepared = new PreparedOwner(await compiled.PrepareAsync());
        GraphicsExportLease<IGraphicsShaderDataBuffer<Number>> export = prepared.Value.ExportShaderData(b);
        await using var execution = new ExecutionOwner(session.CreateExecution(prepared.Value));
        await execution.Value.ExecuteAsync();
        Assert.Equal(2, native.Events.Count(e => e == "copy-shaderdata:0:0:2"));
        Assert.IsType<ManagedShaderDataBuffer<Number>>(export.Value);
        await export.RetireAsync();
    }

    private static ShaderArtifact Artifact() => ShaderArtifact.PackTarget(ShaderTarget.Wgsl, ShaderStage.Compute, "main", "managed-test", "@compute @workgroup_size(1) fn main() {}"u8, "{}");

    private readonly record struct Number(uint Value) : IShaderArguments
    {
        public static string ShaderTypeName => "Number";

        public static int TransferSize => 4;

        public static int ReferenceCount => 0;

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter, allows ref struct
        {
            byte[] bytes = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, Value);
            writer.WriteValue(bytes);
        }
    }

    private readonly record struct DataElement(IGpuRef<int> Source, uint Value, CaptureProbe? Probe) : IShaderArguments
    {
        public static string ShaderTypeName => "DataElement";

        public static int TransferSize => 20;

        public static int ReferenceCount => 1;

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter, allows ref struct
        {
            if (Probe is not null)
            {
                Probe.Calls++;
            }

            writer.WriteReference(Source);
            byte[] bytes = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, Value);
            writer.WriteValue(bytes);
        }
    }

    private readonly record struct DataRoot(IGpuRef<DataElement> Data) : IShaderArguments
    {
        public static string ShaderTypeName => "DataRoot";

        public static int TransferSize => 16;

        public static int ReferenceCount => 1;

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter, allows ref struct => writer.WriteReference(Data);
    }

    private sealed class CaptureProbe
    {
        internal int Calls { get; set; }
    }
}
