using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Planning.Tests;

/// <summary>Physical exposure must not invent unsupported stages or storage accesses.</summary>
public sealed class PhysicalBindingScopeTests
{
    /// <summary>Conservative dependencies retain legal writes without inventing host or read access.</summary>
    /// <param name="write">Whether the descriptor is writable rather than read-only.</param>
    /// <returns>The completed structural assertion.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PhysicalScopesUseAvailableStagesAndExactDescriptorAccessAsync(bool write)
    {
        await using var host = new PlanningHost();
        var native = new ManagedDevice();
        await using var session = new SessionOwner(host.Module, native);
        await using var frame = new FrameOwner(session.CreateFrame());
        ComputeKernel<TestArguments> kernel = session.ImportComputeKernel<TestArguments>(new ManagedKernel(), new TestLease());
        PlanBuffer<int> buffer = frame.Plan.ImportBuffer(new ManagedBuffer<int>(4, native.Events), new TestLease());
        PlanPass pass = frame.Plan.AddPass("physical-scope");
        ResourceAccess access = write ? ResourceAccess.ShaderWrite : ResourceAccess.ShaderRead;
        if (write)
        {
            buffer = pass.Write(buffer, buffer.Range, PlanWriteMode.Discard, new(PipelineStage.ComputeShader, access));
        }
        else
        {
            pass.Read(buffer, buffer.Range, new(PipelineStage.ComputeShader, access));
        }

        var arguments = new TestArguments(pass.Buffer(buffer, buffer.Range, write ? ArgumentBufferAccess.Write : ArgumentBufferAccess.Read));
        pass.Dispatch(kernel, arguments, 1);
        pass.KeepAlive();
        using CompiledGraphicsPlan compiled = await frame.CompileAsync();
        await using var prepared = new PreparedOwner(await compiled.PrepareAsync());
        await using var execution = new ExecutionOwner(session.CreateExecution(prepared.Value));
        await execution.Value.RecordAsync();
        MemoryBarrierDesc barrier = Assert.Single(native.Commands!.Barriers);
        Assert.Equal(PipelineStage.None, barrier.Before.Stages & (PipelineStage.MeshShader | PipelineStage.TaskShader));
        Assert.Equal(ResourceAccess.None, barrier.Before.Access & ResourceAccess.HostWrite);
        Assert.Equal(PipelineStage.ComputeShader, barrier.After.Stages);
        Assert.Equal(access, barrier.After.Access);
    }
}
