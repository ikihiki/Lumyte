using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Browser;

/// <summary>Reports execution accesses only when the owning device has opted into diagnostics.</summary>
internal static class EncoderValidation
{
    internal static PipelineStage RenderStages(ShaderTargetData vertex, ShaderTargetData? fragment, string root)
    {
        PipelineStage stages = ShaderDataLayout.HasRoot(vertex, root) ? PipelineStage.VertexShader : PipelineStage.None;
        if (fragment != null && ShaderDataLayout.HasRoot(fragment, root))
        {
            stages |= PipelineStage.FragmentShader;
        }

        return stages;
    }

    internal static void ShaderAccesses(GraphicsValidation? validation, object command, IReadOnlyCollection<IShaderReference> references, PipelineStage stages)
    {
        if (validation == null)
        {
            return;
        }

        var buffers = new List<(object Storage, ulong Offset, ulong End, ResourceAccess Access)>();
        foreach (IShaderReference reference in references)
        {
            validation.Reference(command, (IValidatedShaderReference)reference);
            validation.Use(command, reference.Table);
            validation.Use(command, reference.Resource);
            if (reference.Resource is IGraphicsTextureView view)
            {
                TextureViewInfo info = view.Info;
                validation.TextureAccess(command, view.Texture, new(info.BaseMipLevel, info.MipLevelCount, info.BaseArrayLayer, info.ArrayLayerCount), TextureState.Sampled, new(stages, ResourceAccess.ShaderRead));
            }
            else if (reference.Resource is not IGraphicsSampler)
            {
                ResourceAccess access = reference.BufferAccess == ShaderBufferAccess.ReadWrite ? ResourceAccess.ShaderRead | ResourceAccess.ShaderWrite : ResourceAccess.ShaderRead;
                object storage = reference.Resource is IValidatedShaderDataStorage data ? data.ValidationStorage : reference.Resource;
                ulong offset = reference.OffsetInBytes;
                ulong end = checked(offset + reference.SizeInBytes);
                for (int index = 0; index < buffers.Count; index++)
                {
                    (object Storage, ulong Offset, ulong End, ResourceAccess Access) previous = buffers[index];
                    if (ReferenceEquals(previous.Storage, storage) && previous.Offset < end && offset < previous.End)
                    {
                        offset = Math.Min(offset, previous.Offset);
                        end = Math.Max(end, previous.End);
                        access |= previous.Access;
                        buffers.RemoveAt(index);
                        index = -1;
                    }
                }

                buffers.Add((storage, offset, end, access));
            }
        }

        foreach ((object storage, ulong offset, ulong end, ResourceAccess access) in buffers)
        {
            validation.Access(command, storage, new(stages, access), offset, checked(end - offset));
        }
    }
}
