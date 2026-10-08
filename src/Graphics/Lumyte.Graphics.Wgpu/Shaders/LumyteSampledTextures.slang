// Backend finite sampled-resource helper. Buffer element schemas belong to the consumer.
[[vk::binding(1, 0)]] Texture2D<float4> materialTexture0;
[[vk::binding(2, 0)]] SamplerState materialSampler0;
[[vk::binding(3, 0)]] Texture2D<float4> materialTexture1;
[[vk::binding(4, 0)]] SamplerState materialSampler1;
[[vk::binding(5, 0)]] Texture2D<float4> materialTexture2;
[[vk::binding(6, 0)]] SamplerState materialSampler2;
[[vk::binding(7, 0)]] Texture2D<float4> materialTexture3;
[[vk::binding(8, 0)]] SamplerState materialSampler3;

float4 sampleSampledTexture2D(uint selector, float2 uv, float2 dx, float2 dy)
{
    switch (selector)
    {
    case 0: return materialTexture0.SampleGrad(materialSampler0, uv, dx, dy);
    case 1: return materialTexture1.SampleGrad(materialSampler1, uv, dx, dy);
    case 2: return materialTexture2.SampleGrad(materialSampler2, uv, dx, dy);
    case 3: return materialTexture3.SampleGrad(materialSampler3, uv, dx, dy);
    default: return float4(0, 0, 0, 0);
    }
}
