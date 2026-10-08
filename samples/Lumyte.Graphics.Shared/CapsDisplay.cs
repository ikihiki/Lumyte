using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Samples;

/// <summary>Displays device capabilities using only the common graphics API.</summary>
public static class CapsDisplay
{
    /// <summary>Checks the capability snapshot and formats its values.</summary>
    /// <param name="device">The device to inspect.</param>
    /// <returns>The formatted capability values.</returns>
    public static string Describe(IGraphicDevice device)
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
