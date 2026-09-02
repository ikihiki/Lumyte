using System.Runtime.InteropServices;

using Silk.NET.Shaderc;

namespace Lumyte.Graphics.Vulkan.Samples;

internal static unsafe class SampleShaderCompiler
{
    internal static byte[] Compile(string source, ShaderKind kind)
    {
        Shaderc api = Shaderc.GetApi();
        Compiler* compiler = api.CompilerInitialize();
        if (compiler is null) { throw new InvalidOperationException("shaderc compiler initialization failed."); }
        try
        {
            CompilationResult* result = api.CompileIntoSpv(compiler, source,
                checked((nuint)System.Text.Encoding.UTF8.GetByteCount(source)), kind,
                "vulkan-sample.glsl", "main", null);
            try
            {
                if (api.ResultGetCompilationStatus(result) != CompilationStatus.Success)
                {
                    throw new InvalidOperationException(api.ResultGetErrorMessageS(result));
                }

                int length = checked((int)api.ResultGetLength(result));
                byte[] bytes = new byte[length];
                Marshal.Copy((nint)api.ResultGetBytes(result), bytes, 0, length);
                return bytes;
            }
            finally { api.ResultRelease(result); }
        }
        finally
        {
            api.CompilerRelease(compiler);
            api.Dispose();
        }
    }
}
