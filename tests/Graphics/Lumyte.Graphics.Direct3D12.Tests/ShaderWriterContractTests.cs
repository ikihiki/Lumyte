using System.Reflection;
using System.Reflection.Emit;
using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Direct3D12.Tests;

/// <summary>Checks the backend-owned writer without requiring a Windows device.</summary>
public sealed class ShaderWriterContractTests
{
    /// <summary>The actual D3D assembly contains an internal value writer, not a shared adapter.</summary>
    [Fact]
    public void WriterIsBackendOwned()
    {
        Type writer = Assert.IsAssignableFrom<Type>(typeof(Direct3D12Device).Assembly.GetType("Lumyte.Graphics.Direct3D12.Direct3D12ShaderValueWriter"));
        Assert.True(writer.IsValueType);
        Assert.False(writer.IsPublic);
        Assert.DoesNotContain(writer.GetFields(BindingFlags.Instance | BindingFlags.NonPublic), field => field.Name is "_expectedCount" or "_referenceCount");
        Assert.DoesNotContain(Assert.Single(writer.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)).GetParameters(), parameter => parameter.Name == "expectedCount");
        MethodInfo write = writer.GetMethod("WriteReference")!;
        byte[] bytes = write.GetMethodBody()!.GetILAsByteArray()!;
        Assert.NotEmpty(bytes);
        var calls = new List<string>();
        var codes = typeof(OpCodes).GetFields(BindingFlags.Static | BindingFlags.Public).Where(field => field.FieldType == typeof(OpCode)).Select(field => (OpCode)field.GetValue(null)!).ToDictionary(code => code.Value);
        for (int offset = 0; offset < bytes.Length;)
        {
            short value = bytes[offset++];
            if (value == 0xfe)
            {
                value = unchecked((short)(0xfe00 | bytes[offset++]));
            }

            OpCode code = codes[value];
            Assert.NotEqual(OpCodes.Localloc, code);
            if (code.OperandType == OperandType.InlineMethod)
            {
                calls.Add(write.Module.ResolveMethod(BitConverter.ToInt32(bytes, offset), writer.GetGenericArguments(), write.GetGenericArguments())!.Name);
            }

            offset += code.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + (BitConverter.ToInt32(bytes, offset) * 4),
                _ => 4,
            };
        }

        Assert.Single(calls, name => name == "ValidateWrite");
        Assert.DoesNotContain("WriteValue", calls);
        Assert.True(typeof(IShaderValueWriter).IsAssignableFrom(writer));
        Assert.Null(typeof(Direct3D12Device).Assembly.GetType("Lumyte.Graphics.Direct3D12.Direct3D12ShaderReferenceEncoding"));
    }
}
