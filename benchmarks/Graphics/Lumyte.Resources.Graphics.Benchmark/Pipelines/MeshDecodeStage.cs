using Lumyte.Graphics.Drawing;

namespace Lumyte.Resources.Graphics.Benchmark.Pipelines;

/// <summary>Decodes every cold load from an observed stream into newly owned mesh arrays.</summary>
public sealed class MeshDecodeStage : IResourceSourceStage<ModelMeshData>
{
    /// <summary>Gets completed decode count.</summary>
    public int Builds { get; private set; }

    /// <summary>Gets released CPU generation count.</summary>
    public int Releases { get; private set; }

    /// <inheritdoc/>
    public async ValueTask<ModelMeshData> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
    {
        ResourceSourceSnapshot snapshot = await context.OpenSourceAsync(new("triangle"), cancellationToken: cancellationToken);
        await using Stream stream = await snapshot.OpenReadAsync(cancellationToken: cancellationToken);
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        int count = reader.ReadInt32();
        if (count != 3)
        {
            throw new InvalidDataException("Expected one nonempty triangle.");
        }

        byte[] vertices = reader.ReadBytes(checked(count * 64));
        if (vertices.Length != count * 64)
        {
            throw new EndOfStreamException();
        }

        ushort[] indices = [reader.ReadUInt16(), reader.ReadUInt16(), reader.ReadUInt16()];
        Builds++;
        return new(vertices, indices);
    }

    /// <inheritdoc/>
    public ValueTask ReleaseAsync(ModelMeshData resource)
    {
        Releases++;
        return ValueTask.CompletedTask;
    }
}
