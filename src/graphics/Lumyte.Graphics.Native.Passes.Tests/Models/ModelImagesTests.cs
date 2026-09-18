using System.Numerics;
using Lumyte.Graphics.ModelPreparation;
using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.Native.Passes.Tests;

public sealed class ModelImagesTests
{
    [Theory]
    [InlineData(GpuFormat.R8Unorm, 1, 0, 0)]
    [InlineData(GpuFormat.Rg8Unorm, 1, .5f, 0)]
    [InlineData(GpuFormat.Bgra8Unorm, .25f, .5f, 1)]
    public void ChannelsFollowTheUploadFormat(GpuFormat format,float red,float green,float blue)
    {
        var image=new GpuImageUploadData(new("image",0),new(1,1,format),GpuImageColorEncoding.Linear,GpuImageAlphaMode.Opaque,
            [new(0,0,4,4,new byte[] {255,128,64,255})]);

        var prepared=ModelImages.Prepare(new(image,true));

        Assert.True(Vector4.Distance(new(red,green,blue,1),Pixel(prepared.Levels[0]))<.003f);
    }

    [Fact]
    public void MissingLevelsAreReservedForGpuGeneration()
    {
        var image=new GpuImageUploadData(new("image",0),new(4,2,GpuFormat.Rgba8Unorm),GpuImageColorEncoding.Linear,GpuImageAlphaMode.Opaque,
            [new(0,0,16,32,new byte[32])]);

        var prepared=ModelImages.Prepare(new(image,true));

        Assert.Collection(prepared.Levels,
            level => Assert.NotEmpty(level.Bytes),
            level => Assert.Equal((2u,1u,0),(level.Width,level.Height,level.Bytes.Length)),
            level => Assert.Equal((1u,1u,0),(level.Width,level.Height,level.Bytes.Length)));
    }

    [Fact]
    public void HalfFloatColorRetainsHdrValues()
    {
        byte[] bytes=new byte[8];
        BitConverter.TryWriteBytes(bytes.AsSpan(),(Half)4);
        BitConverter.TryWriteBytes(bytes.AsSpan(6),(Half)1);
        var image=new GpuImageUploadData(new("image",0),new(1,1,GpuFormat.Rgba16Float),GpuImageColorEncoding.Linear,GpuImageAlphaMode.Straight,[new(0,0,8,8,bytes)]);

        var prepared=ModelImages.Prepare(new(image,true));

        Assert.Equal(new Vector4(4,0,0,1),Pixel(prepared.Levels[0]));
    }

    private static Vector4 Pixel(ModelImageLevel level) => new((float)BitConverter.ToHalf(level.Bytes),
        (float)BitConverter.ToHalf(level.Bytes.AsSpan(2)),(float)BitConverter.ToHalf(level.Bytes.AsSpan(4)),(float)BitConverter.ToHalf(level.Bytes.AsSpan(6)));
}
