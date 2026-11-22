using Lumyte.Graphics.Pipelines;

namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>Captures explicit execution, argument-table and retained ownership policy.</summary>
/// <param name="Execution">The exact finite policy fixed by the graphics build.</param>
/// <param name="ArgumentTables">The explicit argument-table selection.</param>
/// <param name="Retention">The finite retained-frame and asset limits.</param>
public sealed record PipelinePolicy2D(GraphicsPipelinePolicy Execution, DrawingArgumentTableMode ArgumentTables, DrawingRetentionOptions2D Retention)
{
    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Execution);
        ArgumentNullException.ThrowIfNull(Retention);
        if (!Enum.IsDefined(ArgumentTables))
        {
            throw new ArgumentOutOfRangeException(nameof(ArgumentTables));
        }

        Retention.Validate();
    }
}
