using System.Numerics;
using Lumyte.Graphics.Passes;
using Lumyte.Graphics.Portable.RenderGraph;
using Lumyte.Graphics.Portable.Resources.Tests.Unit.Management;
using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.Portable.Passes.Tests;

public sealed class PortableModelPassTests
{
    [Fact]
    public async Task RetryReleasesShaderObjectsAfterPipelineCreationFails()
    {
        var failure = new InvalidOperationException("Injected pipeline failure.");
        var backend = new ManagerTestBackend { RasterCreationError = failure };
        var registry = new PortableRenderPassRegistry().AddModelRendering();
        var provider = new PortableRenderProvider("test", (_, _) => ValueTask.FromResult<IPortableGpuBackend>(backend), registry);
        var geometry = new ModelGeometryData(new("triangle", 0), ModelTopology.Triangles,
            new(new(new("positions", 0), new Vector3[] { new(-1, -1, 0), new(1, -1, 0), new(0, 1, 0) })));
        var image = new GpuImageUploadData(new("environment", 0), new(1, 1, GpuFormat.Rgba8Unorm), GpuImageColorEncoding.Linear,
            GpuImageAlphaMode.Opaque, [new(0, 0, 4, 4, new byte[] { 255, 255, 255, 255 })]);
        var snapshot = new ModelRenderSnapshot(ModelCamera.Orthographic(new(0, 0, 3), Vector3.Zero, Vector3.UnitY, 2, .1f, 10),
            ModelDrawSnapshot.From([new(geometry, new(new("material", 0)), Matrix4x4.Identity)]), new([], new(image)));
        var graph = new GpuRenderGraph(); var input = graph.CreateInput("model", ModelRenderInputContract.Instance, snapshot);
        var color = graph.CreateTexture("color", new(4, 4, GpuFormat.Rgba16Float));
        var depth = graph.CreateTexture("depth", new(4, 4, GpuFormat.D32Float));
        graph.AddModelPass("model", new(input, color, depth)); graph.MarkOutput(color);
        var plan = graph.Compile();

        await using (var runtime = await provider.CreateAsync(new()))
        {
            Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(async () => await runtime.SubmitAsync(plan)));
            backend.RasterCreationError = null;
            using var retry = await runtime.SubmitAsync(plan);
            await retry.WaitForCompletionAsync();
        }

        Assert.All(backend.Created.Where(resource => resource is GpuShaderModuleHandle or GpuBindingLayoutHandle),
            resource => Assert.Single(backend.Destroyed, destroyed => ReferenceEquals(resource, destroyed)));
    }
}
