using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Samples;

/// <summary>Exercises embedded Slang artifacts and modules using only common APIs.</summary>
public static class ShaderExercise
{
    /// <summary>Loads the device target from this DLL and checks module ownership.</summary>
    /// <param name="device">The already created backend device.</param>
    /// <returns>A report after the shader checks succeed.</returns>
    public static string Run(IGraphicDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        var artifact = ShaderArtifact.LoadEmbedded(typeof(ShaderExercise).Assembly, "Lumyte.Shaders.increment.lshader");
        CheckModule(device, artifact);
        foreach (ShaderTarget target in Enum.GetValues<ShaderTarget>())
        {
            ShaderTargetData data = artifact.GetTarget(target);
            if (data.Stage != ShaderStage.Compute || data.EntryPoint != "main" || string.IsNullOrWhiteSpace(data.CompilerVersion) || data.MatrixLayout != "row-major")
            {
                throw new InvalidOperationException("Missing offline compilation metadata.");
            }
        }

        return "Shader checks passed: all-target binary, embedded metadata and module lifetime.";
    }

    /// <summary>Compiles the same Slang source online and creates a device module.</summary>
    /// <param name="device">The already created backend device.</param>
    /// <param name="compiler">The host compiler supplied by the bootstrap.</param>
    /// <returns>A report after online compilation and module creation succeed.</returns>
    public static async Task<string> RunOnlineAsync(IGraphicDevice device, IShaderCompiler compiler)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(compiler);
        using Stream source = typeof(ShaderExercise).Assembly.GetManifestResourceStream("Lumyte.Shaders.increment.slang") ?? throw new InvalidOperationException("Missing Slang source resource.");
        using var reader = new StreamReader(source);
        var desc = new ShaderCompilationDesc { Source = await reader.ReadToEndAsync(), Target = device.Caps.ShaderTarget };
        ShaderArtifact artifact = await compiler.CompileAsync(desc);
        CheckModule(device, artifact);
        return "Online shader checks passed: Slang compilation and module creation.";
    }

    private static void CheckModule(IGraphicDevice device, ShaderArtifact artifact)
    {
        using IGraphicsShader shader = device.CreateShader(artifact);
        if (!ReferenceEquals(shader.Artifact, artifact) || artifact.GetTarget(device.Caps.ShaderTarget).Code.Length == 0)
        {
            throw new InvalidOperationException("Shader artifact was not retained.");
        }

        if (device is IDisposable owner)
        {
            try
            {
                owner.Dispose();
            }
            catch (InvalidOperationException)
            {
                shader.Dispose();
                shader.Dispose();
                return;
            }

            throw new InvalidOperationException("Device was disposed with a live shader.");
        }
    }
}
