using System.Numerics;
using System.Text.Json;

namespace Lumyte.Graphics.Drawing.Gltf;

internal sealed partial class GltfReader
{
    private static Matrix4x4 NodeTransform(JsonElement node)
    {
        if (node.TryGetProperty("matrix", out _))
        {
            if (node.TryGetProperty("translation", out _) || node.TryGetProperty("rotation", out _) || node.TryGetProperty("scale", out _))
            {
                throw new InvalidDataException("A node cannot combine matrix and TRS transforms.");
            }

            float[] m = Floats(node, "matrix", new float[16]);
            if (m[3] != 0 || m[7] != 0 || m[11] != 0 || m[15] != 1)
            {
                throw new InvalidDataException("Node transforms must be affine.");
            }

            // glTF column-major column-vector data becomes the same sequence of .NET row-major row-vector values.
            return new(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8], m[9], m[10], m[11], m[12], m[13], m[14], m[15]);
        }

        float[] t = Floats(node, "translation", [0, 0, 0]);
        float[] r = Floats(node, "rotation", [0, 0, 0, 1]);
        float[] s = Floats(node, "scale", [1, 1, 1]);
        var rotation = new Quaternion(r[0], r[1], r[2], r[3]);
        if (MathF.Abs(rotation.LengthSquared() - 1) > 1e-3f)
        {
            throw new InvalidDataException("Node rotations must be unit quaternions.");
        }

        return Matrix4x4.CreateScale(s[0], s[1], s[2]) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(t[0], t[1], t[2]);
    }

    private GltfInstance[] ReadInstances(int meshCount, CancellationToken token)
    {
        JsonElement[] nodes = Array(_root, "nodes");
        int[] parents = new int[nodes.Length];
        var local = new Matrix4x4[nodes.Length];
        int[][] children = new int[nodes.Length][];
        for (int i = 0; i < nodes.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            JsonElement node = nodes[i];
            if (node.TryGetProperty("skin", out _) || node.TryGetProperty("weights", out _) ||
                (node.TryGetProperty("extensions", out JsonElement extensions) && extensions.TryGetProperty("EXT_mesh_gpu_instancing", out _)))
            {
                throw new NotSupportedException("Deformed or GPU-instanced nodes are not supported.");
            }

            if (node.TryGetProperty("mesh", out JsonElement mesh))
            {
                CheckIndex(mesh.GetInt32(), meshCount, "mesh");
            }

            local[i] = NodeTransform(node);
            children[i] = Array(node, "children").Select(x => x.GetInt32()).ToArray();
            foreach (int child in children[i])
            {
                CheckIndex(child, nodes.Length, "child node");
                if (++parents[child] > 1)
                {
                    throw new InvalidDataException("glTF nodes must form a forest, without shared children.");
                }
            }
        }

        // Validate every node, including nodes outside the selected scene, without recursive traversal.
        int[] rootNodes = Enumerable.Range(0, nodes.Length).Where(i => parents[i] == 0).ToArray();
        var queue = new Queue<int>(rootNodes);
        int visited = 0;
        while (queue.TryDequeue(out int node))
        {
            visited++;
            foreach (int child in children[node])
            {
                queue.Enqueue(child);
            }
        }

        if (visited != nodes.Length)
        {
            throw new InvalidDataException("A node hierarchy contains a cycle.");
        }

        JsonElement[] scenes = Array(_root, "scenes");
        int[] selectedRoots = rootNodes;
        if (scenes.Length != 0)
        {
            int scene = Int(_root, "scene");
            CheckIndex(scene, scenes.Length, "scene");
            selectedRoots = Array(scenes[scene], "nodes").Select(x => x.GetInt32()).ToArray();
        }
        else if (_root.TryGetProperty("scene", out _))
        {
            throw new InvalidDataException("A default scene index requires scenes.");
        }

        var instances = new List<GltfInstance>();
        var stack = new Stack<(int Node, Matrix4x4 Parent)>();
        var selected = new HashSet<int>();
        foreach (int index in selectedRoots.Reverse())
        {
            CheckIndex(index, nodes.Length, "scene node");
            if (parents[index] != 0)
            {
                throw new InvalidDataException("Scene nodes must be hierarchy roots.");
            }

            stack.Push((index, Matrix4x4.Identity));
        }

        while (stack.TryPop(out (int Node, Matrix4x4 Parent) entry))
        {
            token.ThrowIfCancellationRequested();
            if (!selected.Add(entry.Node))
            {
                throw new InvalidDataException("A scene contains a duplicate root.");
            }

            Matrix4x4 world = local[entry.Node] * entry.Parent;
            JsonElement node = nodes[entry.Node];
            if (node.TryGetProperty("mesh", out JsonElement mesh))
            {
                instances.Add(new(String(node, "name"), mesh.GetInt32(), world));
            }

            foreach (int child in children[entry.Node].Reverse())
            {
                stack.Push((child, world));
            }
        }

        return instances.ToArray();
    }
}
