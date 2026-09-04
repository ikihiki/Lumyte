using System.Runtime.InteropServices;

using Silk.NET.Shaderc;

namespace Lumyte.Graphics.Vulkan.Tests;

internal static unsafe class TriangleShaders
{
    internal const string VertexSource = """
        #version 450
        const vec2 positions[3] = vec2[3](
            vec2(-0.8, -0.8),
            vec2( 0.8, -0.8),
            vec2( 0.0,  0.8));
        void main() { gl_Position = vec4(positions[gl_VertexIndex], 0.0, 1.0); }
        """;

    internal const string PixelSource = """
        #version 450
        layout(location = 0) out vec4 color;
        void main() { color = vec4(1.0, 0.2, 0.1, 1.0); }
        """;

    internal const string TexturedVertexSource = """
        #version 450
        const vec2 positions[6] = vec2[6](
            vec2(-1.0, -1.0), vec2( 1.0, -1.0), vec2( 1.0,  1.0),
            vec2(-1.0, -1.0), vec2( 1.0,  1.0), vec2(-1.0,  1.0));
        const vec2 uvs[6] = vec2[6](
            vec2(0.0, 0.0), vec2(1.0, 0.0), vec2(1.0, 1.0),
            vec2(0.0, 0.0), vec2(1.0, 1.0), vec2(0.0, 1.0));
        layout(location = 0) out vec2 uv;
        void main()
        {
            gl_Position = vec4(positions[gl_VertexIndex], 0.0, 1.0);
            uv = uvs[gl_VertexIndex];
        }
        """;

    internal const string TexturedPixelSource = """
        #version 450
        layout(set = 0, binding = 0) uniform texture2D textures[64];
        layout(set = 1, binding = 0) uniform sampler samplers[64];
        layout(push_constant) uniform RootData { uint textureIndex; uint samplerIndex; } rootData;
        layout(location = 0) in vec2 uv;
        layout(location = 0) out vec4 color;
        void main()
        {
            color = texture(sampler2D(textures[rootData.textureIndex], samplers[rootData.samplerIndex]), uv);
        }
        """;

    internal const string BufferPixelSource = """
        #version 450
        layout(set = 2, binding = 0, std430) readonly buffer ShaderBuffer
        {
            vec4 color;
        } shaderBuffers[64];
        layout(location = 0) out vec4 outputColor;
        void main() { outputColor = shaderBuffers[0].color; }
        """;

    internal const string ComputeSource = """
        #version 450
        layout(local_size_x = 8, local_size_y = 1, local_size_z = 1) in;
        layout(set = 0, binding = 0) buffer OutputBuffer { uint values[]; } outputBuffer;
        void main()
        {
            uint index = gl_GlobalInvocationID.x;
            outputBuffer.values[index] = 0x5a000000u | (index * 17u + 3u);
        }
        """;

    internal static byte[] Compile(string source, ShaderKind kind)
    {
        Shaderc api = Shaderc.GetApi();
        Compiler* compiler = api.CompilerInitialize();
        if (compiler is null) { throw new InvalidOperationException("shaderc compiler initialization failed."); }
        try
        {
            CompilationResult* result = api.CompileIntoSpv(
                compiler,
                source,
                checked((nuint)System.Text.Encoding.UTF8.GetByteCount(source)),
                kind,
                "triangle.glsl",
                "main",
                null);
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
            finally
            {
                api.ResultRelease(result);
            }
        }
        finally
        {
            api.CompilerRelease(compiler);
            api.Dispose();
        }
    }
}
