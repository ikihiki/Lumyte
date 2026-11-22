namespace Lumyte.Graphics.Drawing2D;

/// <summary>Internal same-device preparation bridge without a consumer runner.</summary>
internal interface IGraphRendererSource2D
{
    /// <summary>Gets the explicitly selected argument-table policy.</summary>
    DrawingArgumentTableMode ArgumentTableMode { get; }

    /// <summary>Gets the compiled CommandRenderers.</summary>
    DrawingCommandRenderers2D CommandRenderers { get; }

    /// <summary>Gets the compiled ResourceState.</summary>
    DrawingResourceState2D ResourceState { get; }

    /// <summary>Gets the same directly borrowed generic pipeline binding, when selected.</summary>
    Pipelines.PipelineBinding2D? PipelineBinding { get; }
}
