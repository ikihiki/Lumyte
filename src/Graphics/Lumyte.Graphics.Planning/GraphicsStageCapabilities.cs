using Lumyte.Pipelines;

namespace Lumyte.Graphics.Planning;

/// <summary>An expiring declaration capability bound by the explicit Core backend.</summary>
public sealed class GraphicsStageCapabilities : IStageBackendCapabilities, IStageScopeBinding, IStageCpuPolicy
{
    private readonly GraphicsFrameScope _frame;
    private readonly GraphicsStageRequirement _requirement;
    private StageBackendScope? _scope;
    private GraphicsDeclarationScope? _declarations;
    private bool _validating;

    internal GraphicsStageCapabilities(GraphicsFrameScope frame, GraphicsStageRequirement requirement)
    {
        (_frame, _requirement) = (frame, requirement);
    }

    /// <inheritdoc/>
    public bool CanReadCpu => false;

    /// <inheritdoc/>
    public bool CanPublishCpu => false;

    /// <summary>Gets the common declaration builder; this capability never exposes a native device or allocator.</summary>
    public GraphicsDeclarationScope Plan
    {
        get
        {
            CheckOpen();
            return _declarations!;
        }
    }

    /// <summary>Reads an admitted immutable device declaration, not GPU contents.</summary>
    /// <typeparam name = "TSchema">The canonical schema.</typeparam>
    /// <param name = "input">An exact invocation input lease.</param>
    /// <returns>The declaration owned by this frame.</returns>
    public PlanResource Read<TSchema>(ValueLease<Device<TSchema>> input)
        where TSchema : ISchema
    {
        CheckOpen();
        Device<TSchema> value = _scope!.Read(input);
        return value.Resource;
    }

    /// <summary>Publishes a device declaration in this same common plan.</summary>
    /// <typeparam name = "TSchema">The canonical schema.</typeparam>
    /// <typeparam name = "T">The unmanaged canonical buffer element.</typeparam>
    /// <param name = "resource">The declared output version.</param>
    /// <param name = "extent">The immutable finite schema extent.</param>
    /// <returns>The invocation output, without GPU execution or preparation.</returns>
    public ValueLease<Device<TSchema>> Publish<TSchema, T>(PlanBuffer<T> resource, SchemaExtent extent)
        where TSchema : ISchema
        where T : unmanaged
    {
        CheckOpen();
        return _scope!.Publish(_frame.Plan.BufferValue<TSchema, T>(resource, extent));
    }

    /// <summary>Publishes an opaque image declaration without mapping or executing its contents.</summary>
    /// <typeparam name = "TSchema">The author-declared image schema.</typeparam>
    /// <param name = "resource">The immutable texture version.</param>
    /// <param name = "extent">The finite semantic image extent.</param>
    /// <returns>The typed invocation output.</returns>
    public ValueLease<Device<TSchema>> PublishTexture<TSchema>(PlanTexture resource, SchemaExtent extent)
        where TSchema : ISchema
    {
        CheckOpen();
        return _scope!.Publish(_frame.Plan.TextureValue<TSchema>(resource, extent));
    }

    /// <summary>Publishes a typed shader-data declaration without reading GPU bytes.</summary>
    /// <typeparam name="TSchema">The canonical device schema.</typeparam>
    /// <typeparam name="T">The original shader element ABI.</typeparam>
    /// <param name="resource">The exact frame-owned shader-data version.</param>
    /// <param name="extent">The finite logical extent.</param>
    /// <returns>The validated opaque invocation output.</returns>
    public ValueLease<Device<TSchema>> PublishShaderData<TSchema, T>(PlanShaderData<T> resource, SchemaExtent extent)
        where TSchema : ISchema
        where T : struct, Lumyte.Graphics.Abstractions.IShaderArguments
    {
        CheckOpen();
        return _scope!.Publish(_frame.Plan.ShaderDataValue<TSchema, T>(resource, extent));
    }

    /// <inheritdoc/>
    public void ValidateRead<T>(T value) => ValidateValue(value, read: true);

    /// <inheritdoc/>
    public void ValidatePublication<T>(T value) => ValidateValue(value, read: false);

    /// <inheritdoc/>
    public void Bind(StagePreparationContext context)
    {
        if (_scope is not null)
        {
            throw new InvalidOperationException("Capability already bound.");
        }

        _scope = context.Backend(this);
        _frame.BeginDeclaration();
        _declarations = new(_frame.Plan, CheckOpen, _requirement);
    }

    /// <inheritdoc/>
    public void Complete(bool succeeded)
    {
        _scope = null;
        _declarations = null;
        _frame.EndDeclaration();
        if (!succeeded)
        {
            _frame.InvalidateDeclarations();
        }
    }

    private void CheckOpen()
    {
        if (_scope is null || _validating)
        {
            throw new InvalidOperationException("Graphics declaration capability is not bound to an open invocation.");
        }

        _frame.Plan.Mutable();
    }

    private void ValidateValue<T>(T value, bool read)
    {
        CheckOpen();
        var visitor = new GraphicsDeclarationValueVisitor(_frame.Plan);
        _validating = true;
        try
        {
            visitor.Child(value);
        }
        finally
        {
            _validating = false;
        }

        if (read)
        {
            foreach (PlanResource resource in visitor.Resources)
            {
                _declarations!.RegisterInputProducer(resource.Producer);
            }
        }
    }
}
