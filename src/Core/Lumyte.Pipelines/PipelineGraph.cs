namespace Lumyte.Pipelines;

/// <summary>Builds typed passive edges with append-only acyclic connections.</summary>
/// <typeparam name="TInput">The arbitrary input payload.</typeparam>
public sealed class PipelineGraph<TInput>
{
    private readonly GraphStorage _storage = new();

    /// <summary>Initializes a new instance of the <see cref="PipelineGraph{TInput}"/> class.</summary>
    public PipelineGraph()
    {
        Input = new Port<TInput>(_storage, 0);
    }

    /// <summary>Gets the graph input edge.</summary>
    public Port<TInput> Input { get; }

    /// <summary>Adds one independently evaluated node.</summary>
    /// <typeparam name="TA">The TA port payload.</typeparam>
    /// <typeparam name="TO">The TO port payload.</typeparam>
    /// <param name="implementation">The captured immutable declaration.</param>
    /// <param name="a">Input port a.</param>
    /// <returns>The new output edge.</returns>
    public Port<TO> Stage<TA, TO>(IStage<TA, TO> implementation, Port<TA> a)
    {
        ArgumentNullException.ThrowIfNull(implementation);
        _storage.Validate(a);
        return _storage.Add<TO>(new StageNode<TA, TO>(implementation.Factory, [a.Index], implementation.GetType()), [a]);
    }

    /// <summary>Adds one independently evaluated node.</summary>
    /// <typeparam name="TA">The TA port payload.</typeparam>
    /// <typeparam name="TB">The TB port payload.</typeparam>
    /// <typeparam name="TO">The TO port payload.</typeparam>
    /// <param name="implementation">The captured immutable declaration.</param>
    /// <param name="a">Input port a.</param>
    /// <param name="b">Input port b.</param>
    /// <returns>The new output edge.</returns>
    public Port<TO> Stage<TA, TB, TO>(IStage<TA, TB, TO> implementation, Port<TA> a, Port<TB> b)
    {
        ArgumentNullException.ThrowIfNull(implementation);
        _storage.Validate(a);
        _storage.Validate(b);
        return _storage.Add<TO>(new StageNode<TA, TB, TO>(implementation.Factory, [a.Index, b.Index], implementation.GetType()), [a, b]);
    }

    /// <summary>Adds one independently evaluated node.</summary>
    /// <typeparam name="TA">The TA port payload.</typeparam>
    /// <typeparam name="TB">The TB port payload.</typeparam>
    /// <typeparam name="TC">The TC port payload.</typeparam>
    /// <typeparam name="TO">The TO port payload.</typeparam>
    /// <param name="implementation">The captured immutable declaration.</param>
    /// <param name="a">Input port a.</param>
    /// <param name="b">Input port b.</param>
    /// <param name="c">Input port c.</param>
    /// <returns>The new output edge.</returns>
    public Port<TO> Stage<TA, TB, TC, TO>(IStage<TA, TB, TC, TO> implementation, Port<TA> a, Port<TB> b, Port<TC> c)
    {
        ArgumentNullException.ThrowIfNull(implementation);
        _storage.Validate(a);
        _storage.Validate(b);
        _storage.Validate(c);
        return _storage.Add<TO>(new StageNode<TA, TB, TC, TO>(implementation.Factory, [a.Index, b.Index, c.Index], implementation.GetType()), [a, b, c]);
    }

    /// <summary>Adds one independently evaluated node.</summary>
    /// <typeparam name="TA">The TA port payload.</typeparam>
    /// <typeparam name="TB">The TB port payload.</typeparam>
    /// <typeparam name="TC">The TC port payload.</typeparam>
    /// <typeparam name="TD">The TD port payload.</typeparam>
    /// <typeparam name="TO">The TO port payload.</typeparam>
    /// <param name="implementation">The captured immutable declaration.</param>
    /// <param name="a">Input port a.</param>
    /// <param name="b">Input port b.</param>
    /// <param name="c">Input port c.</param>
    /// <param name="d">Input port d.</param>
    /// <returns>The new output edge.</returns>
    public Port<TO> Stage<TA, TB, TC, TD, TO>(IStage<TA, TB, TC, TD, TO> implementation, Port<TA> a, Port<TB> b, Port<TC> c, Port<TD> d)
    {
        ArgumentNullException.ThrowIfNull(implementation);
        _storage.Validate(a);
        _storage.Validate(b);
        _storage.Validate(c);
        _storage.Validate(d);
        return _storage.Add<TO>(new StageNode<TA, TB, TC, TD, TO>(implementation.Factory, [a.Index, b.Index, c.Index, d.Index], implementation.GetType()), [a, b, c, d]);
    }

    /// <summary>Adds one independently evaluated node.</summary>
    /// <typeparam name="TA">The TA port payload.</typeparam>
    /// <typeparam name="TB">The TB port payload.</typeparam>
    /// <typeparam name="TC">The TC port payload.</typeparam>
    /// <typeparam name="TD">The TD port payload.</typeparam>
    /// <typeparam name="TE">The TE port payload.</typeparam>
    /// <typeparam name="TO">The TO port payload.</typeparam>
    /// <param name="implementation">The captured immutable declaration.</param>
    /// <param name="a">Input port a.</param>
    /// <param name="b">Input port b.</param>
    /// <param name="c">Input port c.</param>
    /// <param name="d">Input port d.</param>
    /// <param name="e">Input port e.</param>
    /// <returns>The new output edge.</returns>
    public Port<TO> Stage<TA, TB, TC, TD, TE, TO>(IStage<TA, TB, TC, TD, TE, TO> implementation, Port<TA> a, Port<TB> b, Port<TC> c, Port<TD> d, Port<TE> e)
    {
        ArgumentNullException.ThrowIfNull(implementation);
        _storage.Validate(a);
        _storage.Validate(b);
        _storage.Validate(c);
        _storage.Validate(d);
        _storage.Validate(e);
        return _storage.Add<TO>(new StageNode<TA, TB, TC, TD, TE, TO>(implementation.Factory, [a.Index, b.Index, c.Index, d.Index, e.Index], implementation.GetType()), [a, b, c, d, e]);
    }

    /// <summary>Adds one independently evaluated node.</summary>
    /// <typeparam name="TA">The TA port payload.</typeparam>
    /// <typeparam name="TB">The TB port payload.</typeparam>
    /// <typeparam name="TC">The TC port payload.</typeparam>
    /// <typeparam name="TD">The TD port payload.</typeparam>
    /// <typeparam name="TE">The TE port payload.</typeparam>
    /// <typeparam name="TF">The TF port payload.</typeparam>
    /// <typeparam name="TO">The TO port payload.</typeparam>
    /// <param name="implementation">The captured immutable declaration.</param>
    /// <param name="a">Input port a.</param>
    /// <param name="b">Input port b.</param>
    /// <param name="c">Input port c.</param>
    /// <param name="d">Input port d.</param>
    /// <param name="e">Input port e.</param>
    /// <param name="f">Input port f.</param>
    /// <returns>The new output edge.</returns>
    public Port<TO> Stage<TA, TB, TC, TD, TE, TF, TO>(IStage<TA, TB, TC, TD, TE, TF, TO> implementation, Port<TA> a, Port<TB> b, Port<TC> c, Port<TD> d, Port<TE> e, Port<TF> f)
    {
        ArgumentNullException.ThrowIfNull(implementation);
        _storage.Validate(a);
        _storage.Validate(b);
        _storage.Validate(c);
        _storage.Validate(d);
        _storage.Validate(e);
        _storage.Validate(f);
        return _storage.Add<TO>(new StageNode<TA, TB, TC, TD, TE, TF, TO>(implementation.Factory, [a.Index, b.Index, c.Index, d.Index, e.Index, f.Index], implementation.GetType()), [a, b, c, d, e, f]);
    }

    /// <summary>Flattens a reusable template into this graph without building or preparing a nested run.</summary>
    /// <typeparam name="TA">The template input.</typeparam>
    /// <typeparam name="TO">The template output.</typeparam>
    /// <param name="definition">The immutable template.</param>
    /// <param name="input">An edge in this graph.</param>
    /// <returns>The rebound output edge.</returns>
    public Port<TO> Invoke<TA, TO>(PipelineDefinition<TA, TO> definition, Port<TA> input)
    {
        ArgumentNullException.ThrowIfNull(definition);
        _storage.Validate(input);
        definition.ValidateSchemas();
        return _storage.Append<TA, TO>(definition.Nodes, definition.Output, input);
    }

    /// <summary>Captures the definition and permanently freezes the graph.</summary>
    /// <typeparam name="TOutput">The arbitrary output payload.</typeparam>
    /// <param name="output">The selected output edge.</param>
    /// <returns>The reusable immutable definition.</returns>
    public PipelineDefinition<TInput, TOutput> Complete<TOutput>(Port<TOutput> output)
    {
        _storage.Validate(output);
        return new PipelineDefinition<TInput, TOutput>(_storage.Freeze(), output.Index);
    }
}
