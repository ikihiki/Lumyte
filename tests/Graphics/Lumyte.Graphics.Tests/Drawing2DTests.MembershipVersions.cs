using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Validation;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Lumyte.Mathematics.Colors;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Three real submitted versions own private masks and survive CPU source/root disposal.</summary>
public sealed partial class Drawing2DTests
{
    /// <summary>Checks actual simultaneously submitted version owners on Vulkan.</summary>
    /// <returns>The completed native proof.</returns>
    [GpuFact]
    public async Task VulkanMembershipThreeSubmittedVersionsAsync()
    {
        using var raw = VulkanDevice.Create();
        await CheckValidatedMembershipVersionsAsync(raw);
    }

    /// <summary>Checks actual simultaneously submitted version owners on wgpu.</summary>
    /// <returns>The completed native proof.</returns>
    [GpuFact]
    public async Task WgpuMembershipThreeSubmittedVersionsAsync()
    {
        using var raw = WgpuDevice.Create();
        await CheckValidatedMembershipVersionsAsync(raw);
    }

    /// <summary>Checks all three real submitted multipage versions on Vulkan.</summary>
    /// <returns>The completed native lifetime proof.</returns>
    [GpuFact]
    public async Task VulkanMembershipMultipageThreeVersionsAsync()
    {
        using var raw = VulkanDevice.Create();
        await CheckValidatedMembershipVersionsAsync(raw, multipage: true);
    }

    /// <summary>Checks all three real submitted multipage versions on wgpu.</summary>
    /// <returns>The completed native lifetime proof.</returns>
    [GpuFact]
    public async Task WgpuMembershipMultipageThreeVersionsAsync()
    {
        using var raw = WgpuDevice.Create();
        await CheckValidatedMembershipVersionsAsync(raw, multipage: true);
    }

    private static async Task CheckValidatedMembershipVersionsAsync(IGraphicDevice raw, bool multipage = false)
    {
        var device = new ValidationDevice(raw);
        try
        {
            await CheckMembershipThreeSubmittedVersionsAsync(multipage ? BoundedMembershipDevice(device) : device, multipage);
        }
        catch (Exception original)
        {
            try
            {
                device.Dispose();
            }
            catch (Exception cleanup)
            {
                throw new AggregateException(original, cleanup);
            }

            throw;
        }

        device.Dispose();
    }

    private static async Task CheckMembershipThreeSubmittedVersionsAsync(IGraphicDevice device, bool multipage)
    {
        using IRenderer2D renderer = CreateMembershipOracleRenderer(device);
        using var scene = new DrawingScene2D();
        DrawingNodeId2D node = scene.Insert(scene.Root, 0, VersionContent(false, multipage));
        using DrawingSnapshot2D old = scene.CaptureSnapshot();
        using MembershipVersionSlot first = await MembershipVersionSlot.PrepareAsync(device, renderer, old, 0, multipage);
        scene.SetTransform(node, Matrix3x2.CreateTranslation(multipage ? 2 : 6, 0));
        using DrawingSnapshot2D moved = scene.CaptureSnapshot();
        using MembershipVersionSlot second = await MembershipVersionSlot.PrepareAsync(device, renderer, moved, 1, multipage);
        scene.SetTransform(node, Matrix3x2.Identity);
        scene.ReplaceContent(node, VersionContent(true, multipage));
        using DrawingSnapshot2D replaced = scene.CaptureSnapshot();
        using MembershipVersionSlot third = await MembershipVersionSlot.PrepareAsync(device, renderer, replaced, 2, multipage);
        Assert.Equal(3, renderer.RetentionStatistics.ActiveFrames);
        old.Dispose();
        moved.Dispose();
        replaced.Dispose();
        scene.Dispose();
        renderer.Dispose();
        first.Submit(device);
        second.Submit(device);
        third.Submit(device);
        Assert.NotSame(first.Output, second.Output);
        Assert.NotSame(second.Output, third.Output);
        Assert.NotSame(first.Output, third.Output);
        object[] outputs = first.Outputs.Concat(second.Outputs).Concat(third.Outputs).ToArray();
        Assert.Equal(multipage ? 6 : 3, outputs.Distinct(ReferenceEqualityComparer.Instance).Count());
        Assert.Equal(3, renderer.RetentionStatistics.ActiveFrames);

        // Every actual Queue.Submit and AttachSubmission has happened before this first wait.
        await third.CompleteAsync();
        await first.CompleteAsync();
        await second.CompleteAsync();
        Assert.Equal(0, renderer.RetentionStatistics.ActiveFrames);
    }

    private static DrawList2D VersionContent(bool green, bool multipage)
    {
        if (multipage)
        {
            var pages = new Canvas2D();
            pages.Begin();
            pages.FillPath(MultipageBox(0), new SRgba<float>(green ? 0 : 1, green ? 1 : 0, 0, 0.5f));
            pages.FillPath(MultipageBox(20), new SRgba<float>(green ? 1 : 0, green ? 0 : 1, 0, 0.5f));
            return pages.End();
        }

        var path = new PathBuilder2D();
        path.MoveTo(new(2, 2));
        path.LineTo(new(6, 2));
        path.LineTo(new(6, 6));
        path.LineTo(new(2, 6));
        path.Close();
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillPath(path.Build(), new SRgba<float>(green ? 0 : 1, green ? 1 : 0, 0, 0.5f));
        return canvas.End();
    }

    private sealed class MembershipVersionSlot : IDisposable
    {
        private readonly IGraphicsTexture _texture;
        private readonly IGraphicsTextureView _view;
        private readonly IGraphicsBuffer<byte> _readback;
        private readonly IGraphicsCommandBuffer _commands;
        private readonly uint _pitch;
        private readonly int _version;
        private readonly bool _multipage;
        private readonly uint _width;
        private readonly uint _height;
        private PreparedDrawingFrame2D _frame = null!;
        private IGraphicsSubmission? _submission;
        private bool _recordAttempted;
        private bool _submitAttempted;
        private bool _retired;
        private PartitionCommands? _observed;
        private IShaderReference[] _computeOutputs = [];

        private MembershipVersionSlot(IGraphicDevice device, int version, bool multipage)
        {
            _version = version;
            _multipage = multipage;
            _width = multipage ? 128u : 16u;
            _height = multipage ? 128u : 8u;
            _texture = device.CreateTexture(new() { Width = _width, Height = _height, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
            _view = _texture.CreateView();
            _pitch = OracleRowPitch(device, _width);
            _readback = device.CreateBuffer<byte>(new() { Count = _pitch * _height, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
            _commands = device.CreateCommandBuffer(new());
        }

        internal object Output { get; private set; } = null!;

        internal IEnumerable<object> Outputs => _computeOutputs.Select(output => output.Resource);

        public void Dispose()
        {
            if (_submission is not null && !_retired)
            {
                _submission.WaitAsync().GetAwaiter().GetResult();
                _commands.Dispose();
                _frame.Retire();
                _retired = true;
            }
            else if (_recordAttempted && !_submitAttempted)
            {
                _commands.Dispose();
                _frame.AbortUnsubmitted();
                _retired = true;
            }

            _submission?.Dispose();
            _commands.Dispose();
            _frame.Dispose();
            _readback.Dispose();
            _view.Dispose();
            _texture.Dispose();
        }

        internal static async Task<MembershipVersionSlot> PrepareAsync(IGraphicDevice device, IRenderer2D renderer, DrawingSnapshot2D snapshot, int version, bool multipage)
        {
            var slot = new MembershipVersionSlot(device, version, multipage);
            slot._frame = await renderer.PrepareAsync(snapshot, new() { View = slot._view, LoadOp = AttachmentLoadOp.Load });
            return slot;
        }

        internal void Submit(IGraphicDevice device)
        {
            var color = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite);
            _commands.Barrier(new TextureBarrierDesc { Texture = _texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.Undefined, AfterState = TextureState.ColorAttachment, Before = default, After = color });
            IRenderEncoder clear = _commands.BeginRenderPass(new() { ColorAttachments = [new() { View = _view, LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store, ClearValue = new(0, 0, 1, 1) }] });
            clear.End();
            _commands.Barrier(new TextureBarrierDesc { Texture = _texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.ColorAttachment, Before = color, After = color });
            var observed = new PartitionCommands(_commands);
            _observed = observed;
            _recordAttempted = true;
            _frame.Record(observed);
            Assert.Equal(_multipage ? 2 : 1, observed.ComputePasses);
            Assert.Equal(_multipage ? 2 : 1, observed.ComputeDispatches);
            Assert.Equal(_multipage ? 2 : 1, observed.ComputeRoots.Count);
            _computeOutputs = observed.ComputeRoots.Select(root => Assert.Single(root.Values.Select(value => Assert.IsAssignableFrom<IShaderReference>(value.Reference)), reference => reference.Resource is IGraphicsBuffer<uint>)).ToArray();
            Assert.All(_computeOutputs, output => Assert.Equal(ShaderBufferAccess.ReadWrite, output.BufferAccess));
            if (_multipage)
            {
                Assert.NotSame(_computeOutputs[0].Resource, _computeOutputs[1].Resource);
                Assert.All(_computeOutputs, output => Assert.InRange(output.SizeInBytes, 40000UL, 65536UL));
            }

            Output = _computeOutputs[0].Resource;
            _commands.Barrier(new TextureBarrierDesc { Texture = _texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.CopySource, Before = color, After = _copyRead });
            _commands.CopyTextureToBuffer(new() { Texture = _texture, Width = _width, Height = _height }, new() { Buffer = _readback.SliceElements(0, _readback.Count), BytesPerRow = _pitch, RowsPerImage = _height });
            _commands.Barrier(new BufferBarrierDesc<byte> { Buffer = _readback.SliceElements(0, _readback.Count), Before = _copyWrite, After = new(PipelineStage.Host, ResourceAccess.HostRead) });
            _commands.Finish();
            _submitAttempted = true;
            _submission = device.Queue.Submit([_commands]);
            _frame.AttachSubmission(_submission);
        }

        internal async Task CompleteAsync()
        {
            await _submission!.WaitAsync();
            VerifyPhysicalReferences();
            Assert.Throws<InvalidOperationException>(_frame.Retire);
            _commands.Dispose();
            _frame.Retire();
            _retired = true;
            Vector4[] pixels = await DecodeMembershipOraclePixelsAsync(_readback, _width, _height, _pitch);
            int left = _version == 1 ? 8 : 2;
            for (int y = 0; y < _height; y++)
            {
                for (int x = 0; x < _width; x++)
                {
                    Vector4 expected = _multipage ? MultipagePixel(x, y, _version == 1 ? 2 : 0, _version == 2, blue: true) : x >= left && x < left + 4 && y >= 2 && y < 6
                        ? _version == 2 ? new(0, 0.5f, 0.5f, 1) : new(0.5f, 0, 0.5f, 1)
                        : new(0, 0, 1, 1);
                    AssertPixel(pixels, (int)_width, x, y, expected);
                }
            }
        }

        private void VerifyPhysicalReferences()
        {
            var raster = new List<IShaderReference>();
            var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
            foreach ((IArgumentTable _, ShaderValueSnapshot snapshot) in _observed!.Roots)
            {
                Visit(snapshot.Values.Select(value => Assert.IsAssignableFrom<IShaderReference>(value.Reference)));
            }

            Assert.NotEmpty(raster);
            Assert.All(_computeOutputs, output => Assert.Contains(raster, reference => ReferenceEquals(reference.Resource, output.Resource)));
            Assert.All(raster, reference =>
            {
                Assert.Equal(ShaderBufferAccess.ReadOnly, reference.BufferAccess);
                Assert.NotSame(_computeOutputs.Single(output => ReferenceEquals(output.Resource, reference.Resource)).Table, reference.Table);
            });

            void Visit(IEnumerable<IShaderReference> references)
            {
                foreach (IShaderReference reference in references)
                {
                    if (_computeOutputs.Any(output => ReferenceEquals(reference.Resource, output.Resource)))
                    {
                        raster.Add(reference);
                    }

                    if (reference.Resource is IShaderDataSource data && visited.Add(reference.Resource) && data.TryGetDependencies(out IShaderReferenceMetadata? child))
                    {
                        Visit(child.References);
                    }
                }
            }
        }
    }
}
