using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

/// <summary>An expiring structured pass view whose every mutation validates its declaring stage scope.</summary>
public sealed partial class GraphicsPassScope
{
    private readonly GraphicsDeclarationScope _scope;
    private readonly PlanPass _pass;

    internal GraphicsPassScope(GraphicsDeclarationScope scope, PlanPass pass)
    {
        (_scope, _pass) = (scope, pass);
    }

    /// <summary>Declares read.</summary>
    /// <param name = "resource">The resource.</param>
    /// <param name = "range">The range.</param>
    /// <param name = "access">The access.</param>
    /// <param name = "state">The state.</param>
    public void Read(PlanResource resource, PlanRange range, BarrierScope access, TextureState? state = null)
    {
        _scope.Validate();
        _pass.Read(resource, range, access, state);
    }

    /// <summary>Declares write.</summary>
    /// <param name = "input">The input.</param>
    /// <param name = "range">The range.</param>
    /// <param name = "mode">The mode.</param>
    /// <param name = "access">The access.</param>
    /// <typeparam name = "T">The typed shader or resource contract.</typeparam>
    /// <returns>The declared result.</returns>
    public PlanBuffer<T> Write<T>(PlanBuffer<T> input, PlanRange range, PlanWriteMode mode, BarrierScope access)
        where T : unmanaged
    {
        _scope.Validate();
        return _pass.Write(input, range, mode, access);
    }

    /// <summary>Declares write.</summary>
    /// <param name = "input">The input.</param>
    /// <param name = "range">The range.</param>
    /// <param name = "mode">The mode.</param>
    /// <param name = "access">The access.</param>
    /// <param name = "state">The state.</param>
    /// <returns>The declared result.</returns>
    public PlanTexture Write(PlanTexture input, PlanRange range, PlanWriteMode mode, BarrierScope access, TextureState state)
    {
        _scope.Validate();
        return _pass.Write(input, range, mode, access, state);
    }

    /// <summary>Declares depends on.</summary>
    /// <param name = "predecessor">The predecessor.</param>
    public void DependsOn(GraphicsPassScope predecessor)
    {
        _scope.Validate();
        predecessor._scope.Validate();
        _pass.DependsOn(predecessor._pass);
    }

    /// <summary>Declares keep alive.</summary>
    public void KeepAlive()
    {
        _scope.Validate();
        _pass.KeepAlive();
    }

    /// <summary>Declares buffer.</summary>
    /// <param name = "buffer">The buffer.</param>
    /// <param name = "range">The range.</param>
    /// <param name = "access">The access.</param>
    /// <typeparam name = "T">The typed shader or resource contract.</typeparam>
    /// <returns>The declared result.</returns>
    public PlanGpuRef<T> Buffer<T>(PlanBuffer<T> buffer, PlanRange range, ArgumentBufferAccess access = ArgumentBufferAccess.Read)
        where T : unmanaged
    {
        _scope.Validate();
        return _pass.Buffer(buffer, range, access);
    }

    /// <summary>Declares sample.</summary>
    /// <param name = "texture">The texture.</param>
    /// <returns>The declared result.</returns>
    public PlanGpuRef<IGraphicsTextureView> Sample(PlanTexture texture)
    {
        _scope.Validate();
        return _pass.Sample(texture);
    }

    /// <summary>Declares copy.</summary>
    /// <param name = "source">The source.</param>
    /// <param name = "sourceRange">The source range.</param>
    /// <param name = "destination">The destination.</param>
    /// <param name = "destinationRange">The destination range.</param>
    /// <typeparam name = "T">The typed shader or resource contract.</typeparam>
    public void Copy<T>(PlanBuffer<T> source, PlanRange sourceRange, PlanBuffer<T> destination, PlanRange destinationRange)
        where T : unmanaged
    {
        _scope.Validate();
        _pass.Copy(source, sourceRange, destination, destinationRange);
    }

    /// <summary>Declares dispatch.</summary>
    /// <param name = "kernel">The kernel.</param>
    /// <param name = "arguments">The arguments.</param>
    /// <param name = "x">The x.</param>
    /// <param name = "y">The y.</param>
    /// <param name = "z">The z.</param>
    /// <typeparam name = "TArguments">The typed shader or resource contract.</typeparam>
    public void Dispatch<TArguments>(ComputeKernel<TArguments> kernel, in TArguments arguments, uint x, uint y = 1, uint z = 1)
        where TArguments : struct, IShaderArguments
    {
        _scope.Validate();
        _pass.Dispatch(kernel, arguments, x, y, z);
    }

    /// <summary>Declares draw.</summary>
    /// <param name = "kernel">The kernel.</param>
    /// <param name = "arguments">The arguments.</param>
    /// <param name = "target">The target.</param>
    /// <param name = "raster">The raster.</param>
    /// <param name = "vertexCount">The vertex count.</param>
    /// <param name = "instanceCount">The instance count.</param>
    /// <param name = "load">The load.</param>
    /// <param name = "clear">The clear.</param>
    /// <typeparam name = "TArguments">The typed shader or resource contract.</typeparam>
    public void Draw<TArguments>(RasterKernel<TArguments> kernel, in TArguments arguments, PlanTexture target, GraphicsRasterState raster, uint vertexCount, uint instanceCount = 1, AttachmentLoadOp load = AttachmentLoadOp.Clear, ClearColor clear = default)
        where TArguments : struct, IShaderArguments
    {
        _scope.Validate();
        _pass.Draw(kernel, arguments, target, raster, vertexCount, instanceCount, load, clear);
    }

    /// <summary>Declares clear.</summary>
    /// <param name = "target">The target.</param>
    /// <param name = "color">The color.</param>
    public void Clear(PlanTexture target, ClearColor color)
    {
        _scope.Validate();
        _pass.Clear(target, color);
    }

    /// <summary>Declares copy texture.</summary>
    /// <param name = "source">The source.</param>
    /// <param name = "sourceRegion">The source region.</param>
    /// <param name = "destination">The destination.</param>
    /// <param name = "destinationRegion">The destination region.</param>
    public void CopyTexture(PlanTexture source, PlanTextureRegion sourceRegion, PlanTexture destination, PlanTextureRegion destinationRegion)
    {
        _scope.Validate();
        _pass.CopyTexture(source, sourceRegion, destination, destinationRegion);
    }

    /// <summary>Declares copy buffer to texture.</summary>
    /// <param name = "source">The source.</param>
    /// <param name = "range">The range.</param>
    /// <param name = "bytesPerRow">The bytes per row.</param>
    /// <param name = "rowsPerImage">The rows per image.</param>
    /// <param name = "destination">The destination.</param>
    /// <param name = "region">The region.</param>
    public void CopyBufferToTexture(PlanBuffer<byte> source, PlanRange range, uint bytesPerRow, uint rowsPerImage, PlanTexture destination, PlanTextureRegion region)
    {
        _scope.Validate();
        _pass.CopyBufferToTexture(source, range, bytesPerRow, rowsPerImage, destination, region);
    }

    /// <summary>Declares copy texture to buffer.</summary>
    /// <param name = "source">The source.</param>
    /// <param name = "region">The region.</param>
    /// <param name = "destination">The destination.</param>
    /// <param name = "range">The range.</param>
    /// <param name = "bytesPerRow">The bytes per row.</param>
    /// <param name = "rowsPerImage">The rows per image.</param>
    public void CopyTextureToBuffer(PlanTexture source, PlanTextureRegion region, PlanBuffer<byte> destination, PlanRange range, uint bytesPerRow, uint rowsPerImage)
    {
        _scope.Validate();
        _pass.CopyTextureToBuffer(source, region, destination, range, bytesPerRow, rowsPerImage);
    }

    /// <summary>Declares sampler.</summary>
    /// <param name = "sampler">The sampler.</param>
    /// <returns>The declared result.</returns>
    public PlanGpuRef<IGraphicsSampler> Sampler(PlanSampler sampler)
    {
        _scope.Validate();
        return _pass.Sampler(sampler);
    }

    /// <summary>Declares an explicit typed shader-data copy successor.</summary>
    /// <typeparam name="T">The original shader element ABI.</typeparam>
    /// <param name="input">The consumed immutable version.</param>
    /// <param name="range">The written elements.</param>
    /// <param name="mode">Discard or preserve semantics.</param>
    /// <param name="access">The explicit copy-write scope.</param>
    /// <returns>The successor version.</returns>
    public PlanShaderData<T> Write<T>(PlanShaderData<T> input, PlanRange range, PlanWriteMode mode, BarrierScope access)
        where T : struct, IShaderArguments
    {
        _scope.Validate();
        return _pass.Write(input, range, mode, access);
    }

    /// <summary>Captures finite shader-data elements and reserves their explicit preparation/upload task.</summary>
    /// <typeparam name="T">The original shader element ABI.</typeparam>
    /// <param name="destination">The predeclared written version.</param>
    /// <param name="range">The exact written elements.</param>
    /// <param name="values">The immediately captured values.</param>
    public void UploadShaderData<T>(PlanShaderData<T> destination, PlanRange range, ReadOnlySpan<T> values)
        where T : struct, IShaderArguments
    {
        _scope.Validate();
        _scope.ReserveTransferResource();
        _pass.UploadShaderData(destination, range, values);
    }

    /// <summary>Declares an explicit coherent shader-data GPU copy.</summary>
    /// <typeparam name="T">The shader element ABI.</typeparam>
    /// <param name="source">The source version.</param>
    /// <param name="sourceRange">The source elements.</param>
    /// <param name="destination">The destination version.</param>
    /// <param name="destinationRange">The destination elements.</param>
    public void CopyShaderData<T>(PlanShaderData<T> source, PlanRange sourceRange, PlanShaderData<T> destination, PlanRange destinationRange)
        where T : struct, IShaderArguments
    {
        _scope.Validate();
        _pass.CopyShaderData(source, sourceRange, destination, destinationRange);
    }

    /// <summary>Binds a known typed shader-data closure to this operation's coherent table domain.</summary>
    /// <typeparam name="T">The shader element ABI.</typeparam>
    /// <param name="data">The device storage version.</param>
    /// <param name="range">The finite reachable elements.</param>
    /// <returns>The typed symbolic shader reference.</returns>
    public PlanGpuRef<T> ShaderData<T>(PlanShaderData<T> data, PlanRange range)
        where T : struct, IShaderArguments
    {
        _scope.Validate();
        return _pass.ShaderData(data, range);
    }
}
