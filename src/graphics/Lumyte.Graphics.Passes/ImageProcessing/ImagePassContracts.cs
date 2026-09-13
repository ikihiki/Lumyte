using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.Passes;

public sealed class ClearPassContract : IGpuRenderPassContract<ClearPassRequest, TexturePassResult>
{
    public static ClearPassContract Instance { get; } = new();
    private ClearPassContract() { }
    public string Id => "lumyte.image.clear";
    public int Version => 1;
    public ClearPassRequest Snapshot(ClearPassRequest request) => request ?? throw new ArgumentNullException(nameof(request));

    public TexturePassResult Declare(GpuPassDeclarationContext context, ClearPassRequest request)
    {
        context.Write(request.Target);
        context.ReadInput(request.Value, ClearValueInputContract.Instance);
        return new(request.Target);
    }
}

public sealed class TextureCopyPassContract : IGpuRenderPassContract<TextureCopyPassRequest, TexturePassResult>
{
    public static TextureCopyPassContract Instance { get; } = new();
    private TextureCopyPassContract() { }
    public string Id => "lumyte.image.copy";
    public int Version => 1;
    public TextureCopyPassRequest Snapshot(TextureCopyPassRequest request) => request ?? throw new ArgumentNullException(nameof(request));

    public TexturePassResult Declare(GpuPassDeclarationContext context, TextureCopyPassRequest request)
    {
        if (request.Source == request.Target)
        {
            throw new ArgumentException("Texture copy requires distinct source and target resources.", nameof(request));
        }
        if (request.Source.Description != request.Target.Description)
        {
            throw new ArgumentException("Texture copy requires matching logical descriptions.", nameof(request));
        }
        if (request.Source.Description.SampleCount != 1)
        {
            throw new NotSupportedException("Texture copy version 1 supports single-sample images.");
        }
        context.Read(request.Source);
        context.Write(request.Target);
        return new(request.Target);
    }
}

public sealed class OutputPassContract : IGpuRenderPassContract<OutputPassRequest, TexturePassResult>
{
    public static OutputPassContract Instance { get; } = new();
    private OutputPassContract() { }
    public string Id => "lumyte.image.output";
    public int Version => 1;
    public OutputPassRequest Snapshot(OutputPassRequest request) => request ?? throw new ArgumentNullException(nameof(request));

    public TexturePassResult Declare(GpuPassDeclarationContext context, OutputPassRequest request)
    {
        if (request.Source == request.Target)
        {
            throw new ArgumentException("Output requires distinct source and target resources.", nameof(request));
        }
        RequireOutputShape(request.Source.Description);
        RequireOutputShape(request.Target.Description);
        if (request.Source.Description.Width != request.Target.Description.Width
            || request.Source.Description.Height != request.Target.Description.Height)
        {
            throw new ArgumentException("Output requires matching source and target extents.", nameof(request));
        }
        context.Read(request.Source);
        context.Write(request.Target);
        return new(request.Target);
    }

    private static void RequireOutputShape(GpuGraphTextureDescription description)
    {
        if (description.Dimension != GpuGraphTextureDimension.TwoD || description.DepthOrArrayLayers != 1
            || description.MipLevelCount != 1 || description.SampleCount != 1)
        {
            throw new NotSupportedException("Output version 1 supports 2D images with one mip, layer, and sample.");
        }
    }
}
