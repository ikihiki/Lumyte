namespace Lumyte.Graphics.ImageEffects.Benchmark.Preparation;

/// <summary>Bounded built-in effect graphs that always produce a retained output.</summary>
public enum EffectScenario
{
    /// <summary>Horizontal and vertical Gaussian passes.</summary>
    Blur,

    /// <summary>Blur, opacity and composition with the original source.</summary>
    BlurComposite,

    /// <summary>A shared blurred node feeding two branches and their composition.</summary>
    SharedBranch,
}
