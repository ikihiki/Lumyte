using Lumyte.Memory;
using StbImageSharp;

namespace Lumyte.Resources.Gltf.Sample;

internal sealed class ImageStage : IResourceSourceStage<CpuImage>
{
    public async ValueTask<CpuImage> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
    {
        ResourceSourceSnapshot snapshot = await context.OpenSourceAsync(context.Address.Id, cancellationToken: cancellationToken);
        await using Stream stream = await snapshot.OpenReadAsync(cancellationToken: cancellationToken);
        var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
        cancellationToken.ThrowIfCancellationRequested();
        IMemoryBuffer<byte> pixels = await context.GetMemoryScope(ResourceDependencyLifetime.Resident).RentAsync<byte>(new("gltf.cpu"), image.Data.Length, MemoryPurpose.Resident, cancellationToken);
        image.Data.CopyTo(pixels.Memory.Span);
        return new(image.Width, image.Height, pixels);
    }

    public ValueTask ReleaseAsync(CpuImage resource) => ValueTask.CompletedTask;
}
