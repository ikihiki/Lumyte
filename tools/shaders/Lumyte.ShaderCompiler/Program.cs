using System.Runtime.Versioning;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shaders;

namespace Lumyte.ShaderCompiler;

[UnsupportedOSPlatform("browser")]
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length < 6)
            {
                throw new ArgumentException("Expected source, output, entry, stage, targets and compiler, followed by compiler arguments.");
            }

            ShaderStage stage = args[3] switch
            {
                "vertex" => ShaderStage.Vertex,
                "fragment" => ShaderStage.Fragment,
                "compute" => ShaderStage.Compute,
                "mesh" => ShaderStage.Mesh,
                "task" => ShaderStage.Task,
                _ => throw new ArgumentException("Unknown shader stage: " + args[3]),
            };
            bool mesh = stage is ShaderStage.Mesh or ShaderStage.Task;
            string[] names = args[4].Split(';');
            var targets = new List<ShaderTarget>();
            foreach (string name in names)
            {
                ShaderTarget target = name switch
                {
                    "wgsl" => ShaderTarget.Wgsl,
                    "spirv" => ShaderTarget.SpirV,
                    "dxil" => ShaderTarget.Dxil,
                    _ => throw new ArgumentException("Unknown or empty shader target: " + name),
                };
                if (targets.Contains(target))
                {
                    throw new ArgumentException("Duplicate shader target: " + name);
                }

                if (mesh && target == ShaderTarget.Wgsl)
                {
                    throw new NotSupportedException("WGSL does not support Mesh or Task shaders.");
                }

                targets.Add(target);
            }

            string source = await File.ReadAllTextAsync(args[0]);
            string[] additional = ["-I", Path.GetDirectoryName(Path.GetFullPath(args[0]))!, .. args.Skip(6)];
            var compiler = new SlangShaderCompiler(args[5]);
            var results = new List<ShaderTargetData>();
            foreach (ShaderTarget target in targets)
            {
                var desc = new ShaderCompilationDesc
                {
                    Source = source,
                    EntryPoint = args[2],
                    Stage = stage,
                    Target = target,
                };
                ShaderArtifact compiled = await compiler.CompileOfflineAsync(desc, additional);
                ShaderTargetData data = compiled.GetTarget(target);
                results.Add(data);
                string suffix = target switch { ShaderTarget.Wgsl => "wgsl", ShaderTarget.SpirV => "spv", _ => "dxil" };
                await File.WriteAllBytesAsync(Path.ChangeExtension(args[1], suffix), data.Code);
                await File.WriteAllTextAsync(Path.ChangeExtension(args[1], suffix + ".reflection.json"), data.ReflectionJson);
            }

            // Publish the complete artifact only after every requested target succeeds.
            byte[] binary = ShaderArtifact.PackTargets(results).GetBinary();
            string temporary = args[1] + ".tmp";
            await File.WriteAllBytesAsync(temporary, binary);
            File.Move(temporary, args[1], overwrite: true);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }
}
