using Lumyte.Graphics.Native.Resources.Tests;

namespace Lumyte.Graphics.Native.RenderGraph.Tests;

public sealed class NativeImportedTextureTests
{
    private static NativeGpuTextureDescription Description => new(NativeGpuTextureDimension.TwoD, 4, 4, 1, 1, 1, 1,
        GpuFormat.Rgba16Float, NativeGpuTextureUsage.CopyDestination | NativeGpuTextureUsage.Sampled);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FreshImportedTexturesAreDiscardedBeforeTheirFirstWrite(bool explicitTransitions)
    {
        var backend = new TestResourceBackend { ExplicitTransitions = explicitTransitions };
        await using var runtime = await NativeGraphPlanningTests.Create(backend, (context, request) =>
        {
            using var scope = context.Services.Resources.CreateScope();
            var reference = scope.CreateTexture(Description);
            var texture = context.ImportTexture(reference, Description, discardContents: true);
            context.AddPass("initialize", 0, (_, _) => { }).Write(texture, new(GpuStage.Copy, GpuAccess.CopyWrite));
            var same = context.ImportTexture(reference, Description);
            context.AddPass("consume", 0, (_, _) => { }).Read(same, new(GpuStage.PixelShader, GpuAccess.ShaderRead))
                .Write(context.ImportBuffer(request.Target), new(GpuStage.Copy, GpuAccess.CopyWrite));
        });

        using var execution = await runtime.SubmitAsync(NativeGraphPlanningTests.Plan());
        await execution.WaitForCompletionAsync();

        Assert.Equal(explicitTransitions ? "Discard:CopyDestination" : "Discard:General",
            Assert.Single(backend.Commands, command => command.StartsWith("Discard:", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DiscardedImportsRequireInitializationBeforeReading(bool readWrite)
    {
        await using var runtime = await NativeGraphPlanningTests.Create(new(), (context, request) =>
        {
            using var scope = context.Services.Resources.CreateScope();
            var texture = context.ImportTexture(scope.CreateTexture(Description), Description, discardContents: true);
            var pass = context.AddPass("consume", 0, (_, _) => { }).Write(context.ImportBuffer(request.Target), new(GpuStage.Copy, GpuAccess.CopyWrite));
            if (readWrite) { pass.ReadWrite(texture, new(GpuStage.Copy, GpuAccess.CopyWrite)); }
            else { pass.Read(texture, new(GpuStage.PixelShader, GpuAccess.ShaderRead)); }
        });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => await runtime.SubmitAsync(NativeGraphPlanningTests.Plan()));

        Assert.Contains("reads uninitialized", error.Message);
    }

    [Fact]
    public async Task InitializedImportSchedulesCannotHideDiscardedReads()
    {
        bool discard = false;
        await using var runtime = await NativeGraphPlanningTests.Create(new(), (context, request) =>
        {
            using var scope = context.Services.Resources.CreateScope();
            var texture = context.ImportTexture(scope.CreateTexture(Description), Description, discard);
            context.AddPass("consume", 0, (_, _) => { }).Read(texture, new(GpuStage.PixelShader, GpuAccess.ShaderRead))
                .Write(context.ImportBuffer(request.Target), new(GpuStage.Copy, GpuAccess.CopyWrite));
        });
        var plan = NativeGraphPlanningTests.Plan();
        using var first = await runtime.SubmitAsync(plan);
        await first.WaitForCompletionAsync();

        discard = true;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => await runtime.SubmitAsync(plan));

        Assert.Contains("reads uninitialized", error.Message);
    }
}
