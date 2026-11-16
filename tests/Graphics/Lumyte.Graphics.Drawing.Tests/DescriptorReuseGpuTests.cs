using System.Collections;
using System.Numerics;
using System.Reflection;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Vulkan;
using Xunit;

namespace Lumyte.Graphics.Drawing.Tests;

/// <summary>Checks immutable per-draw descriptors, root ranges and command-owned retirement on Vulkan.</summary>
public sealed class DescriptorReuseGpuTests
{
    /// <summary>Checks ordered pixels across root-only updates, replacement, release, tables, layouts and passes.</summary>
    [DrawingGpuFact]
    public void OrderedDrawsPreserveEveryDescriptorSnapshot()
    {
        using var scene = new Scene();
        using IGraphicsCommandBuffer commands = scene.Device.CreateCommandBuffer(new());
        scene.BeginTarget(commands);
        IRenderEncoder render = scene.BeginPass(commands, clear: true);
        try
        {
            scene.Draw(render, 0, scene.Table, scene.Pipeline);
            scene.Repeat(render);
            scene.Draw(render, 1, scene.Table, scene.Pipeline);
            scene.Table.WriteTexture(0, scene.Views[1]);
            scene.Draw(render, 2, scene.Table, scene.Pipeline);
            scene.Table.WriteSampler(0, scene.SamplerB);
            scene.Draw(render, 3, scene.Table, scene.Pipeline);
            scene.Table.ReleaseTexture(0);
            scene.Table.WriteTexture(0, scene.Views[3]);
            scene.Draw(render, 4, scene.Table, scene.Pipeline);
            scene.Draw(render, 5, scene.TableB, scene.Pipeline);
            scene.Draw(render, 6, scene.TableB, scene.PipelineB);
            scene.Draw(render, 6, scene.TableB, scene.Pipeline);
            scene.Draw(render, 6, scene.TableB, scene.PipelineB);
        }
        finally
        {
            render.End();
        }

        render = scene.BeginPass(commands, clear: false);
        try
        {
            scene.Draw(render, 7, scene.TableB, scene.PipelineB);
        }
        finally
        {
            render.End();
        }

        IEnumerable bindings = Bindings(commands);
        scene.CompleteAndVerify(commands);
        Assert.Equal(5, bindings.Cast<object>().Count());
        Assert.Equal(5, Counter(scene, "AllocatedSets"));
        IShaderReference[][] records = Snapshots(commands);
        Assert.Equal(10, records.Length);
        IShaderReference[] original = records.Single(a => a.Any(r => ReferenceEquals(r.Resource, scene.DrawData) && r.OffsetInBytes == 0) && a.Any(r => ReferenceEquals(r.Resource, scene.Views[0])));
        IShaderReference[] next = records.Single(a => a.Any(r => ReferenceEquals(r.Resource, scene.DrawData) && r.OffsetInBytes == scene.DrawData.ShaderElementStrideInBytes));
        Assert.Contains(original, r => ReferenceEquals(r.Resource, scene.DrawData) && r.OffsetInBytes == 0);
        Assert.Contains(next, r => ReferenceEquals(r.Resource, scene.DrawData) && r.OffsetInBytes == scene.DrawData.ShaderElementStrideInBytes);
        commands.Dispose();
        Assert.Empty(bindings);
        Assert.Empty(Snapshots(commands));
        Assert.Equal(2, Counter(scene, "LiveSets"));
        scene.Table.Dispose();
        scene.TableB.Dispose();
        Assert.Equal(0, Counter(scene, "LiveSets"));
        commands.Dispose();
    }

    /// <summary>Records thirty-two distinct root ranges with exactly one command-owned native descriptor binding.</summary>
    [DrawingGpuFact]
    public void UnchangedDescriptorsReuseOnePoolForThirtyTwoRoots()
    {
        using var scene = new Scene();
        using IGraphicsCommandBuffer commands = scene.Device.CreateCommandBuffer(new());
        scene.BeginTarget(commands);
        IRenderEncoder render = scene.BeginPass(commands, clear: true);
        try
        {
            for (int i = 0; i < 32; i++)
            {
                scene.Draw(render, i < 5 ? i : i + 3, scene.Table, scene.Pipeline);
            }
        }
        finally
        {
            render.End();
        }

        commands.Finish();
        IEnumerable bindings = Bindings(commands);
        Assert.Single(bindings.Cast<object>());
        object cached = ((IDictionary)scene.Table.GetType().GetField("_sets", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scene.Table)!).Values.Cast<object>().Single();
        IShaderReference[] cachedResources = Registrations(cached);
        Assert.Equal(2, cachedResources.Length);
        Assert.All(cachedResources, r => Assert.True(r.Resource is IGraphicsTextureView or IGraphicsSampler));
        scene.Table.ReleaseBuffer(1);
        Assert.DoesNotContain(cachedResources, r => ReferenceEquals(r.Resource, scene.DrawData));
        IShaderReference[][] snapshots = Snapshots(commands);
        Assert.Equal(32, snapshots.Length);
        Assert.Equal(32, TableCount(scene.Table, "SnapshotBuildCount"));
        Assert.Equal(1, TableCount(scene.Table, "LayoutScanCount"));
        Assert.Equal(1, Counter(scene, "AllocatedSets"));
        ulong[] offsets = snapshots.Select(a => Assert.Single(a, r => ReferenceEquals(r.Resource, scene.DrawData)).OffsetInBytes).ToArray();
        Assert.Equal(32, offsets.Distinct().Count());
        Assert.Contains(34 * scene.DrawData.ShaderElementStrideInBytes, offsets);
        scene.Table.Dispose();
        Assert.Equal(1, Counter(scene, "LiveSets"));
        commands.Dispose();
        Assert.Empty(bindings);
        Assert.Equal(0, Counter(scene, "LiveSets"));
    }

    /// <summary>Unchanged draws reuse all three identities; A/B/A retains each Set once.</summary>
    [DrawingGpuFact]
    public void RepeatedDrawsAndTableSwitchesTrackActualBinds()
    {
        using var scene = new Scene();
        using IGraphicsCommandBuffer commands = scene.Device.CreateCommandBuffer(new());
        scene.BeginTarget(commands);
        IRenderEncoder render = scene.BeginPass(commands, clear: true);
        try
        {
            scene.Draw(render, 0, scene.Table, scene.Pipeline);
            for (int i = 0; i < 32; i++)
            {
                scene.Repeat(render);
            }

            Assert.Equal(1, NativeBindCount(render, "_pipelineBindCount"));
            Assert.Equal(1, NativeBindCount(render, "_descriptorBindCount"));
            Assert.Single(Snapshots(commands));
            Assert.Equal(1, TableCount(scene.Table, "SnapshotBuildCount"));
            Assert.Equal(1, TableCount(scene.Table, "LayoutScanCount"));
            GraphicsRenderStateDesc state = ModelDrawing.CreateRenderState(ModelDrawing.DefaultMaterial, doubleSided: true) with { DepthStencil = new() };
            render.SetRenderState(state with { Rasterization = state.Rasterization with { FrontFace = FrontFace.Clockwise } });
            scene.Repeat(render);
            Assert.Equal(2, NativeBindCount(render, "_pipelineBindCount"));
            Assert.Equal(1, NativeBindCount(render, "_descriptorBindCount"));
            render.SetRenderState(state);
            scene.Draw(render, 5, scene.TableB, scene.Pipeline);
            scene.Draw(render, 1, scene.Table, scene.Pipeline);
            Assert.Equal(3, NativeBindCount(render, "_pipelineBindCount"));
            Assert.Equal(3, NativeBindCount(render, "_descriptorBindCount"));
            Assert.Equal(2, Bindings(commands).Cast<object>().Count());
            Assert.Equal(3, Snapshots(commands).Length);
            scene.Draw(render, 2, scene.Table, scene.PipelineB);
            Assert.Equal(4, NativeBindCount(render, "_pipelineBindCount"));
            Assert.Equal(4, NativeBindCount(render, "_descriptorBindCount"));
        }
        finally
        {
            render.End();
        }

        render = scene.BeginPass(commands, clear: false);
        scene.Draw(render, 0, scene.Table, scene.Pipeline);
        Assert.Equal(1, NativeBindCount(render, "_pipelineBindCount"));
        Assert.Equal(1, NativeBindCount(render, "_descriptorBindCount"));
        render.End();
        commands.Finish();
    }

    /// <summary>Distinct inline roots survive skipped binds and sparse descriptor layout changes.</summary>
    [DrawingGpuFact]
    public void StableRootsAndLayoutShapeChangesPreservePixels()
    {
        using var scene = new Scene();
        using IArgumentTable table = scene.CreateWideTable();
        IGpuRef<ModelDrawArguments> rows = table.WriteBuffer(1, scene.DrawData.SliceElements(0, 40));
        using IGraphicsCommandBuffer commands = scene.Device.CreateCommandBuffer(new());
        scene.BeginTarget(commands);
        IRenderEncoder render = scene.BeginPass(commands, clear: true);
        try
        {
            for (int row = 0; row < 8; row++)
            {
                if (row == 1)
                {
                    table.WriteTexture(3, scene.Views[0]);
                }
                else if (row == 2)
                {
                    table.ReleaseTexture(3);
                }

                render.SetPipeline(scene.Pipeline);
                render.SetArgumentTable(table);
                render.SetViewport(new(row * 8, 0, 8, 16, 0, 1));
                render.SetScissor(new((uint)row * 8, 0, 8, 16));
                var root = new ModelDrawRoot(rows.GetElement((ulong)row));
                render.SetArguments(in root);
                scene.Repeat(render);
            }

            Assert.Equal(3, NativeBindCount(render, "_pipelineBindCount"));
            Assert.Equal(3, NativeBindCount(render, "_descriptorBindCount"));
            Assert.Equal(3, TableCount(table, "SnapshotBuildCount"));
            Assert.Equal(3, TableCount(table, "LayoutScanCount"));
            Assert.Equal(3, Snapshots(commands).Length);
        }
        finally
        {
            render.End();
        }

        scene.CompleteAndVerify(commands, [0, 1, -1, -1, -1, 0, 1, 2]);
    }

    /// <summary>Rejects explicitly disposed resources on reuse, without executing invalid recorded commands.</summary>
    /// <param name="resource">The disposed resource category.</param>
    [DrawingGpuTheory]
    [InlineData("view")]
    [InlineData("texture")]
    [InlineData("sampler")]
    [InlineData("table")]
    public void DisposedDescriptorResourcesAreRejected(string resource)
    {
        using var scene = new Scene();
        using IGraphicsCommandBuffer commands = scene.Device.CreateCommandBuffer(new());
        scene.BeginTarget(commands);
        IRenderEncoder render = scene.BeginPass(commands, clear: true);
        try
        {
            scene.Draw(render, 0, scene.Table, scene.Pipeline);
            switch (resource)
            {
                case "view": scene.Views[0].Dispose(); break;
                case "texture": scene.Textures[0].Dispose(); break;
                case "sampler": scene.Sampler.Dispose(); break;
                default: scene.Table.Dispose(); break;
            }

            int snapshots = Snapshots(commands).Length;
            int sets = Bindings(commands).Cast<object>().Count();
            int pipelines = NativeBindCount(render, "_pipelineBindCount");
            int descriptors = NativeBindCount(render, "_descriptorBindCount");
            Assert.Throws<ObjectDisposedException>(() => scene.Repeat(render));
            Assert.Equal(pipelines, NativeBindCount(render, "_pipelineBindCount"));
            Assert.Equal(descriptors, NativeBindCount(render, "_descriptorBindCount"));
            Assert.Equal(snapshots, Snapshots(commands).Length);
            Assert.Equal(sets, Bindings(commands).Cast<object>().Count());
        }
        finally
        {
            render.End();
        }
    }

    /// <summary>Commits descriptor generations only for successful sampled-resource mutations.</summary>
    [DrawingGpuFact]
    public void BufferUpdatesAndFailedWritesDoNotChangeDescriptorGeneration()
    {
        using var scene = new Scene();
        PropertyInfo generation = scene.Table.GetType().GetProperty("DescriptorGeneration", BindingFlags.Instance | BindingFlags.NonPublic)!;
        long first = (long)generation.GetValue(scene.Table)!;
        scene.Table.WriteBuffer(1, scene.DrawData.SliceElements(0, 1));
        Assert.Equal(first, generation.GetValue(scene.Table));
        Assert.Throws<ArgumentOutOfRangeException>(() => scene.Table.WriteTexture(1, scene.Views[1]));
        Assert.Throws<ArgumentOutOfRangeException>(() => scene.Table.WriteSampler(1, scene.SamplerB));
        scene.Table.ReleaseBuffer(1);
        scene.Table.WriteTexture(0, scene.Views[0]);
        scene.Table.WriteSampler(0, scene.SamplerB);
        Assert.Equal(first, generation.GetValue(scene.Table));
        using IGraphicsCommandBuffer commands = scene.Device.CreateCommandBuffer(new());
        scene.BeginTarget(commands);
        IRenderEncoder render = scene.BeginPass(commands, clear: true);
        scene.Draw(render, 0, scene.Table, scene.Pipeline);
        scene.Table.WriteTexture(0, scene.Views[1]);
        scene.Table.ReleaseTexture(0);
        scene.Table.WriteTexture(0, scene.Views[0]);
        scene.Table.ReleaseSampler(0);
        scene.Table.WriteSampler(0, scene.Sampler);
        Assert.Equal(first + 1, generation.GetValue(scene.Table));
        Assert.Equal(1, Counter(scene, "AllocatedSets"));
        scene.Draw(render, 1, scene.Table, scene.Pipeline);
        Assert.Equal(2, Counter(scene, "AllocatedSets"));
        render.End();
    }

    /// <summary>Reuses canonical layouts across passes, command disposal and pipeline recreation.</summary>
    [DrawingGpuFact]
    public void UnchangedTableSurvivesCommandsFramesAndPipelineRecreation()
    {
        using var scene = new Scene();
        for (int frame = 0; frame < 4; frame++)
        {
            using IGraphicsCommandBuffer commands = scene.Device.CreateCommandBuffer(new());
            scene.BeginTarget(commands);
            for (int pass = 0; pass < 2; pass++)
            {
                IRenderEncoder render = scene.BeginPass(commands, clear: pass == 0);
                scene.Draw(render, 0, scene.Table, frame % 2 == 0 ? scene.Pipeline : scene.PipelineB);
                render.End();
            }

            commands.Finish();
            using IGraphicsSubmission submission = scene.Device.Queue.Submit([commands]);
            submission.WaitAsync().GetAwaiter().GetResult();
            Assert.Equal(1, Counter(scene, "AllocatedSets"));
        }

        scene.Pipeline.Dispose();
        using IGraphicsShader vertex = scene.Device.CreateShader(DrawingShaders.LoadModelVertex());
        using IGraphicsShader fragment = scene.Device.CreateShader(DrawingShaders.LoadModelFragment());
        using IGraphicsPipeline replacement = scene.Device.CreateGraphicsPipeline(new() { VertexShader = vertex, FragmentShader = fragment });
        using IGraphicsCommandBuffer last = scene.Device.CreateCommandBuffer(new());
        scene.BeginTarget(last);
        IRenderEncoder final = scene.BeginPass(last, clear: true);
        scene.Draw(final, 0, scene.Table, replacement);
        final.End();
        last.Finish();
        Assert.Equal(1, Counter(scene, "AllocatedSets"));
    }

    /// <summary>Shared pages cross capacity, then old generations retire without an unbounded table history.</summary>
    [DrawingGpuFact]
    public void SharedPagesCrossCapacityAndReclaimOldGenerations()
    {
        using var scene = new Scene();
        using IGraphicsCommandBuffer commands = scene.Device.CreateCommandBuffer(new());
        scene.BeginTarget(commands);
        IRenderEncoder render = scene.BeginPass(commands, clear: true);
        for (int i = 0; i < 70; i++)
        {
            scene.Table.WriteTexture(0, scene.Views[i % 2]);
            scene.Draw(render, 0, scene.Table, scene.Pipeline);
        }

        scene.Draw(render, 5, scene.TableB, scene.PipelineB);
        render.End();
        commands.Finish();
        Assert.Equal(71, Counter(scene, "AllocatedSets"));
        Assert.Equal(3, Counter(scene, "CreatedPools"));
        Assert.Equal(71, Counter(scene, "LiveSets"));
        scene.Table.Dispose();
        scene.TableB.Dispose();
        Assert.Equal(71, Counter(scene, "LiveSets"));
        commands.Dispose();
        Assert.Equal(0, Counter(scene, "LiveSets"));
        Assert.Equal(0, Counter(scene, "PoolCount"));
        Assert.Equal(71, Counter(scene, "FreedSets"));
    }

    /// <summary>Device destruction closes shared pools; delayed table release must not call a dead device.</summary>
    [DrawingGpuFact]
    public void TableCacheReleaseAfterDeviceDestructionIsManagedOnly()
    {
        using var scene = new Scene();
        using IGraphicsCommandBuffer commands = scene.Device.CreateCommandBuffer(new());
        scene.BeginTarget(commands);
        IRenderEncoder render = scene.BeginPass(commands, clear: true);
        scene.Draw(render, 0, scene.Table, scene.Pipeline);
        render.End();
        commands.Finish();
        commands.Dispose();
        Assert.Equal(1, Counter(scene, "LiveSets"));
        scene.DisposeKeepingTables();
        scene.Table.Dispose();
        scene.TableB.Dispose();
        Assert.Equal(0, Counter(scene, "PoolCount"));
    }

    private static int TableCount(IArgumentTable table, string property) => (int)table.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(table)!;

    private static int NativeBindCount(IRenderEncoder encoder, string field) => (int)encoder.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(encoder)!;

    private static IEnumerable Bindings(IGraphicsCommandBuffer commands) => (IEnumerable)commands.GetType().GetField("_bindings", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(commands)!;

    private static IShaderReference[][] Snapshots(IGraphicsCommandBuffer commands) => ((IEnumerable)commands.GetType().GetField("_registrationSnapshots", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(commands)!).Cast<IShaderReference[]>().ToArray();

    private static IShaderReference[] Registrations(object record) => (IShaderReference[])record.GetType().GetField("_registrations", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(record)!;

    private static long Counter(Scene scene, string name)
    {
        object allocator = scene.Device.GetType().GetProperty("Descriptors", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scene.Device)!;
        return Convert.ToInt64(allocator.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(allocator));
    }

    private sealed class Scene : IDisposable
    {
        private readonly ShaderArtifact _artifact = DrawingShaders.LoadModelVertex();
        private readonly IGraphicsShader _vertex;
        private readonly IGraphicsShader _fragment;
        private readonly ModelMesh _mesh;
        private readonly IGraphicsShaderDataBuffer<ModelDrawArguments> _upload;
        private readonly IGraphicsTexture _target;
        private readonly IGraphicsTextureView _view;
        private readonly IGraphicsBuffer<byte> _readback;

        public Scene()
        {
            Device = VulkanDevice.Create(physicalDeviceIndex: 0);
            _vertex = Device.CreateShader(_artifact);
            _fragment = Device.CreateShader(DrawingShaders.LoadModelFragment());
            Pipeline = Device.CreateGraphicsPipeline(new() { VertexShader = _vertex, FragmentShader = _fragment });
            PipelineB = Device.CreateGraphicsPipeline(new() { VertexShader = _vertex, FragmentShader = _fragment });
            _target = Device.CreateTexture(new() { Width = 64, Height = 16, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
            _view = _target.CreateView();
            _readback = Device.CreateBuffer<byte>(new() { Count = 64 * 16 * 4, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
            Textures = Enumerable.Range(0, 4).Select(_ => Device.CreateTexture(new() { Width = 1, Height = 1, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.Sampled | TextureUsage.CopyDestination })).ToArray();
            Views = Textures.Select(t => t.CreateView()).ToArray();
            Sampler = Device.CreateSampler(new());
            SamplerB = Device.CreateSampler(new() { MinFilter = FilterMode.Nearest, MagFilter = FilterMode.Nearest });
            ModelVertex[] vertices =
            [
                new(new(-1, -1, 0), Vector3.UnitZ, Vector4.UnitX, new(0.5f), Vector4.One),
                new(new(1, -1, 0), Vector3.UnitZ, Vector4.UnitX, new(0.5f), Vector4.One),
                new(new(1, 1, 0), Vector3.UnitZ, Vector4.UnitX, new(0.5f), Vector4.One),
                new(new(-1, 1, 0), Vector3.UnitZ, Vector4.UnitX, new(0.5f), Vector4.One),
            ];
            _mesh = ModelMesh.CreateAsync(Device, vertices, new ushort[] { 0, 1, 2, 0, 2, 3 }, _artifact).GetAwaiter().GetResult();
            DrawData = Device.CreateShaderDataBuffer<ModelDrawArguments>(_artifact, 40);
            _upload = Device.CreateShaderDataBuffer<ModelDrawArguments>(_artifact, 40, MemoryPreference.Upload);
            Table = Device.CreateArgumentTable(new() { BufferCapacity = 2, TextureCapacity = 1, SamplerCapacity = 1 });
            TableB = Device.CreateArgumentTable(new() { BufferCapacity = 2, TextureCapacity = 1, SamplerCapacity = 1 });
            var white = new SampledTexture(Table.WriteTexture(0, Views[0]), Table.WriteSampler(0, Sampler));
            var whiteB = new SampledTexture(TableB.WriteTexture(0, Views[0]), TableB.WriteSampler(0, SamplerB));
            IGpuRef<ModelVertex> vertexRef = Table.WriteBuffer(0, _mesh.Vertices.SliceElements(0, 4));
            IGpuRef<ModelVertex> vertexRefB = TableB.WriteBuffer(0, _mesh.Vertices.SliceElements(0, 4));
            Vector4 red = new(1, 0, 0, 1);
            Vector4 green = new(0, 1, 0, 1);
            Vector4[] colors = [red, green, Vector4.One, Vector4.One, Vector4.One, red, green, new(0, 0, 1, 1)];
            ModelDrawArguments[] arguments = Enumerable.Range(0, 40).Select(i => ModelDrawing.CreateArguments(i is >= 5 and < 8 ? vertexRefB : vertexRef, ModelDrawing.DefaultMaterial with { Unlit = 1, BaseColor = colors[i % 8] }, i is >= 5 and < 8 ? whiteB : white, i is >= 5 and < 8 ? whiteB : white)).ToArray();
            _upload.MapAsync().GetAwaiter().GetResult();
            _upload.CopyFrom(arguments);
            _upload.Unmap();
            using IGraphicsBuffer<byte> textureUpload = Device.CreateBuffer<byte>(new() { Count = 16, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload });
            textureUpload.MapAsync().GetAwaiter().GetResult();
            textureUpload.CopyFrom([255, 255, 255, 255, 255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255]);
            textureUpload.Unmap();
            using IGraphicsCommandBuffer commands = Device.CreateCommandBuffer(new());
            _mesh.RecordInitialUpload(commands);
            commands.Barrier(new ShaderDataBufferBarrierDesc<ModelDrawArguments> { Buffer = _upload.SliceElements(0, 40), Before = new(PipelineStage.Host, ResourceAccess.HostWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
            commands.CopyBuffer(_upload.SliceElements(0, 40), DrawData.SliceElements(0, 40));
            commands.Barrier(new ShaderDataBufferBarrierDesc<ModelDrawArguments> { Buffer = DrawData.SliceElements(0, 40), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.VertexShader | PipelineStage.FragmentShader, ResourceAccess.ShaderRead) });
            for (int i = 0; i < 4; i++)
            {
                commands.Barrier(new TextureBarrierDesc { Texture = Textures[i], Range = new(0, 1, 0, 1), BeforeState = TextureState.Undefined, AfterState = TextureState.CopyDestination, Before = default, After = new(PipelineStage.Copy, ResourceAccess.CopyWrite) });
                commands.CopyBufferToTexture(new() { Buffer = textureUpload.SliceElements((ulong)i * 4, 4), BytesPerRow = 4, RowsPerImage = 1 }, new() { Texture = Textures[i], Width = 1, Height = 1 });
                commands.Barrier(new TextureBarrierDesc { Texture = Textures[i], Range = new(0, 1, 0, 1), BeforeState = TextureState.CopyDestination, AfterState = TextureState.Sampled, Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.FragmentShader, ResourceAccess.ShaderRead) });
            }

            commands.Finish();
            using IGraphicsSubmission submission = Device.Queue.Submit([commands]);
            submission.WaitAsync().GetAwaiter().GetResult();
            _mesh.ReleaseUploadResources();
        }

        public VulkanDevice Device { get; }

        public IGraphicsPipeline Pipeline { get; }

        public IGraphicsPipeline PipelineB { get; }

        public IArgumentTable Table { get; }

        public IArgumentTable TableB { get; }

        public IGraphicsShaderDataBuffer<ModelDrawArguments> DrawData { get; }

        public IGraphicsTexture[] Textures { get; }

        public IGraphicsTextureView[] Views { get; }

        public IGraphicsSampler Sampler { get; }

        public IGraphicsSampler SamplerB { get; }

        public void BeginTarget(IGraphicsCommandBuffer commands) => commands.Barrier(new TextureBarrierDesc { Texture = _target, Range = new(0, 1, 0, 1), BeforeState = TextureState.Undefined, AfterState = TextureState.ColorAttachment, Before = default, After = new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite) });

        public IRenderEncoder BeginPass(IGraphicsCommandBuffer commands, bool clear)
        {
            if (!clear)
            {
                commands.Barrier(new TextureBarrierDesc { Texture = _target, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.ColorAttachment, Before = new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite), After = new(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite) });
            }

            IRenderEncoder render = commands.BeginRenderPass(new() { ColorAttachments = [new() { View = _view, LoadOp = clear ? AttachmentLoadOp.Clear : AttachmentLoadOp.Load, StoreOp = AttachmentStoreOp.Store }] });
            render.SetRenderState(ModelDrawing.CreateRenderState(ModelDrawing.DefaultMaterial, doubleSided: true) with { DepthStencil = new() });
            render.SetBlendConstant(new(0, 0, 0, 0));
            render.SetStencilReference(0);
            return render;
        }

        public void Draw(IRenderEncoder render, int i, IArgumentTable table, IGraphicsPipeline pipeline)
        {
            render.SetPipeline(pipeline);
            render.SetArgumentTable(table);
            render.SetViewport(new((i % 8) * 8, 0, 8, 16, 0, 1));
            render.SetScissor(new(((uint)i % 8) * 8, 0, 8, 16));
            var root = new ModelDrawRoot(table.WriteBuffer(1, DrawData.SliceElements((ulong)i, 1)));
            render.SetArguments(in root);
            _mesh.Draw(render);
        }

        public void Repeat(IRenderEncoder render) => _mesh.Draw(render);

        public IArgumentTable CreateWideTable()
        {
            IArgumentTable table = Device.CreateArgumentTable(new() { TextureCapacity = 4, SamplerCapacity = 1, BufferCapacity = 2 });
            table.WriteTexture(0, Views[0]);
            table.WriteSampler(0, Sampler);
            table.WriteBuffer(0, _mesh.Vertices.SliceElements(0, 4));
            return table;
        }

        public void CompleteAndVerify(IGraphicsCommandBuffer commands, int[]? expectedChannels = null)
        {
            commands.Barrier(new TextureBarrierDesc { Texture = _target, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.CopySource, Before = new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
            commands.CopyTextureToBuffer(new() { Texture = _target, Width = 64, Height = 16 }, new() { Buffer = _readback.SliceElements(0, _readback.Count), BytesPerRow = 256, RowsPerImage = 16 });
            commands.Barrier(new BufferBarrierDesc<byte> { Buffer = _readback.SliceElements(0, _readback.Count), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Host, ResourceAccess.HostRead) });
            commands.Finish();
            using IGraphicsSubmission submission = Device.Queue.Submit([commands]);
            submission.WaitAsync().GetAwaiter().GetResult();
            _readback.MapAsync().GetAwaiter().GetResult();
            byte[] pixels = new byte[4096];
            _readback.CopyTo(pixels);
            _readback.Unmap();
            int[] channel = expectedChannels ?? [0, 1, 0, 0, 2, 0, 1, 2];
            for (int y = 0; y < 16; y++)
            {
                for (int x = 0; x < 64; x++)
                {
                    int offset = ((y * 64) + x) * 4;
                    for (int c = 0; c < 3; c++)
                    {
                        Assert.Equal(channel[x / 8] == -1 || c == channel[x / 8] ? 255 : 0, pixels[offset + c]);
                    }

                    Assert.Equal(255, pixels[offset + 3]);
                }
            }
        }

        public void Dispose()
        {
            Table.Dispose();
            TableB.Dispose();
            DisposeKeepingTables();
        }

        public void DisposeKeepingTables()
        {
            DrawData.Dispose();
            _upload.Dispose();
            _mesh.Dispose();
            Sampler.Dispose();
            SamplerB.Dispose();
            foreach (IGraphicsTextureView view in Views)
            {
                view.Dispose();
            }

            foreach (IGraphicsTexture texture in Textures)
            {
                texture.Dispose();
            }

            _readback.Dispose();
            _view.Dispose();
            _target.Dispose();
            Pipeline.Dispose();
            PipelineB.Dispose();
            _fragment.Dispose();
            _vertex.Dispose();
            Device.Dispose();
        }
    }
}
