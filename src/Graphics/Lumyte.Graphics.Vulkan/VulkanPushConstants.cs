using System.Text.Json;
using Lumyte.Graphics.Abstractions;
using Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan;

internal static class VulkanPushConstants
{
    // One fixed range per bind point; updates include every stage of its overlapping range.
    internal const ShaderStageFlags GraphicsStages = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit;
    internal const ShaderStageFlags ComputeStages = ShaderStageFlags.ComputeBit;

    // Descriptor visibility is independent of the bind-point push-constant range.
    internal const ShaderStageFlags Stages = GraphicsStages | ComputeStages;

    internal static uint ValidateSize(VulkanDevice owner, int size)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(size);
        ulong rounded = ((ulong)size + 3) & ~3UL;
        if (rounded > owner.MaxPushConstantsSize)
        {
            throw new NotSupportedException("Root arguments exceed the device push-constant size limit.");
        }

        return (uint)rounded;
    }

    internal static int ValidateStackSize(VulkanDevice owner, int size)
    {
        uint rounded = ValidateSize(owner, size);

        // Reject implausibly large payloads explicitly rather than risking a stack overflow.
        // The physical limit remains unchanged and is still advertised and used by layouts.
        if (rounded > 64 * 1024)
        {
            throw new NotSupportedException("Root arguments above 64 KiB cannot be recorded with bounded stack storage.");
        }

        return checked((int)rounded);
    }

    internal static void ValidateStorage(VulkanDevice owner, ShaderTargetData target)
    {
        using var document = JsonDocument.Parse(target.ReflectionJson);
        JsonElement root = document.RootElement;
        if (!root.TryGetProperty("parameters", out JsonElement parameters))
        {
            return;
        }

        bool hasStorage = root.TryGetProperty("lumyteRootStorage", out JsonElement storage);
        foreach (JsonElement parameter in parameters.EnumerateArray())
        {
            string name = parameter.GetProperty("name").GetString()!;
            if (parameter.GetProperty("type").GetProperty("kind").GetString() != "constantBuffer" ||
                name == "lumyteDrawInfo" || name.StartsWith("__lumyte_schema_", StringComparison.Ordinal))
            {
                continue;
            }

            if (hasStorage ? storage.GetString() != "VkPushConstants" :
                !parameter.TryGetProperty("binding", out JsonElement binding) ||
                !binding.TryGetProperty("kind", out JsonElement kind) || kind.GetString() != "pushConstantBuffer")
            {
                throw new NotSupportedException("Vulkan root artifacts require push-constant storage; recompile this shader.");
            }

            // Reflection restores the logical schema, not the padded uint4 wire declaration.
            ValidateSize(owner, ShaderDataLayout.Root(target, name).Size);
        }
    }

    internal static unsafe void Emit(VulkanCommandBuffer owner, PipelineLayout layout, ShaderStageFlags stages, ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty)
        {
            return;
        }

        fixed (byte* data = payload)
        {
            owner.Owner.Api.CmdPushConstants(owner.Native, layout, stages, 0, checked((uint)payload.Length), data);
        }
    }
}
