using System.Text.Json;
using Lumyte.Graphics.Abstractions;
using Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan;

internal static class VulkanPushConstants
{
    internal const ShaderStageFlags Stages = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit | ShaderStageFlags.ComputeBit;

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

    internal static unsafe void Emit(VulkanCommandBuffer owner, PipelineLayout layout, ShaderRootArguments arguments)
    {
        uint size = ValidateSize(owner.Owner, arguments.WireSize);
        if (arguments.Storage is not VulkanRootStorage storage)
        {
            throw new ArgumentException("Root storage belongs to another backend.", nameof(arguments));
        }

        arguments.Seal();
        if (size == 0)
        {
            return;
        }

        fixed (byte* payload = storage.Data)
        {
            owner.Owner.Api.CmdPushConstants(owner.Native, layout, Stages, 0, size, payload);
        }
    }
}
