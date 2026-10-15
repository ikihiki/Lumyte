using System.Numerics;
using System.Text.Json;

namespace Lumyte.Resources.Gltf.Sample;

internal sealed class SceneStage : IResourceTransform<GltfDocument, GpuScene>
{
    public ResourceDependencyLifetime InputLifetime => ResourceDependencyLifetime.Build;

    public ResourceAddress GetInput(ResourceAddress output) => new(output.Id);

    public async ValueTask<GpuScene> BuildAsync(GltfDocument input, ResourceBuildContext context, CancellationToken cancellationToken)
    {
        JsonElement scene = input.Root.GetProperty("scenes")[GltfDocument.Select(context.Address, "scene")];
        var draws = new List<SceneDraw>();
        var visited = new HashSet<int>();
        foreach (JsonElement node in scene.GetProperty("nodes").EnumerateArray())
        {
            await VisitAsync(node.GetInt32(), Matrix4x4.Identity, input, context, draws, visited, cancellationToken);
        }

        return new(draws.AsReadOnly());
    }

    public ValueTask ReleaseAsync(GpuScene resource) => ValueTask.CompletedTask;

    private static async ValueTask VisitAsync(int index, Matrix4x4 parent, GltfDocument document, ResourceBuildContext context, List<SceneDraw> draws, HashSet<int> visited, CancellationToken cancellationToken)
    {
        if (!visited.Add(index) || visited.Count > 1024)
        {
            throw new InvalidDataException("The scene is cyclic, reuses a node, or exceeds 1024 nodes.");
        }

        JsonElement node = document.Root.GetProperty("nodes")[index];
        if (node.TryGetProperty("skin", out _))
        {
            throw new NotSupportedException("Skinned meshes are outside this sample's subset.");
        }

        Matrix4x4 world = LocalTransform(node) * parent;
        if (node.TryGetProperty("mesh", out JsonElement mesh))
        {
            GpuMesh gpu = await context.RequireAsync(new ResourceKey<GpuMesh>(document.Id, $"mesh/{mesh.GetInt32()}"), cancellationToken: cancellationToken);
            draws.Add(new(gpu, world));
        }

        if (node.TryGetProperty("children", out JsonElement children))
        {
            foreach (JsonElement child in children.EnumerateArray())
            {
                await VisitAsync(child.GetInt32(), world, document, context, draws, visited, cancellationToken);
            }
        }
    }

    private static Matrix4x4 LocalTransform(JsonElement node)
    {
        if (node.TryGetProperty("matrix", out JsonElement matrix))
        {
            // glTF's column-major array becomes a row-vector System.Numerics matrix.
            return new(matrix[0].GetSingle(), matrix[1].GetSingle(), matrix[2].GetSingle(), matrix[3].GetSingle(), matrix[4].GetSingle(), matrix[5].GetSingle(), matrix[6].GetSingle(), matrix[7].GetSingle(), matrix[8].GetSingle(), matrix[9].GetSingle(), matrix[10].GetSingle(), matrix[11].GetSingle(), matrix[12].GetSingle(), matrix[13].GetSingle(), matrix[14].GetSingle(), matrix[15].GetSingle());
        }

        Vector3 scale = node.TryGetProperty("scale", out JsonElement s) ? new(s[0].GetSingle(), s[1].GetSingle(), s[2].GetSingle()) : Vector3.One;
        Quaternion rotation = node.TryGetProperty("rotation", out JsonElement r) ? new(r[0].GetSingle(), r[1].GetSingle(), r[2].GetSingle(), r[3].GetSingle()) : Quaternion.Identity;
        Vector3 translation = node.TryGetProperty("translation", out JsonElement t) ? new(t[0].GetSingle(), t[1].GetSingle(), t[2].GetSingle()) : Vector3.Zero;
        return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(translation);
    }
}
