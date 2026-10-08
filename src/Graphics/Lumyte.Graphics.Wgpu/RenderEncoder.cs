using Ahjo.Wgpu.Native;

namespace Lumyte.Graphics.Wgpu;

internal sealed unsafe class RenderEncoder : IDisposable
{
    private readonly CommandEncoder _parent;
    private readonly uint _width;
    private readonly uint _height;
    private readonly Texture _target;
    private WGPURenderPassEncoderImpl* _handle;
    private GraphicsPipeline? _pipeline;
    private DrawingArguments? _drawingArguments;
    private BufferSlice _indices;
    private IndexFormat _indexFormat;

    internal RenderEncoder(CommandEncoder parent, WGPURenderPassEncoderImpl* handle, Texture target)
    {
        _parent = parent;
        _handle = handle;
        _target = target;
        _width = target.Width;
        _height = target.Height;
        WGPU.wgpuRenderPassEncoderSetViewport(handle, 0, 0, _width, _height, 0, 1);
        WGPU.wgpuRenderPassEncoderSetScissorRect(handle, 0, 0, _width, _height);
    }

    public void SetPipeline(GraphicsPipeline pipeline)
    {
        lock (_parent.Owner.Gate)
        {
            Active();
            pipeline.Check(_parent.Owner);
            _parent.Use(pipeline);
            WGPU.wgpuRenderPassEncoderSetPipeline(_handle, pipeline.Native.Handle);
            _pipeline = pipeline;
            _drawingArguments = null;
        }
    }

    public void SetViewport(Viewport v)
    {
        lock (_parent.Owner.Gate)
        {
            Active();
            if (!float.IsFinite(v.X) || !float.IsFinite(v.Y) || !float.IsFinite(v.Width) || !float.IsFinite(v.Height) || !float.IsFinite(v.MinDepth) || !float.IsFinite(v.MaxDepth) || v.X < 0 || v.Y < 0 || v.Width <= 0 || v.Height <= 0 || v.X + (double)v.Width > _width || v.Y + (double)v.Height > _height || v.MinDepth < 0 || v.MinDepth > v.MaxDepth || v.MaxDepth > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(v));
            }

            WGPU.wgpuRenderPassEncoderSetViewport(_handle, v.X, v.Y, v.Width, v.Height, v.MinDepth, v.MaxDepth);
        }
    }

    public void SetScissor(Scissor s)
    {
        lock (_parent.Owner.Gate)
        {
            Active();
            if ((ulong)s.X + s.Width > _width || (ulong)s.Y + s.Height > _height)
            {
                throw new ArgumentOutOfRangeException(nameof(s));
            }

            WGPU.wgpuRenderPassEncoderSetScissorRect(_handle, s.X, s.Y, s.Width, s.Height);
        }
    }

    public void SetIndexBuffer(BufferSlice indices, IndexFormat format)
    {
        lock (_parent.Owner.Gate)
        {
            Active();
            if (indices.Buffer is null)
            {
                throw new ArgumentException("Invalid index range.");
            }

            indices.Buffer.Check(_parent.Owner);
            if (!Enum.IsDefined(format))
            {
                throw new ArgumentException("Unknown index format.");
            }

            uint size = format == IndexFormat.Uint16 ? 2u : 4u;
            if (!indices.Buffer.Usage.HasFlag(BufferUsage.Index) || indices.Offset % size != 0 || indices.Length % size != 0)
            {
                throw new ArgumentException("Index range or usage is invalid.");
            }

            _parent.Use(indices.Buffer);
            WGPU.wgpuRenderPassEncoderSetIndexBuffer(_handle, indices.Buffer.Native.Handle, format == IndexFormat.Uint16 ? WGPUIndexFormat.Uint16 : WGPUIndexFormat.Uint32, indices.Offset, indices.Length);
            _indices = indices;
            _indexFormat = format;
        }
    }

    public void Draw(DrawingArguments arguments, DrawDesc desc)
    {
        lock (_parent.Owner.Gate)
        {
            Active();
            ArgumentNullException.ThrowIfNull(desc);
            arguments.Check(_parent.Owner);
            arguments.Region.Check(_parent.Owner);
            if (!ReferenceEquals(arguments.Pipeline, _pipeline))
            {
                throw new ArgumentException("Set the matching graphics pipeline before drawing.");
            }

            if (arguments.Dependencies.Any(entry => entry.Resource is TextureView view && ReferenceEquals(view.Texture, _target)))
            {
                throw new ArgumentException("Cannot sample the active render target.");
            }

            checked
            {
                _ = desc.FirstVertex + desc.VertexCount;
                _ = desc.FirstInstance + desc.InstanceCount;
            }

            _parent.Use(arguments);
            _parent.Use(arguments.Region.Buffer);
            WGPU.wgpuRenderPassEncoderSetBindGroup(_handle, 0, arguments.Handle, 0, null);
            _drawingArguments = arguments;
            Draw(desc);
        }
    }

    public void Draw(uint vertexCount, uint instanceCount = 1) => Draw(new DrawDesc { VertexCount = vertexCount, InstanceCount = instanceCount });

    public void Draw(DrawDesc desc)
    {
        lock (_parent.Owner.Gate)
        {
            Active();
            ArgumentNullException.ThrowIfNull(desc);
            if (_pipeline is null)
            {
                throw new InvalidOperationException("Set a graphics pipeline before drawing.");
            }

            CheckDrawingArguments();
            checked
            {
                _ = desc.FirstVertex + desc.VertexCount;
                _ = desc.FirstInstance + desc.InstanceCount;
            }

            WGPU.wgpuRenderPassEncoderDraw(_handle, desc.VertexCount, desc.InstanceCount, desc.FirstVertex, desc.FirstInstance);
        }
    }

    public void DrawIndexed(IndexedDrawDesc desc)
    {
        lock (_parent.Owner.Gate)
        {
            Active();
            ArgumentNullException.ThrowIfNull(desc);
            if (_pipeline is null || _indices.Buffer is null)
            {
                throw new InvalidOperationException("Set a pipeline and index buffer before drawing.");
            }

            CheckDrawingArguments();
            uint size = _indexFormat == IndexFormat.Uint16 ? 2u : 4u;
            if (((ulong)desc.FirstIndex + desc.IndexCount) * size > _indices.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(desc));
            }

            checked
            {
                _ = desc.FirstInstance + desc.InstanceCount;
            }

            WGPU.wgpuRenderPassEncoderDrawIndexed(_handle, desc.IndexCount, desc.InstanceCount, desc.FirstIndex, desc.BaseVertex, desc.FirstInstance);
        }
    }

    public void End()
    {
        lock (_parent.Owner.Gate)
        {
            Active();
            Abort();
            _parent.EndPass(this);
        }
    }

    public void Dispose()
    {
        lock (_parent.Owner.Gate)
        {
            if (_handle != null)
            {
                End();
            }
        }
    }

    internal void Abort()
    {
        if (_handle == null)
        {
            return;
        }

        WGPU.wgpuRenderPassEncoderEnd(_handle);
        WGPU.wgpuRenderPassEncoderRelease(_handle);
        _handle = null;
    }

    private void Active()
    {
        _parent.Active();
        if (_handle == null)
        {
            throw new InvalidOperationException("Render pass has ended.");
        }
    }

    private void CheckDrawingArguments()
    {
        if (_pipeline?.ShaderDataSchema is not null)
        {
            if (_drawingArguments is null)
            {
                throw new InvalidOperationException("Shader-data pipeline requires graphics arguments.");
            }

            _drawingArguments.Region.Check(_parent.Owner);
        }
    }
}
