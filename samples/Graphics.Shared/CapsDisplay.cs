using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Samples;

internal static class CapsDisplay
{
    internal static string Describe(IGraphicDevice device)
    {
        DeviceCaps caps = device.Caps;
        if (!ReferenceEquals(caps, device.Caps))
        {
            throw new InvalidOperationException("Caps must return the same snapshot.");
        }

        if (caps.MaxBufferSize == 0 || caps.MaxTextureDimension2D == 0 || caps.MaxComputeInvocationsPerWorkgroup == 0)
        {
            throw new InvalidOperationException("A graphics and compute device must report usable limits.");
        }

        return $"Features: {caps.Features}\n" +
            $"MaxBufferSize: {caps.MaxBufferSize} bytes\n" +
            $"MaxStorageBufferBindingSize: {caps.MaxStorageBufferBindingSize} bytes\n" +
            $"MaxTextureDimension2D: {caps.MaxTextureDimension2D} texels\n" +
            $"MaxColorAttachments: {caps.MaxColorAttachments}\n" +
            $"MaxSampledTexturesPerStage: {caps.MaxSampledTexturesPerStage}\n" +
            $"MaxSamplersPerStage: {caps.MaxSamplersPerStage}\n" +
            $"MaxUniformBuffersPerStage: {caps.MaxUniformBuffersPerStage}\n" +
            $"MaxStorageBuffersPerStage: {caps.MaxStorageBuffersPerStage}\n" +
            $"MaxComputeInvocationsPerWorkgroup: {caps.MaxComputeInvocationsPerWorkgroup}\n" +
            $"CopyBufferOffsetAlignment: {caps.CopyBufferOffsetAlignment} bytes\n" +
            $"CopyBufferSizeAlignment: {caps.CopyBufferSizeAlignment} bytes\n" +
            $"CopyBytesPerRowAlignment: {caps.CopyBytesPerRowAlignment} bytes\n" +
            $"StorageBufferOffsetAlignment: {caps.StorageBufferOffsetAlignment} bytes";
    }
}
