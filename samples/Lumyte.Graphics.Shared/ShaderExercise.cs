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
        var artifact = ShaderArtifact.LoadEmbedded(typeof(ShaderExercise).Assembly, "Lumyte.Shaders.increment", device.Caps.ShaderTarget, ShaderStage.Compute);
        CheckModule(device, artifact);
        ShaderTarget otherTarget = device.Caps.ShaderTarget == ShaderTarget.Wgsl ? ShaderTarget.SpirV : ShaderTarget.Wgsl;
        var other = ShaderArtifact.LoadEmbedded(typeof(ShaderExercise).Assembly, "Lumyte.Shaders.increment", otherTarget, ShaderStage.Compute);
        try
        {
            using IGraphicsShader unexpected = device.CreateShader(other);
        }
        catch (ArgumentException)
        {
            return "Shader checks passed: embedded artifacts, target validation and module lifetime.";
        }

        throw new InvalidOperationException("A mismatched shader target was accepted.");
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
        if (!ReferenceEquals(shader.Artifact, artifact) || artifact.GetCode().Length == 0 || string.IsNullOrWhiteSpace(artifact.ReflectionJson))
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
