using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shared;

namespace Lumyte.Graphics.Validation;

internal sealed partial class ValidationCommandBuffer
{
    private readonly List<NativePreparation> _preparations = [];
    private bool _preparing;

    public void PrepareRender<T>(RenderPassDesc pass, IGraphicsPipeline pipeline, GraphicsRenderStateDesc state, IArgumentTable? table, in T arguments, IndexFormat? indexFormat = null)
        where T : struct, IShaderArguments
    {
        BeginPreparation();
        try
        {
            if (arguments.NativePreparationIdentity == null)
            {
                throw new ArgumentException("Preparation requires an immutable identity.");
            }

            IGraphicsPipeline rawPipeline = Device.Unwrap(pipeline);
            RenderPassDesc nativePass = UnwrapPreparationPass(pass);
            ValidationWriteContext context = PreparationContext(T.TransferSize == 0 ? null : ValidationEncoderChecks.RenderRoot(Device, pipeline, T.RootParameter), table, out Func<IShaderReferenceMetadata> metadata);
            var adapter = new ValidationArguments<T>(in arguments, context);
            Raw.PrepareRender(nativePass, rawPipeline, state, table == null ? null : Device.Unwrap(table), in adapter, indexFormat);
            SavePreparation(in arguments, pipeline, metadata());
        }
        finally
        {
            _preparing = false;
        }
    }

    public void PrepareMesh<T>(RenderPassDesc pass, IMeshPipeline pipeline, GraphicsRenderStateDesc state, IArgumentTable? table, in T arguments)
        where T : struct, IShaderArguments
    {
        BeginPreparation();
        try
        {
            if (arguments.NativePreparationIdentity == null)
            {
                throw new ArgumentException("Preparation requires an immutable identity.");
            }

            IMeshPipeline rawPipeline = Device.Unwrap(pipeline);
            RenderPassDesc nativePass = UnwrapPreparationPass(pass);
            ValidationWriteContext context = PreparationContext(T.TransferSize == 0 ? null : ValidationEncoderChecks.MeshRoot(Device, pipeline, T.RootParameter), table, out Func<IShaderReferenceMetadata> metadata);
            var adapter = new ValidationArguments<T>(in arguments, context);
            Raw.PrepareMesh(nativePass, rawPipeline, state, table == null ? null : Device.Unwrap(table), in adapter);
            SavePreparation(in arguments, pipeline, metadata());
        }
        finally
        {
            _preparing = false;
        }
    }

    public void PrepareCompute<T>(IGraphicsComputePipeline pipeline, IArgumentTable? table, in T arguments)
        where T : struct, IShaderArguments
    {
        BeginPreparation();
        try
        {
            if (arguments.NativePreparationIdentity == null)
            {
                throw new ArgumentException("Preparation requires an immutable identity.");
            }

            IGraphicsComputePipeline rawPipeline = Device.Unwrap(pipeline);
            ValidationWriteContext context = PreparationContext(T.TransferSize == 0 ? null : ShaderDataLayout.Root(ValidationEncoderChecks.Target(Device, pipeline.Desc.ComputeShader), T.RootParameter), table, out Func<IShaderReferenceMetadata> metadata);
            var adapter = new ValidationArguments<T>(in arguments, context);
            Raw.PrepareCompute(rawPipeline, table == null ? null : Device.Unwrap(table), in adapter);
            SavePreparation(in arguments, pipeline, metadata());
        }
        finally
        {
            _preparing = false;
        }
    }

    internal NativePreparation FindPreparation(object identity, object program)
        => _preparations.FirstOrDefault(entry => ReferenceEquals(entry.Identity, identity) && ReferenceEquals(entry.Program, program)) ?? throw new InvalidOperationException("No command-local native preparation for these arguments and pipeline.");

    private void BeginPreparation()
    {
        OutsidePass(true);
        if (_recordingStarted || _preparing)
        {
            throw new InvalidOperationException("Native preparation must precede all recording.");
        }

        _preparing = true;
    }

    private RenderPassDesc UnwrapPreparationPass(RenderPassDesc pass)
        => pass with { ColorAttachments = pass.ColorAttachments.Select(a => a with { View = Device.Unwrap(a.View) }).ToArray(), DepthStencilAttachment = pass.DepthStencilAttachment is { } depth ? depth with { View = Device.Unwrap(depth.View) } : null };

    private ValidationWriteContext PreparationContext(ShaderDataLayout? diagnostic, IArgumentTable? table, out Func<IShaderReferenceMetadata> metadata)
    {
        IShaderReferenceMetadata? result = diagnostic == null ? new ShaderDependencyMetadata([]) : null;
        metadata = () => result ?? throw new InvalidOperationException("Backend did not materialize root metadata during preparation.");
        return new(Device, diagnostic, value =>
        {
            IArgumentTable? hint = null;
            foreach (IShaderReference reference in value.References)
            {
                ((IValidationReference)reference).ValidateRegistration();
                if (hint != null && !ReferenceEquals(hint, reference.Table))
                {
                    throw new ArgumentException("Mixed argument tables.");
                }

                hint = reference.Table as IArgumentTable ?? throw new ArgumentException("Invalid reference table.");
                if (table != null && !ReferenceEquals(table, hint))
                {
                    throw new ArgumentException("Prepared table differs from references.");
                }
            }

            result = value;
        });
    }

    private void SavePreparation<T>(in T arguments, object program, IShaderReferenceMetadata metadata)
        where T : struct, IShaderArguments
    {
        object identity = arguments.NativePreparationIdentity ?? throw new ArgumentException("Preparation requires an immutable identity.");
        ValidateRecording();
        Device.UnwrapObject(program);
        _preparations.Add(new(identity, program, T.RootParameter, metadata));
    }

    internal sealed record NativePreparation(object Identity, object Program, string RootParameter, IShaderReferenceMetadata Metadata);
}
