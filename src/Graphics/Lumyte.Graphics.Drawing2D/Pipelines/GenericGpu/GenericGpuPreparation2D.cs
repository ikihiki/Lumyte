using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Pipelines;
using Lumyte.Pipelines;

namespace Lumyte.Graphics.Drawing2D.Pipelines;

internal static class GenericGpuPreparation2D
{
    // Implementation acceptance ceilings, not silent quality reductions.
    internal const long MaxSampleEntries = 16L * 1048576;
    internal const int MaxResources = 128;
    internal const long ResourceBytes = (2 * (MaxResources + 1L) * 32) + 4;

    internal static StageArtifactKey Key<T>() => new(typeof(T), 1, 0);

    internal static GraphicsStagePreparationContext Preparation(StagePreparationContext context) => (context.Capabilities as IGraphicsStageCapabilities ?? throw new NotSupportedException("A Graphics build is required.")).Preparation;

    internal static StageArtifactLease Artifact<T>(StageBuildContext context) => (context.Capabilities as IGraphicsStageCapabilities ?? throw new NotSupportedException("A Graphics build is required.")).Build.GetArtifact(Key<T>());

    internal static bool Upper(StageInputDescriptions inputs) => inputs.Extents.Any(e => e.Metadata.ContainsKey("UpperBound") || e.Metadata.ContainsKey("LowerBound"));

    internal static long Count(SchemaExtent extent)
    {
        if (!extent.Metadata.TryGetValue("source", out object? source) || source is not Guid id || id == Guid.Empty || Convert.ToInt32(extent.Metadata["samples"]) != 16 || Convert.ToInt32(extent.Metadata["gridVersion"]) != 1)
        {
            throw new InvalidOperationException("Canonical source/sample/grid metadata is required.");
        }

        long width = Convert.ToInt64(extent.Metadata["width"]), height = Convert.ToInt64(extent.Metadata["height"]), commands = Convert.ToInt64(extent.Metadata["commandCount"]);
        long count = checked(checked(checked(width * height) * commands) * 16);
        if (width <= 0 || height <= 0 || width > uint.MaxValue || height > uint.MaxValue || commands < 0 || commands > uint.MaxValue || count > MaxSampleEntries)
        {
            throw new NotSupportedException("Generic GPU canonical sample extent exceeds the finite implementation ceiling.");
        }

        return count;
    }

    internal static void Match(params SchemaExtent[] extents)
    {
        foreach (SchemaExtent extent in extents)
        {
            Count(extent);
            CpuNormalize2D.ValidateIdentity(extents[0], extent);
        }
    }

    internal static StageRequirements Describe<T>(StageInputDescriptions inputs, GraphicsExecutionDomain domain, int outputStride, int targetIndex = -1, bool resources = false)
    {
        long count = Upper(inputs) ? MaxSampleEntries : Count(inputs.Extents[0]);
        if (!Upper(inputs))
        {
            Match(inputs.Extents.ToArray());

            if (resources)
            {
                foreach (SchemaExtent extent in inputs.Extents)
                {
                    if (Convert.ToInt64(extent.Metadata["viewCount"]) > MaxResources || Convert.ToInt64(extent.Metadata["samplerCount"]) > MaxResources)
                    {
                        throw new NotSupportedException("Generic GPU resource table exceeds the finite source-entry ceiling.");
                    }
                }
            }
        }

        StageInputAccess[] accesses = Enumerable.Range(0, inputs.Extents.Count).Select(i => new StageInputAccess(i, i == targetIndex ? ResourceAccess.ColorRead | ResourceAccess.ColorWrite : ResourceAccess.ShaderRead)).ToArray();
        return new(65536, [new GraphicsStageRequirements(domain, checked((count * outputStride) + (resources ? ResourceBytes : 0)), targetIndex >= 0 ? [Key<T>(), GraphCompositionArtifact2D.Key] : [Key<T>()], accesses)]);
    }

    internal static GenericArguments2D Arguments(SchemaExtent extent, IArgumentTable table, BufferSlice<Vector4> scene) => new()
    {
        Scene = table.WriteBuffer(0, scene),
        Width = Convert.ToUInt32(extent.Metadata["width"]),
        Height = Convert.ToUInt32(extent.Metadata["height"]),
        Commands = Convert.ToUInt32(extent.Metadata["commandCount"]),
        TranslationX = extent.Metadata.TryGetValue("translationX", out object? x) ? Convert.ToSingle(x) : 0,
        TranslationY = extent.Metadata.TryGetValue("translationY", out object? y) ? Convert.ToSingle(y) : 0,
    };

    internal static IArgumentTable Table(GraphicsStagePreparationContext gpu, int textureCount = 0, int samplerCount = 0)
    {
        IArgumentTable table = gpu.CreateArgumentTable(new() { BufferCapacity = 8, TextureCapacity = checked((uint)textureCount), SamplerCapacity = checked((uint)samplerCount) });
        return table;
    }

    internal static async ValueTask<ResourceUpload> ResourcesAsync(GraphicsStagePreparationContext gpu, DeviceBindings<ResourceViewsV1> bindings, IArgumentTable table, StageArtifactLease artifact, CancellationToken ct)
    {
        if (!bindings.Extent.Metadata.TryGetValue("resourceSnapshot", out object? value) || value is not RenderResourcesSnapshot2D snapshot)
        {
            throw new InvalidOperationException("Resource views must retain their exact immutable canonical resource snapshot.");
        }

        if (snapshot.Views.Count > MaxResources || snapshot.Samplers.Count > MaxResources)
        {
            throw new NotSupportedException("Generic GPU resource table exceeds 128 source entries.");
        }

        int count = Math.Max(snapshot.Views.Count, snapshot.Samplers.Count) + 1;
        IGraphicsShaderDataBuffer<GenericResource2D> upload = gpu.CreateShaderDataScratchBuffer<GenericResource2D>(artifact, checked((ulong)count), MemoryPreference.Upload);
        IGraphicsShaderDataBuffer<GenericResource2D> device = gpu.CreateShaderDataScratchBuffer<GenericResource2D>(artifact, checked((ulong)count));
        var entries = new GenericResource2D[count];
        IGpuRef<IGraphicsSampler> fallback = table.WriteSampler(0, artifact.Sampler!);
        IGpuRef<IGraphicsTextureView> fallbackTexture;
        if (snapshot.Views.Count > 0)
        {
            fallbackTexture = table.WriteTexture(0, bindings.GetTexture(0));
        }
        else
        {
            gpu.Run.ReserveScratch(4);
            IGraphicsTexture texture = gpu.Run.Device.CreateTexture(new() { Width = 1, Height = 1, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.Sampled });
            gpu.Run.Own(texture);
            IGraphicsTextureView view = texture.CreateView(new());
            gpu.Run.Own(view);
            fallbackTexture = table.WriteTexture(0, view);
        }

        for (int i = 0; i < count; i++)
        {
            entries[i].Texture = fallbackTexture;
            entries[i].Sampler = i > 0 && i <= snapshot.Samplers.Count ? table.WriteSampler((uint)i, snapshot.Samplers[i - 1]) : fallback;
            if (i > 0 && i <= snapshot.Views.Count)
            {
                entries[i].Texture = i == 1 ? fallbackTexture : table.WriteTexture((uint)(i - 1), bindings.GetTexture(i - 1));
            }
        }

        await upload.MapAsync(ct);
        try
        {
            upload.CopyFrom(entries);
        }
        finally
        {
            upload.Unmap();
        }

        ShaderDataSlice<GenericResource2D> destination = device.SliceElements(0, (ulong)count);
        return new(table.WriteBuffer(4, destination), commands =>
        {
            commands.CopyBuffer(upload.SliceElements(0, (ulong)count), destination);
            commands.Barrier(new ShaderDataBufferBarrierDesc<GenericResource2D> { Buffer = destination, Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.ComputeShader | PipelineStage.FragmentShader, ResourceAccess.ShaderRead) });
        });
    }

    internal static void Compute(GraphicsStagePreparationContext gpu, StageArtifactLease artifact, IArgumentTable table, GenericArguments2D arguments, long count, Action<IGraphicsCommandBuffer>? before, Action<Action<StageRecordContext>> encode)
    {
        uint groups = checked((uint)((count + 63) / 64));
        uint limit = gpu.Run.Device.Caps.MaxComputeWorkgroupsPerDimension;
        if (limit == 0 || gpu.Run.Device.Caps.MaxComputeWorkgroupSizeX < 64 || gpu.Run.Device.Caps.MaxComputeInvocationsPerWorkgroup < 64)
        {
            throw new NotSupportedException("64-lane compute workgroups are required.");
        }

        uint x = Math.Min(Math.Max(groups, 1), limit), y = checked((groups + x - 1) / x);
        if (y > limit)
        {
            throw new NotSupportedException("Dispatch grid exceeds native limits.");
        }

        arguments.CommandIndex = checked(x * 64);
        encode(record =>
        {
            before?.Invoke(record.Commands);
            if (groups == 0)
            {
                return;
            }

            IComputeEncoder pass = record.Commands.BeginComputePass(new());
            try
            {
                pass.SetPipeline(artifact.Compute!);
                pass.SetArgumentTable(table);
                SetComputeArguments(pass, arguments);
                pass.Dispatch(x, y);
            }
            finally
            {
                pass.End();
            }
        });
    }

    internal static void Raster(GraphicsStagePreparationContext gpu, StageArtifactLease artifact, IArgumentTable table, GenericArguments2D arguments, IGraphicsTextureView target, Action<IGraphicsCommandBuffer>? before, Action<Action<StageRecordContext>> encode)
    {
        var component = new BlendComponentDesc
        {
            Source = BlendFactor.One,
            Destination = BlendFactor.OneMinusSourceAlpha,
        };
        var state = new GraphicsRenderStateDesc
        {
            ColorTargets = [new()
            {
                BlendEnable = true,
                Color = component,
                Alpha = component,
            }

            ],
        };
        encode(record =>
        {
            before?.Invoke(record.Commands);
            IRenderEncoder pass = record.Commands.BeginRenderPass(new() { ColorAttachments = [new() { View = target, LoadOp = AttachmentLoadOp.Load, StoreOp = AttachmentStoreOp.Store }] });
            try
            {
                if (artifact.Mesh is { } mesh)
                {
                    pass.SetPipeline(mesh);
                }
                else
                {
                    pass.SetPipeline(artifact.Graphics!);
                }

                pass.SetArgumentTable(table);
                pass.SetRenderState(state);
                pass.SetBlendConstant(default);
                pass.SetStencilReference(0);
                pass.SetViewport(new(0, 0, arguments.Width, arguments.Height, 0, 1));
                pass.SetScissor(new(0, 0, arguments.Width, arguments.Height));

                // One hardware SrcOver operation after each command's correlated mean.
                for (uint j = 0; j < arguments.Commands; j++)
                {
                    GenericArguments2D draw = arguments with
                    {
                        CommandIndex = j,
                    };
                    SetRasterArguments(pass, draw);
                    if (artifact.Mesh is not null)
                    {
                        pass.DrawMeshTasks(1);
                    }
                    else
                    {
                        pass.Draw(3);
                    }
                }
            }
            finally
            {
                pass.End();
            }
        });
    }

    private static void SetComputeArguments(IComputeEncoder pass, GenericArguments2D args)
    {
        if (args.Coverage is not null)
        {
            var root = new GenericCoverageArguments2D(args);
            pass.SetArguments(in root);
        }
        else if (args.Paint is not null)
        {
            var root = new GenericPaintArguments2D(args);
            pass.SetArguments(in root);
        }
        else
        {
            var root = new GenericClipArguments2D(args);
            pass.SetArguments(in root);
        }
    }

    private static void SetRasterArguments(IRenderEncoder pass, GenericArguments2D args)
    {
        if (args.Coverage is not null)
        {
            var root = new GenericComposeArguments2D(args);
            pass.SetArguments(in root);
        }
        else
        {
            var root = new GenericFusedArguments2D(args);
            pass.SetArguments(in root);
        }
    }

    internal sealed record ResourceUpload(IGpuRef<GenericResource2D> Reference, Action<IGraphicsCommandBuffer> Encode);
}
