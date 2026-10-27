namespace Lumyte.Graphics.Drawing2D.RenderGraph;

/// <summary>Records 2D layers and effect subgraphs for insertion into a parent RenderGraph.</summary>
public sealed class RenderGraphCanvas2D : DrawingCanvas2D
{
    private readonly Stack<LayerFrame> _layers = [];

    /// <inheritdoc/>
    protected override int SaveBoundary => _layers.TryPeek(out LayerFrame? layer) ? layer.SaveDepth : 0;

    /// <summary>Begins a finite offscreen layer, deferring the parent clip until effect output composition.</summary>
    /// <param name="options">The input geometry clip, effects, group opacity, and optional output clip.</param>
    public void BeginLayer(LayerOptions2D options)
    {
        RequireRecording();
        ArgumentNullException.ThrowIfNull(options);
        ModelValidation2D.Rectangle(options.ContentBounds, nameof(options));
        ModelValidation2D.Unit(options.Opacity, nameof(options));

        if (options.OutputClip is Rect2D outputClip)
        {
            ModelValidation2D.Rectangle(outputClip, nameof(options));
        }

        LayerOptions2D snapshot = options with { };
        var contentClip = new ClipElement2D(ModelValidation2D.RectanglePath(snapshot.ContentBounds), State.Transform, FillRule2D.NonZero);
        var childState = new DrawingState2D(State.Transform, new ClipStack2D([contentClip]));
        AddCommand(new BeginLayerCommand2D(snapshot, State));
        _layers.Push(new LayerFrame(State, SaveDepth));
        State = childState;
    }

    /// <summary>Ends the current layer, restoring its entry state; unmatched inner saves are rejected.</summary>
    public void EndLayer()
    {
        RequireRecording();
        if (!_layers.TryPeek(out LayerFrame? layer))
        {
            throw new InvalidOperationException("There is no open layer.");
        }

        if (SaveDepth != layer.SaveDepth)
        {
            throw new InvalidOperationException("All Save calls in a layer must be restored before EndLayer.");
        }

        AddCommand(new EndLayerCommand2D(layer.ParentState));
        _layers.Pop();
        State = layer.ParentState;
    }

    /// <summary>Records a filtered replacement of the preceding background in the current layer.</summary>
    /// <param name="options">The displayed output bounds and image effect graph.</param>
    public void DrawBackdropEffect(BackdropEffectOptions2D options)
    {
        RequireRecording();
        ArgumentNullException.ThrowIfNull(options);
        ModelValidation2D.Rectangle(options.OutputBounds, nameof(options));

        ArgumentNullException.ThrowIfNull(options.Effects, nameof(options));
        AddCommand(new DrawBackdropEffectCommand2D(options with { }, State));
    }

    /// <summary>Completes a reusable layered recording.</summary>
    /// <returns>The graph-specific drawing snapshot.</returns>
    public RenderGraphDrawList2D End() => new(CompleteRecording());

    /// <inheritdoc/>
    protected override void ValidateComplete()
    {
        if (_layers.Count != 0)
        {
            throw new InvalidOperationException("All BeginLayer calls must be matched before End.");
        }
    }

    /// <inheritdoc/>
    protected override void ResetAdditionalState() => _layers.Clear();

    private sealed record LayerFrame(DrawingState2D ParentState, int SaveDepth);
}
