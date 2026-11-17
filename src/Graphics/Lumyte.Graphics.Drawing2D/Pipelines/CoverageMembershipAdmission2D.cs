using System.Numerics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D.Pipelines;

internal static class CoverageMembershipAdmission2D
{
    internal static bool CanPrepare<T>(RenderPreparationContext2D context, ReadOnlyMemory<T> commands)
        where T : DrawingCommand2D
    {
        if (!context.SupportsCanonicalRecords)
        {
            return false;
        }

        return Validate(context, commands);
    }

    private static bool Validate<T>(RenderPreparationContext2D context, ReadOnlyMemory<T> commands)
        where T : DrawingCommand2D
    {
        DeviceCaps caps = context.Device.Caps;
        bool pathSlot = typeof(T) == typeof(FillPathCommand2D) || typeof(T) == typeof(StrokePathCommand2D) || typeof(T) == typeof(DrawLineCommand2D);
        if (pathSlot && (caps.MaxComputeInvocationsPerWorkgroup < 256 || caps.MaxComputeWorkgroupSizeX < 16 || caps.MaxComputeWorkgroupSizeY < 16 || caps.MaxComputeWorkgroupSizeZ < 1))
        {
            return false;
        }

        if (context.Device.GetBufferLayout<Vector4>().ElementStrideInBytes != 16)
        {
            return false;
        }

        ArgumentTableBindingLimits limits = caps.ArgumentTableBindingLimits;
        ulong maximum = Math.Min(Math.Min(caps.MaxBufferSize, caps.MaxStorageBufferBindingSize == 0 ? caps.MaxBufferSize : caps.MaxStorageBufferBindingSize), limits.MaximumBufferRangeBytes);
        maximum = Math.Min(maximum, int.MaxValue);
        if (!DrawingWorkingBudget2D.TryValidate(commands, context.Target, context.MaxWorkingBytes - context.OtherWorkingBytes, out long working))
        {
            return false;
        }

        using var registrations = new ArgumentTablePlan(context.Device);
        foreach (T command in commands.Span)
        {
            NormalizedClip2D clip = context.Clips.Get(command.State.Clip, Matrix3x2.Identity, command.State.Clip.Elements);
            if (clip.Kind == NormalizedClipKind2D.Empty)
            {
                continue;
            }

            var page = new DrawRecordPage2D();
            ShaderResources scope = registrations.CreateShaderResources();
            var program = new DrawingProgram2D(scope, context.Target, context.Clips, recordLowering: true);
            uint root = program.Compile(command);
            program.ValidateFiniteData();
            DrawingInstruction terminal = program.Instructions[(int)root];
            if (terminal.Kind is 16 or 17)
            {
                terminal = program.Instructions[(int)terminal.First];
            }

            if (terminal.Kind != 11)
            {
                continue;
            }

            ScissorRect bounds = DrawingBounds2D.GetScissor(command, null, context.Target, null, false, false, program.Clip?.Bounds);
            if (bounds.Width == 0 || bounds.Height == 0)
            {
                continue;
            }

            DrawingInstruction shape = program.Instructions[(int)terminal.First];
            bool membership = shape.Kind == 4 || (shape.Kind == 5 && shape.Mode != 1);
            if (!page.FitsAddition(program, root, clip, default, maximum, out uint buffers, out bool needsSampler, combineDirectories: membership))
            {
                return false;
            }

            if (membership)
            {
                if (((ulong)bounds.Width + 15) / 16 > caps.MaxComputeWorkgroupsPerDimension || ((ulong)bounds.Height + 15) / 16 > caps.MaxComputeWorkgroupsPerDimension)
                {
                    return false;
                }

                ulong maskBytes = checked((ulong)bounds.Width * bounds.Height * 4);
                if (maskBytes > maximum)
                {
                    return false;
                }

                buffers = Math.Max(buffers, 7u);
            }

            uint textures = checked((uint)program.Views.Count);
            uint samplers = checked((uint)program.Samplers.Count + (needsSampler || membership ? 1u : 0u));
            uint textureBindings = Math.Max(textures, limits.MinimumTextureBindings);
            uint samplerBindings = Math.Max(samplers, limits.MinimumSamplerBindings);
            ulong combined = limits.ReservedBindings + (ulong)limits.ColorAttachmentBindingWeight + ((ulong)textureBindings * limits.TextureBindingWeight) + ((ulong)samplerBindings * limits.SamplerBindingWeight) + ((ulong)buffers * limits.BufferBindingWeight);
            if (textures > limits.TextureSlots || textureBindings > limits.TexturesPerDraw || samplers > limits.SamplerSlots || samplerBindings > limits.SamplersPerDraw || buffers > Math.Min(limits.BufferSlots, limits.BuffersPerDraw) || combined > limits.CombinedBindings)
            {
                return false;
            }

            working = checked(working + 32768 + ((long)program.Instructions.Count * 8192) + ((long)Math.Max(1, program.Data.Count) * 96));
            if (working > context.MaxWorkingBytes - context.OtherWorkingBytes)
            {
                return false;
            }
        }

        return true;
    }
}
