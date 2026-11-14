using System.Reflection;
using System.Reflection.Emit;
using Xunit;

namespace Lumyte.Graphics.Direct3D12.Tests;

/// <summary>Guards selection-time native recording rather than equivalent final pixels.</summary>
public sealed class ImmediateRootCommandTests
{
    /// <summary>Both generic Set overloads record constants before publishing a selection.</summary>
    /// <param name="encoderName">The encoder implementation.</param>
    [Theory]
    [InlineData("Direct3D12ComputeEncoder")]
    [InlineData("Direct3D12RenderEncoder")]
    public void SetRecordsConstantsAndExecutionNeverReplaysPayload(string encoderName)
    {
        Assembly assembly = typeof(Lumyte.Graphics.Direct3D12.Direct3D12Device).Assembly;
        Type encoder = assembly.GetType("Lumyte.Graphics.Direct3D12." + encoderName, true)!;
        MethodInfo[] sets = encoder.GetMethods().Where(method => method.Name == "SetArguments" && method.IsGenericMethodDefinition).ToArray();
        Assert.Equal(2, sets.Length);
        MethodInfo select = encoder.GetMethod("SelectRoot", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.NotNull(select);
        foreach (MethodInfo set in sets)
        {
            Assert.Contains(Instructions(set), item => item.Member is MethodBase called && called.Name == "SelectRoot");
        }

        (OpCode Code, MemberInfo? Member)[] instructions = Instructions(select).ToArray();
        int emit = Array.FindIndex(instructions, item => item.Member is MethodBase called && called.DeclaringType?.Name == "NativeMethods" && called.Name == "RootConstants");
        int ownerValidation = Array.FindIndex(instructions, item => item.Member is MethodBase called && called.Name == "ValidatePass");
        int pipelineValidation = Array.FindIndex(instructions, item => item.Member is MethodBase called && called.Name == "ValidateAlive");
        Assert.True(
            ownerValidation >= 0 && pipelineValidation > ownerValidation && emit > pipelineValidation,
            "After user callbacks, validate owner and captured pipeline before issuing native commands.");
        int publish = Array.FindIndex(instructions, item => item.Code == OpCodes.Stfld && item.Member?.Name == "_arguments");
        Assert.True(emit >= 0, "Set must synchronously record native constants, not defer them to execution.");
        Assert.True(publish > emit, "Only publish the selection after native recording succeeds.");
        foreach (MethodInfo method in encoder.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Where(method => method.Name != "SelectRoot" && method.Name != "SetArguments"))
        {
            Assert.DoesNotContain(Instructions(method), item => item.Member is MethodBase called && ((called.DeclaringType?.Name == "NativeMethods" && called.Name == "RootConstants") || called.Name == "get_Payload" || called.Name == "get_Storage"));
        }
    }

    /// <summary>Rejects encoder reentrancy while preserving graph admission table autobinding.</summary>
    /// <param name="encoderName">The encoder implementation.</param>
    [Theory]
    [InlineData("Direct3D12ComputeEncoder")]
    [InlineData("Direct3D12RenderEncoder")]
    public void SelectionGuardsMutationsButAllowsGraphTableAutobinding(string encoderName)
    {
        Type encoder = typeof(Lumyte.Graphics.Direct3D12.Direct3D12Device).Assembly.GetType("Lumyte.Graphics.Direct3D12." + encoderName, true)!;
        foreach (MethodInfo method in encoder.GetMethods(BindingFlags.Instance | BindingFlags.Public).Where(method => method.DeclaringType == encoder && method.Name != "SetArgumentTable" && method.Name != "SetIndexBuffer"))
        {
            string guard = method.Name == "SetArguments" ? "ValidateRootSelection" : "ThrowIfSettingArguments";
            Assert.Contains(Instructions(method), item => item.Member is MethodBase called && called.Name == guard);
        }

        MethodInfo indices = encoder.GetMethod("SetIndices", BindingFlags.Instance | BindingFlags.NonPublic)!;
        if (indices != null)
        {
            Assert.Contains(Instructions(indices), item => item.Member is MethodBase called && called.Name == "ThrowIfSettingArguments");
        }

        MethodInfo table = encoder.GetMethod("SetArgumentTable")!;
        Assert.DoesNotContain(Instructions(table), item => item.Member is MethodBase called && called.Name == "ThrowIfSettingArguments");
        foreach (MethodInfo set in encoder.GetMethods().Where(method => method.Name == "SetArguments"))
        {
            Assert.True(set.GetMethodBody()!.ExceptionHandlingClauses.Any(clause => clause.Flags == ExceptionHandlingClauseOptions.Finally), "Set must always release the reentrancy guard.");
            Assert.Contains(Instructions(set), item => item.Code == OpCodes.Stfld && item.Member?.Name == "_settingArguments");
        }
    }

    private static IEnumerable<(OpCode Code, MemberInfo? Member)> Instructions(MethodInfo method)
    {
        byte[] bytes = method.GetMethodBody()?.GetILAsByteArray() ?? [];
        var codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static).Where(field => field.FieldType == typeof(OpCode)).Select(field => (OpCode)field.GetValue(null)!).ToDictionary(code => code.Value);
        for (int offset = 0; offset < bytes.Length;)
        {
            short value = bytes[offset++];
            if (value == 0xfe)
            {
                value = unchecked((short)(0xfe00 | bytes[offset++]));
            }

            OpCode code = codes[value];
            MemberInfo? member = null;
            int size = code.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + (BitConverter.ToInt32(bytes, offset) * 4),
                _ => 4,
            };
            if (code.OperandType is OperandType.InlineMethod or OperandType.InlineField)
            {
                member = method.Module.ResolveMember(BitConverter.ToInt32(bytes, offset), method.DeclaringType?.GetGenericArguments(), method.IsGenericMethod ? method.GetGenericArguments() : null);
            }

            offset += size;
            yield return (code, member);
        }
    }
}
