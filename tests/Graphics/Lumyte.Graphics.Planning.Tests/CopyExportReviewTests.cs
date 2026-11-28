using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Planning.Tests;

/// <summary>Checks operation-specific copy scopes and immutable export observations.</summary>
public sealed class CopyExportReviewTests
{
    /// <summary>Copy stages and access bits must coexist on a matching operand use.</summary>
    /// <param name="operand">The operand with the malformed declaration.</param>
    /// <param name="wrongStage">Whether only the stage is incorrect.</param>
    /// <returns>The asynchronous completion.</returns>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task BufferCopyRejectsNonCopyScopesAsync(bool operand, bool wrongStage)
    {
        await using var host = new PlanningHost();
        await using var session = new SessionOwner(host.Module, new ManagedDevice());
        await using var frame = new FrameOwner(session.CreateFrame());
        PlanBuffer<int> source = frame.Plan.ImportBuffer(new ManagedBuffer<int>(4, []), new TestLease());
        PlanBuffer<int> destination = frame.Plan.CreateBuffer<int>(new() { Count = 4, Usage = BufferUsage.CopyDestination });
        PlanPass copy = frame.Plan.AddPass("copy");
        BarrierScope badRead = new(wrongStage ? PipelineStage.ComputeShader : PipelineStage.Copy, wrongStage ? ResourceAccess.CopyRead : ResourceAccess.ShaderRead);
        BarrierScope badWrite = new(wrongStage ? PipelineStage.ComputeShader : PipelineStage.Copy, wrongStage ? ResourceAccess.CopyWrite : ResourceAccess.ShaderWrite);
        copy.Read(source, source.Range, operand ? new(PipelineStage.Copy, ResourceAccess.CopyRead) : badRead);
        destination = copy.Write(destination, destination.Range, PlanWriteMode.Discard, operand ? badWrite : new(PipelineStage.Copy, ResourceAccess.CopyWrite));
        Assert.Throws<InvalidOperationException>(() => copy.Copy(source, source.Range, destination, destination.Range));
    }

    /// <summary>A GPU-written source retains a dependency targeting the actual copy stage and access.</summary>
    /// <returns>The asynchronous completion.</returns>
    [Fact]
    public async Task BufferCopyPreservesCopyBarrierAsync()
    {
        await using var host = new PlanningHost();
        await using var session = new SessionOwner(host.Module, new ManagedDevice());
        await using var frame = new FrameOwner(session.CreateFrame());
        PlanBuffer<int> source = frame.Plan.ImportBuffer(new ManagedBuffer<int>(4, []), new TestLease());
        ComputeKernel<NoShaderArguments> kernel = session.ImportComputeKernel<NoShaderArguments>(new ManagedKernel(), new TestLease());
        PlanPass produce = frame.Plan.AddPass("produce");
        source = produce.Write(source, source.Range, PlanWriteMode.Discard, new(PipelineStage.ComputeShader, ResourceAccess.ShaderWrite));
        produce.Dispatch(kernel, default, 1);
        PlanBuffer<int> target = frame.Plan.CreateBuffer<int>(new() { Count = 4, Usage = BufferUsage.CopyDestination });
        PlanPass copy = frame.Plan.AddPass("copy");
        copy.Read(source, source.Range, new(PipelineStage.Copy, ResourceAccess.CopyRead));
        target = copy.Write(target, target.Range, PlanWriteMode.Discard, new(PipelineStage.Copy, ResourceAccess.CopyWrite));
        copy.Copy(source, source.Range, target, target.Range);
        frame.Plan.Export(target);
        using CompiledGraphicsPlan compiled = await frame.CompileAsync();
        PlanBarrier barrier = Assert.Single(compiled.Barriers, b => b.Before == produce && b.After == copy);
        Assert.Equal(new BarrierScope(PipelineStage.Copy, ResourceAccess.CopyRead), barrier.Destination.Scope);
    }

    /// <summary>Typed copies enforce their source copy scope, not merely logical read direction.</summary>
    /// <returns>The asynchronous completion.</returns>
    [Fact]
    public async Task ShaderDataCopyRejectsShaderReadAsync()
    {
        await using var host = new PlanningHost();
        await using var session = new SessionOwner(host.Module, new ManagedDevice());
        await using var frame = new FrameOwner(session.CreateFrame());
        PlanShaderData<TestArguments> source = Upload(frame, default);
        PlanShaderData<TestArguments> destination = frame.Plan.CreateShaderData<TestArguments>(Artifact(), 1);
        PlanPass copy = frame.Plan.AddPass("copy");
        copy.Read(source, source.Range, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead));
        destination = copy.Write(destination, destination.Range, PlanWriteMode.Discard, new(PipelineStage.Copy, ResourceAccess.CopyWrite));
        Assert.Throws<InvalidOperationException>(() => copy.CopyShaderData(source, source.Range, destination, destination.Range));
    }

    /// <summary>All structured texture transfer operands require copy synchronization.</summary>
    /// <param name="kind">Texture, buffer-to-texture, or texture-to-buffer transfer.</param>
    /// <param name="destinationBad">Whether the destination declaration is malformed.</param>
    /// <param name="valid">Whether both operands have valid copy scopes.</param>
    /// <returns>The asynchronous completion.</returns>
    [Theory]
    [InlineData(0, false, false)]
    [InlineData(0, true, false)]
    [InlineData(1, false, false)]
    [InlineData(1, true, false)]
    [InlineData(2, false, false)]
    [InlineData(2, true, false)]
    [InlineData(0, false, true)]
    [InlineData(1, false, true)]
    [InlineData(2, false, true)]
    public async Task TextureTransfersRequireCopyScopesAsync(int kind, bool destinationBad, bool valid)
    {
        await using var host = new PlanningHost();
        await using var session = new SessionOwner(host.Module, new ManagedDevice());
        await using var frame = new FrameOwner(session.CreateFrame());
        PlanTexture source = frame.Plan.ImportTexture(new ManagedTexture(new() { Width = 4, Height = 4, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.CopySource }, []), new TestLease(), initialState: TextureState.CopySource);
        PlanTexture target = frame.Plan.CreateTexture(new() { Width = 4, Height = 4, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.CopyDestination });
        PlanBuffer<byte> buffer = frame.Plan.ImportBuffer(new ManagedBuffer<byte>(64, []), new TestLease());
        PlanPass copy = frame.Plan.AddPass("copy");
        BarrierScope read = valid || destinationBad ? new(PipelineStage.Copy, ResourceAccess.CopyRead) : new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead);
        BarrierScope write = valid || !destinationBad ? new(PipelineStage.Copy, ResourceAccess.CopyWrite) : new(PipelineStage.ComputeShader, ResourceAccess.ShaderWrite);
        PlanTextureRegion region = new(0, 0, 0, 0, 4, 4);
        Action declare;
        if (kind == 0)
        {
            copy.Read(source, source.Range, read, TextureState.CopySource);
            target = copy.Write(target, target.Range, PlanWriteMode.Discard, write, TextureState.CopyDestination);
            declare = () => copy.CopyTexture(source, region, target, region);
        }
        else if (kind == 1)
        {
            copy.Read(buffer, buffer.Range, read);
            target = copy.Write(target, target.Range, PlanWriteMode.Discard, write, TextureState.CopyDestination);
            declare = () => copy.CopyBufferToTexture(buffer, buffer.Range, 16, 4, target, region);
        }
        else
        {
            copy.Read(source, source.Range, read, TextureState.CopySource);
            buffer = copy.Write(buffer, buffer.Range, PlanWriteMode.Discard, write);
            declare = () => copy.CopyTextureToBuffer(source, region, buffer, buffer.Range, 16, 4);
        }

        if (valid)
        {
            declare();
            copy.KeepAlive();
            using CompiledGraphicsPlan compiled = await frame.CompileAsync();
            Assert.Single(compiled.Passes);
        }
        else
        {
            Assert.Throws<InvalidOperationException>(declare);
        }
    }

    /// <summary>Exported views only reject writes overlapping their exact mip/layer observation.</summary>
    /// <param name="mip">The written mip.</param>
    /// <param name="layer">The written layer.</param>
    /// <returns>The asynchronous completion.</returns>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    public async Task DirectTextureExportChecksOverlapAsync(uint mip, uint layer)
    {
        await using var host = new PlanningHost();
        await using var session = new SessionOwner(host.Module, new ManagedDevice());
        await using var frame = new FrameOwner(session.CreateFrame());
        PlanTexture texture = frame.Plan.ImportTexture(new ManagedTexture(new() { Width = 4, Height = 4, MipLevels = 2, ArrayLayers = 2, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.RenderAttachment | TextureUsage.Sampled }, []), new TestLease(), initialState: TextureState.Sampled);
        PlanTexture exported = frame.Plan.View(texture, new() { BaseMipLevel = 0, MipLevelCount = 1, BaseArrayLayer = 0, ArrayLayerCount = 1 });
        PlanTexture written = frame.Plan.View(texture, new() { BaseMipLevel = mip, MipLevelCount = 1, BaseArrayLayer = layer, ArrayLayerCount = 1 });
        PlanPass clear = frame.Plan.AddPass("clear");
        written = clear.Write(written, written.Range, PlanWriteMode.Discard, new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite), TextureState.ColorAttachment);
        clear.Clear(written, default);
        frame.Plan.Export(exported);
        if (mip == 0 && layer == 0)
        {
            await Assert.ThrowsAsync<NotSupportedException>(async () => { using CompiledGraphicsPlan compiled = await frame.CompileAsync(); });
        }
        else
        {
            using CompiledGraphicsPlan compiled = await frame.CompileAsync();
            Assert.Single(compiled.Passes);
        }
    }

    /// <summary>Recursive exported read closures cannot silently expose later overlapping writes.</summary>
    /// <param name="transitive">Whether another shader-data reference intervenes.</param>
    /// <param name="overlap">Whether the write overlaps the captured leaf range.</param>
    /// <param name="preserveCopy">Whether an explicit independent copy protects the leaf.</param>
    /// <returns>The asynchronous completion.</returns>
    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public async Task ShaderDataExportChecksRecursiveReadVersionsAsync(bool transitive, bool overlap, bool preserveCopy)
    {
        await using var host = new PlanningHost();
        await using var session = new SessionOwner(host.Module, new ManagedDevice());
        await using var frame = new FrameOwner(session.CreateFrame());
        PlanBuffer<int> original = frame.Plan.ImportBuffer(new ManagedBuffer<int>(4, []), new TestLease());
        PlanBuffer<int> leaf = original;
        PlanRange observed = new(0, 2);
        if (preserveCopy)
        {
            leaf = frame.Plan.CreateBuffer<int>(new() { Count = 4, Usage = BufferUsage.CopyDestination | BufferUsage.ShaderRead });
            PlanPass preserve = frame.Plan.AddPass("preserve");
            preserve.Read(original, observed, new(PipelineStage.Copy, ResourceAccess.CopyRead));
            leaf = preserve.Write(leaf, observed, PlanWriteMode.Discard, new(PipelineStage.Copy, ResourceAccess.CopyWrite));
            preserve.Copy(original, observed, leaf, observed);
            preserve.KeepAlive();
        }

        PlanPass references = frame.Plan.AddPass("references");
        references.Read(leaf, observed, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead));
        PlanShaderData<TestArguments> data = Upload(frame, new(references.Buffer(leaf, observed)));
        PlanResource exported = data;
        if (transitive)
        {
            references.Read(data, data.Range, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead));
            PlanGpuRef<TestArguments> nested = references.ShaderData(data, data.Range);
            PlanShaderData<NestedArguments> root = frame.Plan.CreateShaderData<NestedArguments>(Artifact(), 1);
            PlanPass upload = frame.Plan.AddPass("root-upload");
            root = upload.Write(root, root.Range, PlanWriteMode.Discard, new(PipelineStage.Copy, ResourceAccess.CopyWrite));
            upload.UploadShaderData(root, root.Range, new NestedArguments[] { new(nested) });
            exported = root;
        }

        PlanBuffer<int> replacement = frame.Plan.ImportBuffer(new ManagedBuffer<int>(4, []), new TestLease());
        PlanRange changed = overlap ? new(1, 2) : new(2, 2);
        PlanPass overwrite = frame.Plan.AddPass("overwrite");
        overwrite.Read(replacement, observed, new(PipelineStage.Copy, ResourceAccess.CopyRead));
        PlanBuffer<int> next = overwrite.Write(original, changed, PlanWriteMode.Discard, new(PipelineStage.Copy, ResourceAccess.CopyWrite));
        overwrite.Copy(replacement, observed, next, changed);
        frame.Plan.Export(exported);
        if (overlap && !preserveCopy)
        {
            await Assert.ThrowsAsync<NotSupportedException>(async () => { using CompiledGraphicsPlan compiled = await frame.CompileAsync(); });
        }
        else
        {
            using CompiledGraphicsPlan compiled = await frame.CompileAsync();
            Assert.NotEmpty(compiled.Passes);
        }
    }

    private static PlanShaderData<TestArguments> Upload(FrameOwner frame, TestArguments value)
    {
        PlanShaderData<TestArguments> data = frame.Plan.CreateShaderData<TestArguments>(Artifact(), 1);
        PlanPass upload = frame.Plan.AddPass("upload");
        data = upload.Write(data, data.Range, PlanWriteMode.Discard, new(PipelineStage.Copy, ResourceAccess.CopyWrite));
        upload.UploadShaderData(data, data.Range, new TestArguments[] { value });
        upload.KeepAlive();
        return data;
    }

    private static ShaderArtifact Artifact() => ShaderArtifact.PackTarget(ShaderTarget.Wgsl, ShaderStage.Compute, "main", "managed-test", "@compute @workgroup_size(1) fn main() {}"u8, "{}");

    private readonly record struct NestedArguments(IGpuRef<TestArguments> Data) : IShaderArguments
    {
        public static string ShaderTypeName => "Nested";

        public static int TransferSize => 16;

        public static int ReferenceCount => 1;

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter, allows ref struct => writer.WriteReference(Data);
    }
}
