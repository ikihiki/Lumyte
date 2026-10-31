using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Samples;

/// <summary>Backend-independent catalog of GPU exercises shared by native and browser hosts.</summary>
public static class GraphicsExercises
{
    /// <summary>Gets the focused exercises, in suite execution order.</summary>
    public static IReadOnlyList<string> Names { get; } = Array.AsReadOnly(new[] { "buffers", "textures", "samplers", "arguments", "shaders", "commands", "pipelines", "bindings", "advanced", "semaphores", "render-graph" });

    /// <summary>Runs one exercise or the complete contract suite.</summary>
    /// <param name="device">Device owned by the calling host.</param>
    /// <param name="name">Exercise name, caps, or suite.</param>
    /// <param name="report">Receives individual results as they complete.</param>
    /// <returns>Completion of all selected exercises.</returns>
    public static async Task RunAsync(IGraphicDevice device, string name, Action<string> report)
    {
        if (name == "suite")
        {
            foreach (string exercise in Names)
            {
                await RunAsync(device, exercise, report);
            }

            return;
        }

        string result = name switch
        {
            "caps" => CapsDisplay.Describe(device),
            "buffers" => await BufferExercise.RunAsync(device),
            "textures" => TextureExercise.Run(device),
            "samplers" => SamplerExercise.Run(device),
            "arguments" => ArgumentTableExercise.Run(device),
            "shaders" => ShaderExercise.Run(device),
            "commands" => await CommandExercise.RunAsync(device),
            "pipelines" => await PipelineExercise.RunAsync(device),
            "bindings" => await ShaderBindingExercise.RunAsync(device),
            "advanced" => await AdvancedCommandExercise.RunAsync(device),
            "semaphores" => await SemaphoreExercise.RunAsync(device),
            "render-graph" => await RenderGraphExercise.RunAsync(device),
            _ => throw new ArgumentException($"Unknown graphics exercise: {name}", nameof(name)),
        };
        report(result);
    }
}
