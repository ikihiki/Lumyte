using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Pipelines;
using Lumyte.Pipelines;

namespace Lumyte.Graphics.Drawing2D.Pipelines;

internal static class PagedGpuPreparation2D
{
    internal static StageRequirements Describe<T>(StageInputDescriptions inputs, GraphicsExecutionDomain domain, int outputStride, int targetIndex = -1, bool resources = false, GenericPagingLimits2D? limits = null)
    {
        ArgumentNullException.ThrowIfNull(limits);
        StageRequirements original = GenericGpuPreparation2D.Describe<T>(inputs, domain, outputStride, targetIndex, resources);
        var graphics = (GraphicsStageRequirements)original.Extensions[0];
        long perWindowHost = 4096 + (resources ? 2L * (GenericGpuPreparation2D.MaxResources + 1) * 128 : 0);
        long extraDevice = checked(limits.MaxWindows * ((2L * limits.MaxScenePages * 16) + (resources ? GenericGpuPreparation2D.ResourceBytes : 0)));
        return new(checked(65536 + (limits.MaxWindows * perWindowHost)), [new GraphicsStageRequirements(domain, checked(graphics.MaxDeviceBytes + extraDevice), graphics.Artifacts, graphics.InputAccesses)]);
    }

    internal static IArgumentTable Table(GraphicsStagePreparationContext gpu, int textureCount = 0, int samplerCount = 0)
        => gpu.CreateArgumentTable(new() { BufferCapacity = 6, TextureCapacity = checked((uint)textureCount), SamplerCapacity = checked((uint)samplerCount) });

    internal static GenericArguments2D Arguments(SchemaExtent extent, IGpuRef<GenericScenePage2D>? directory, uint pageCells, uint sceneCells = 0) => new()
    {
        ScenePages = directory!,
        PageCells = pageCells,
        SceneCells = sceneCells,
        Width = Convert.ToUInt32(extent.Metadata["width"]),
        Height = Convert.ToUInt32(extent.Metadata["height"]),
        Commands = Convert.ToUInt32(extent.Metadata["commandCount"]),
        TranslationX = extent.Metadata.TryGetValue("translationX", out object? x) ? Convert.ToSingle(x) : 0,
        TranslationY = extent.Metadata.TryGetValue("translationY", out object? y) ? Convert.ToSingle(y) : 0,
    };

    internal static Plan Admit(GraphicsStagePreparationContext gpu, long count, BufferSlice<Vector4> scene, GenericPagingLimits2D limits, DeviceBindings<ResourceViewsV1>? views = null)
    {
        RenderResourcesSnapshot2D? snapshot = views is null ? null : (RenderResourcesSnapshot2D)views.Extent.Metadata["resourceSnapshot"];
        return Admit(gpu.Run.Device.Caps, count, checked((long)scene.Count), limits, snapshot?.Views.Count ?? 0, snapshot?.Samplers.Count ?? 0);
    }

    internal static Plan Admit(DeviceCaps caps, long count, long sceneCells, GenericPagingLimits2D limits, int viewCount = 0, int samplerCount = 0)
    {
        ArgumentTableBindingLimits binding = caps.ArgumentTableBindingLimits;
        ulong maximum = Math.Min(caps.MaxStorageBufferBindingSize, binding.MaximumBufferRangeBytes);
        ulong alignment = Lcm(Math.Max(1u, caps.StorageBufferOffsetAlignment), Math.Max(1u, binding.BufferRangeAlignment));
        ulong sizeAlignment = Math.Max(1u, binding.BufferRangeSizeAlignment);
        ulong pageQuantum = Lcm(16, Lcm(alignment, sizeAlignment));
        ulong pageBytes = maximum / pageQuantum * pageQuantum;
        ulong sampleQuantum = Lcm(16, Lcm(alignment, sizeAlignment));
        ulong sampleCapacity = maximum / 16 / sampleQuantum * sampleQuantum;
        if (count <= 0 || count > GenericGpuPreparation2D.MaxSampleEntries || sceneCells <= 0 || pageBytes == 0 || sampleCapacity == 0 ||
            checked((ulong)count * 16) > caps.MaxBufferSize || checked((ulong)sceneCells * 16) > caps.MaxBufferSize ||
            checked((ulong)sceneCells * 16) % sizeAlignment != 0 || (ulong)count % Lcm(16, sizeAlignment) != 0)
        {
            throw new NotSupportedException("Paged generic storage requires finite aligned complete-pixel ranges.");
        }

        ulong pages = (((ulong)sceneCells * 16) + pageBytes - 1) / pageBytes;
        ulong windows = ((ulong)count + sampleCapacity - 1) / sampleCapacity;

        // Worst case: scene pages + directory + paint output + resource data + helper remap.
        ulong physical = pages + 4;
        ulong buffers = binding.DirectSlotBindings ? 6 : pages + 3;
        ulong textures = Math.Max(checked((uint)Math.Max(1, viewCount)), binding.MinimumTextureBindings);
        ulong samplers = Math.Max(checked((uint)samplerCount + 1), binding.MinimumSamplerBindings);
        ulong combined = checked(binding.ReservedBindings + (buffers * binding.BufferBindingWeight) + (textures * binding.TextureBindingWeight) + (samplers * binding.SamplerBindingWeight));
        if (pages > (ulong)limits.MaxScenePages || windows > (ulong)limits.MaxWindows || pages * 16 > maximum ||
            (pages * 16) % sizeAlignment != 0 || physical > caps.MaxStorageBuffersPerStage ||
            buffers > binding.BuffersPerDraw || binding.BufferSlots < 6 || combined > binding.CombinedBindings)
        {
            throw new NotSupportedException("Paged generic scene, output-window, or physical binding inventory exceeds the explicit finite policy.");
        }

        ulong dispatchSamples = Math.Min(sampleCapacity, (ulong)count);
        if (caps.MaxComputeWorkgroupsPerDimension == 0 || caps.MaxComputeWorkgroupSizeX < 64 || caps.MaxComputeInvocationsPerWorkgroup < 64 ||
            (dispatchSamples + 63) / 64 > (ulong)caps.MaxComputeWorkgroupsPerDimension * caps.MaxComputeWorkgroupsPerDimension)
        {
            throw new NotSupportedException("Paged generic stages require a finite 64-lane compute dispatch grid.");
        }

        var result = new SampleWindow[checked((int)windows)];
        for (int i = 0; i < result.Length; i++)
        {
            ulong offset = (ulong)i * sampleCapacity;
            result[i] = new(checked((uint)offset), checked((uint)Math.Min(sampleCapacity, (ulong)count - offset)));
        }

        return new(checked((uint)(pageBytes / 16)), checked((uint)sceneCells), checked((int)pages), result);
    }

    internal static void ValidateSource<T>(GraphicsStagePreparationContext gpu, BufferSlice<T> source)
        where T : unmanaged
    {
        DeviceCaps caps = gpu.Run.Device.Caps;
        ulong alignment = Lcm(Math.Max(1u, caps.StorageBufferOffsetAlignment), Math.Max(1u, caps.ArgumentTableBindingLimits.BufferRangeAlignment));
        if (source.OffsetInBytes % alignment != 0)
        {
            throw new NotSupportedException("Canonical source offset cannot satisfy the finite storage binding alignment.");
        }
    }

    internal static void ValidateResources(GraphicsStagePreparationContext gpu, DeviceBindings<ResourceViewsV1> bindings)
    {
        if (!bindings.Extent.Metadata.TryGetValue("resourceSnapshot", out object? value) || value is not RenderResourcesSnapshot2D snapshot)
        {
            throw new InvalidOperationException("Resource views require their exact immutable canonical snapshot.");
        }

        DeviceCaps caps = gpu.Run.Device.Caps;
        ArgumentTableBindingLimits binding = caps.ArgumentTableBindingLimits;
        uint textures = Math.Max(checked((uint)Math.Max(1, snapshot.Views.Count)), binding.MinimumTextureBindings);
        uint samplers = Math.Max(checked((uint)snapshot.Samplers.Count + 1), binding.MinimumSamplerBindings);
        ulong bytes = checked((ulong)(Math.Max(snapshot.Views.Count, snapshot.Samplers.Count) + 1) * 32);
        if (snapshot.Views.Count > GenericGpuPreparation2D.MaxResources || snapshot.Samplers.Count > GenericGpuPreparation2D.MaxResources ||
            textures > Math.Min(caps.MaxSampledTexturesPerStage, Math.Min(binding.TextureSlots, binding.TexturesPerDraw)) ||
            samplers > Math.Min(caps.MaxSamplersPerStage, Math.Min(binding.SamplerSlots, binding.SamplersPerDraw)) ||
            bytes > Math.Min(caps.MaxStorageBufferBindingSize, binding.MaximumBufferRangeBytes) || bytes % Math.Max(1u, binding.BufferRangeSizeAlignment) != 0)
        {
            throw new NotSupportedException("Canonical resources exceed the finite device binding limits before allocation.");
        }
    }

    internal static void Reserve(GraphicsStagePreparationContext gpu, long count, int stride, Plan plan, DeviceBindings<ResourceViewsV1>? views = null)
    {
        long directory = checked((long)plan.ScenePages * ShaderSize<GenericScenePage2D>());
        long resources = 0, fallback = 0;
        if (views is not null)
        {
            var snapshot = (RenderResourcesSnapshot2D)views.Extent.Metadata["resourceSnapshot"];
            resources = checked((long)(Math.Max(snapshot.Views.Count, snapshot.Samplers.Count) + 1) * ShaderSize<GenericResource2D>());
            fallback = snapshot.Views.Count == 0 ? 4 : 0;
        }

        long transfer = checked((directory + resources) * plan.Windows.Length);
        long scratch = checked((count * stride) + (2 * transfer) + (fallback * plan.Windows.Length));
        gpu.PreflightAllocations(scratch, transfer);
    }

    internal static BufferSlice<T> Window<T>(BufferSlice<T> source, SampleWindow window)
        where T : unmanaged => source.Buffer.SliceElements(checked(source.Offset + window.Offset), window.Count);

    internal static async ValueTask<SceneUpload> SceneAsync(GraphicsStagePreparationContext gpu, IArgumentTable table, StageArtifactLease artifact, BufferSlice<Vector4> scene, Plan plan, CancellationToken ct)
    {
        var entries = new GenericScenePage2D[plan.ScenePages];
        for (int i = 0; i < entries.Length; i++)
        {
            ulong offset = (ulong)i * plan.PageCells;
            entries[i].Page = table.WriteBuffer((uint)i, scene.Buffer.SliceElements(scene.Offset + offset, Math.Min(plan.PageCells, scene.Count - offset)));
        }

        IGraphicsShaderDataBuffer<GenericScenePage2D> upload = gpu.CreateShaderDataScratchBuffer<GenericScenePage2D>(artifact, (ulong)entries.Length, MemoryPreference.Upload);
        IGraphicsShaderDataBuffer<GenericScenePage2D> device = gpu.CreateShaderDataScratchBuffer<GenericScenePage2D>(artifact, (ulong)entries.Length);
        await upload.MapAsync(ct);
        try
        {
            upload.CopyFrom(entries);
        }
        finally
        {
            upload.Unmap();
        }

        ShaderDataSlice<GenericScenePage2D> destination = device.SliceElements(0, (ulong)entries.Length);
        return new(table.WriteBuffer(3, destination), commands =>
        {
            commands.Barrier(new ShaderDataBufferBarrierDesc<GenericScenePage2D> { Buffer = upload.SliceElements(0, (ulong)entries.Length), Before = new(PipelineStage.Host, ResourceAccess.HostWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
            commands.CopyBuffer(upload.SliceElements(0, (ulong)entries.Length), destination);
            commands.Barrier(new ShaderDataBufferBarrierDesc<GenericScenePage2D> { Buffer = destination, Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead) });
        });
    }

    internal static async ValueTask<ResourceUpload> ResourcesAsync(GraphicsStagePreparationContext gpu, DeviceBindings<ResourceViewsV1> bindings, IArgumentTable table, StageArtifactLease artifact, CancellationToken ct)
    {
        if (!bindings.Extent.Metadata.TryGetValue("resourceSnapshot", out object? value) || value is not RenderResourcesSnapshot2D snapshot)
        {
            throw new InvalidOperationException("Resource views must retain their exact immutable canonical resource snapshot.");
        }

        if (snapshot.Views.Count > GenericGpuPreparation2D.MaxResources || snapshot.Samplers.Count > GenericGpuPreparation2D.MaxResources)
        {
            throw new NotSupportedException("Generic GPU resource table exceeds 128 source entries.");
        }

        int count = Math.Max(snapshot.Views.Count, snapshot.Samplers.Count) + 1;
        IGraphicsShaderDataBuffer<GenericResource2D> upload = gpu.CreateShaderDataScratchBuffer<GenericResource2D>(artifact, checked((ulong)count), MemoryPreference.Upload);
        IGraphicsShaderDataBuffer<GenericResource2D> device = gpu.CreateShaderDataScratchBuffer<GenericResource2D>(artifact, checked((ulong)count));
        var entries = new GenericResource2D[count];
        IGpuRef<IGraphicsSampler> fallback = table.WriteSampler(0, artifact.Sampler!);
        IGpuRef<IGraphicsTextureView> fallbackTexture;
        IGraphicsTexture? fallbackAllocation = null;
        if (snapshot.Views.Count > 0)
        {
            fallbackTexture = table.WriteTexture(0, bindings.GetTexture(0));
        }
        else
        {
            gpu.ReserveNativeScratch(4);
            IGraphicsTexture texture = gpu.Run.Device.CreateTexture(new() { Width = 1, Height = 1, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.Sampled });
            gpu.Run.Own(texture);
            fallbackAllocation = texture;
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
        return new(table.WriteBuffer(5, destination), commands =>
        {
            if (fallbackAllocation is not null)
            {
                commands.Barrier(new TextureBarrierDesc { Texture = fallbackAllocation, Range = new(0, 1, 0, 1), BeforeState = TextureState.Undefined, AfterState = TextureState.Sampled, Before = default, After = new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead) });
            }

            commands.Barrier(new ShaderDataBufferBarrierDesc<GenericResource2D> { Buffer = upload.SliceElements(0, (ulong)count), Before = new(PipelineStage.Host, ResourceAccess.HostWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
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

    internal static void Raster(GraphicsStagePreparationContext gpu, StageArtifactLease artifact, IReadOnlyList<(IArgumentTable Table, GenericArguments2D Arguments)> bindings, IGraphicsTextureView target, Action<Action<StageRecordContext>> encode)
    {
        GenericArguments2D arguments = bindings[0].Arguments;
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

                pass.SetRenderState(state);
                pass.SetBlendConstant(default);
                pass.SetStencilReference(0);
                pass.SetViewport(new(0, 0, arguments.Width, arguments.Height, 0, 1));
                pass.SetScissor(new(0, 0, arguments.Width, arguments.Height));

                ulong samplesPerCommand = checked((ulong)arguments.Width * arguments.Height * 16);
                for (uint command = 0; command < arguments.Commands; command++)
                {
                    ulong first = command * samplesPerCommand, end = first + samplesPerCommand;
                    foreach ((IArgumentTable table, GenericArguments2D window) in bindings)
                    {
                        if (window.SampleOffset < end && (ulong)window.SampleOffset + window.SampleCount > first)
                        {
                            pass.SetArgumentTable(table);
                            GenericArguments2D draw = window with { CommandIndex = command };
                            var root = new GenericPagedComposeArguments2D(draw);
                            pass.SetArguments(in root);
                            pass.Draw(3);
                        }
                    }
                }
            }
            finally
            {
                pass.End();
            }
        });
    }

    private static int ShaderSize<T>()
        where T : struct, IShaderArguments => T.TransferSize;

    private static ulong Lcm(ulong a, ulong b)
    {
        ulong x = a, y = b;
        while (y != 0)
        {
            (x, y) = (y, x % y);
        }

        return checked(a / x * b);
    }

    private static void SetComputeArguments(IComputeEncoder pass, GenericArguments2D args)
    {
        if (args.Coverage is not null)
        {
            var root = new GenericPagedCoverageArguments2D(args);
            pass.SetArguments(in root);
        }
        else if (args.Paint is not null)
        {
            var root = new GenericPagedPaintArguments2D(args);
            pass.SetArguments(in root);
        }
        else
        {
            var root = new GenericPagedClipArguments2D(args);
            pass.SetArguments(in root);
        }
    }

    internal readonly record struct SampleWindow(uint Offset, uint Count);

    internal sealed record Plan(uint PageCells, uint SceneCells, int ScenePages, SampleWindow[] Windows);

    internal sealed record SceneUpload(IGpuRef<GenericScenePage2D> Reference, Action<IGraphicsCommandBuffer> Encode);

    internal sealed record ResourceUpload(IGpuRef<GenericResource2D> Reference, Action<IGraphicsCommandBuffer> Encode);
}
