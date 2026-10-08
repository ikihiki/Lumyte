// Logical descriptor lowering. Stable wire IDs are resolved against this draw's dependency table.
struct BindlessLookup
{
    uint4 root;
    uint4 textures[8];
    uint4 samplers[4];
    uint4 buffers[4];
};
[[vk::binding(1, 0)]] ConstantBuffer<BindlessLookup> bindlessLookup;
[[vk::binding(2, 0)]] Texture2D<float4> bindlessTexture0;
[[vk::binding(3, 0)]] Texture2D<float4> bindlessTexture1;
[[vk::binding(4, 0)]] Texture2D<float4> bindlessTexture2;
[[vk::binding(5, 0)]] Texture2D<float4> bindlessTexture3;
[[vk::binding(6, 0)]] Texture2D<float4> bindlessTexture4;
[[vk::binding(7, 0)]] Texture2D<float4> bindlessTexture5;
[[vk::binding(8, 0)]] Texture2D<float4> bindlessTexture6;
[[vk::binding(9, 0)]] Texture2D<float4> bindlessTexture7;
[[vk::binding(10, 0)]] SamplerState bindlessSampler0;
[[vk::binding(11, 0)]] SamplerState bindlessSampler1;
[[vk::binding(12, 0)]] SamplerState bindlessSampler2;
[[vk::binding(13, 0)]] SamplerState bindlessSampler3;
[[vk::binding(14, 0)]] ByteAddressBuffer bindlessBuffer0;
[[vk::binding(15, 0)]] ByteAddressBuffer bindlessBuffer1;
[[vk::binding(16, 0)]] ByteAddressBuffer bindlessBuffer2;
[[vk::binding(17, 0)]] ByteAddressBuffer bindlessBuffer3;

uint shaderDataIndex(uint index) { return bindlessLookup.root.x + index; }
uint shaderDataCount() { return bindlessLookup.root.y; }
uint resolveTexture(uint identity)
{
    if (identity == 0) return 0xffffffff;
    for (uint i = 0; i < 8; i++) if (bindlessLookup.textures[i].x == identity) return i;
    return 0xffffffff;
}
uint resolveSampler(uint identity)
{
    if (identity == 0) return 0xffffffff;
    for (uint i = 0; i < 4; i++) if (bindlessLookup.samplers[i].x == identity) return i;
    return 0xffffffff;
}
float4 sampleTextureGrad(uint textureIdentity, uint samplerIdentity, float2 uv, float2 dx, float2 dy)
{
    uint textureIndex = resolveTexture(textureIdentity);
    uint samplerIndex = resolveSampler(samplerIdentity);
    switch (textureIndex)
    {
    case 0:
        switch (samplerIndex)
        {
        case 0: return bindlessTexture0.SampleGrad(bindlessSampler0, uv, dx, dy);
        case 1: return bindlessTexture0.SampleGrad(bindlessSampler1, uv, dx, dy);
        case 2: return bindlessTexture0.SampleGrad(bindlessSampler2, uv, dx, dy);
        case 3: return bindlessTexture0.SampleGrad(bindlessSampler3, uv, dx, dy);
        default: return float4(0);
        }
    case 1:
        switch (samplerIndex)
        {
        case 0: return bindlessTexture1.SampleGrad(bindlessSampler0, uv, dx, dy);
        case 1: return bindlessTexture1.SampleGrad(bindlessSampler1, uv, dx, dy);
        case 2: return bindlessTexture1.SampleGrad(bindlessSampler2, uv, dx, dy);
        case 3: return bindlessTexture1.SampleGrad(bindlessSampler3, uv, dx, dy);
        default: return float4(0);
        }
    case 2:
        switch (samplerIndex)
        {
        case 0: return bindlessTexture2.SampleGrad(bindlessSampler0, uv, dx, dy);
        case 1: return bindlessTexture2.SampleGrad(bindlessSampler1, uv, dx, dy);
        case 2: return bindlessTexture2.SampleGrad(bindlessSampler2, uv, dx, dy);
        case 3: return bindlessTexture2.SampleGrad(bindlessSampler3, uv, dx, dy);
        default: return float4(0);
        }
    case 3:
        switch (samplerIndex)
        {
        case 0: return bindlessTexture3.SampleGrad(bindlessSampler0, uv, dx, dy);
        case 1: return bindlessTexture3.SampleGrad(bindlessSampler1, uv, dx, dy);
        case 2: return bindlessTexture3.SampleGrad(bindlessSampler2, uv, dx, dy);
        case 3: return bindlessTexture3.SampleGrad(bindlessSampler3, uv, dx, dy);
        default: return float4(0);
        }
    case 4:
        switch (samplerIndex)
        {
        case 0: return bindlessTexture4.SampleGrad(bindlessSampler0, uv, dx, dy);
        case 1: return bindlessTexture4.SampleGrad(bindlessSampler1, uv, dx, dy);
        case 2: return bindlessTexture4.SampleGrad(bindlessSampler2, uv, dx, dy);
        case 3: return bindlessTexture4.SampleGrad(bindlessSampler3, uv, dx, dy);
        default: return float4(0);
        }
    case 5:
        switch (samplerIndex)
        {
        case 0: return bindlessTexture5.SampleGrad(bindlessSampler0, uv, dx, dy);
        case 1: return bindlessTexture5.SampleGrad(bindlessSampler1, uv, dx, dy);
        case 2: return bindlessTexture5.SampleGrad(bindlessSampler2, uv, dx, dy);
        case 3: return bindlessTexture5.SampleGrad(bindlessSampler3, uv, dx, dy);
        default: return float4(0);
        }
    case 6:
        switch (samplerIndex)
        {
        case 0: return bindlessTexture6.SampleGrad(bindlessSampler0, uv, dx, dy);
        case 1: return bindlessTexture6.SampleGrad(bindlessSampler1, uv, dx, dy);
        case 2: return bindlessTexture6.SampleGrad(bindlessSampler2, uv, dx, dy);
        case 3: return bindlessTexture6.SampleGrad(bindlessSampler3, uv, dx, dy);
        default: return float4(0);
        }
    case 7:
        switch (samplerIndex)
        {
        case 0: return bindlessTexture7.SampleGrad(bindlessSampler0, uv, dx, dy);
        case 1: return bindlessTexture7.SampleGrad(bindlessSampler1, uv, dx, dy);
        case 2: return bindlessTexture7.SampleGrad(bindlessSampler2, uv, dx, dy);
        case 3: return bindlessTexture7.SampleGrad(bindlessSampler3, uv, dx, dy);
        default: return float4(0);
        }
    default: return float4(0);
    }
}

uint loadBufferUInt(uint identity, uint byteOffset)
{
    if (identity == 0 || byteOffset % 4 != 0) return 0;
    for (uint i = 0; i < 4; i++)
    {
        uint4 entry = bindlessLookup.buffers[i];
        if (entry.x == identity)
        {
            if (entry.z < 4 || byteOffset > entry.z - 4) return 0;
            uint address = entry.y + byteOffset;
            switch (i)
            {
            case 0: return bindlessBuffer0.Load(address);
            case 1: return bindlessBuffer1.Load(address);
            case 2: return bindlessBuffer2.Load(address);
            case 3: return bindlessBuffer3.Load(address);
            }
        }
    }
    return 0;
}
