namespace Lumyte.Pipelines;

/// <summary>Visits captured factories and typed edges in declaration order. Edge zero is the input.</summary>
public interface IPipelineDefinitionVisitor
{
    /// <summary>Visits one captured factory without reconstructing its parameters.</summary>
    /// <typeparam name="TA">The A edge payload.</typeparam>
    /// <typeparam name="TO">The output edge payload.</typeparam>
    /// <param name="factory">The original immutable factory instance.</param>
    /// <param name="inputs">The typed producer edges.</param>
    /// <param name="output">The output edge.</param>
    void Stage<TA, TO>(IStageFactory<TA, TO> factory, PipelineEdge<TA> inputs, PipelineEdge<TO> output);

    /// <summary>Visits one captured factory without reconstructing its parameters.</summary>
    /// <typeparam name="TA">The A edge payload.</typeparam>
    /// <typeparam name="TB">The B edge payload.</typeparam>
    /// <typeparam name="TO">The output edge payload.</typeparam>
    /// <param name="factory">The original immutable factory instance.</param>
    /// <param name="inputs">The typed producer edges.</param>
    /// <param name="output">The output edge.</param>
    void Stage<TA, TB, TO>(IStageFactory<TA, TB, TO> factory, (PipelineEdge<TA> A, PipelineEdge<TB> B) inputs, PipelineEdge<TO> output);

    /// <summary>Visits one captured factory without reconstructing its parameters.</summary>
    /// <typeparam name="TA">The A edge payload.</typeparam>
    /// <typeparam name="TB">The B edge payload.</typeparam>
    /// <typeparam name="TC">The C edge payload.</typeparam>
    /// <typeparam name="TO">The output edge payload.</typeparam>
    /// <param name="factory">The original immutable factory instance.</param>
    /// <param name="inputs">The typed producer edges.</param>
    /// <param name="output">The output edge.</param>
    void Stage<TA, TB, TC, TO>(IStageFactory<TA, TB, TC, TO> factory, (PipelineEdge<TA> A, PipelineEdge<TB> B, PipelineEdge<TC> C) inputs, PipelineEdge<TO> output);

    /// <summary>Visits one captured factory without reconstructing its parameters.</summary>
    /// <typeparam name="TA">The A edge payload.</typeparam>
    /// <typeparam name="TB">The B edge payload.</typeparam>
    /// <typeparam name="TC">The C edge payload.</typeparam>
    /// <typeparam name="TD">The D edge payload.</typeparam>
    /// <typeparam name="TO">The output edge payload.</typeparam>
    /// <param name="factory">The original immutable factory instance.</param>
    /// <param name="inputs">The typed producer edges.</param>
    /// <param name="output">The output edge.</param>
    void Stage<TA, TB, TC, TD, TO>(IStageFactory<TA, TB, TC, TD, TO> factory, (PipelineEdge<TA> A, PipelineEdge<TB> B, PipelineEdge<TC> C, PipelineEdge<TD> D) inputs, PipelineEdge<TO> output);

    /// <summary>Visits one captured factory without reconstructing its parameters.</summary>
    /// <typeparam name="TA">The A edge payload.</typeparam>
    /// <typeparam name="TB">The B edge payload.</typeparam>
    /// <typeparam name="TC">The C edge payload.</typeparam>
    /// <typeparam name="TD">The D edge payload.</typeparam>
    /// <typeparam name="TE">The E edge payload.</typeparam>
    /// <typeparam name="TO">The output edge payload.</typeparam>
    /// <param name="factory">The original immutable factory instance.</param>
    /// <param name="inputs">The typed producer edges.</param>
    /// <param name="output">The output edge.</param>
    void Stage<TA, TB, TC, TD, TE, TO>(IStageFactory<TA, TB, TC, TD, TE, TO> factory, (PipelineEdge<TA> A, PipelineEdge<TB> B, PipelineEdge<TC> C, PipelineEdge<TD> D, PipelineEdge<TE> E) inputs, PipelineEdge<TO> output);

    /// <summary>Visits one captured factory without reconstructing its parameters.</summary>
    /// <typeparam name="TA">The A edge payload.</typeparam>
    /// <typeparam name="TB">The B edge payload.</typeparam>
    /// <typeparam name="TC">The C edge payload.</typeparam>
    /// <typeparam name="TD">The D edge payload.</typeparam>
    /// <typeparam name="TE">The E edge payload.</typeparam>
    /// <typeparam name="TF">The F edge payload.</typeparam>
    /// <typeparam name="TO">The output edge payload.</typeparam>
    /// <param name="factory">The original immutable factory instance.</param>
    /// <param name="inputs">The typed producer edges.</param>
    /// <param name="output">The output edge.</param>
    void Stage<TA, TB, TC, TD, TE, TF, TO>(IStageFactory<TA, TB, TC, TD, TE, TF, TO> factory, (PipelineEdge<TA> A, PipelineEdge<TB> B, PipelineEdge<TC> C, PipelineEdge<TD> D, PipelineEdge<TE> E, PipelineEdge<TF> F) inputs, PipelineEdge<TO> output);
}
