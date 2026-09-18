struct FilterRoot
{
    uint textureIndex;
    uint mode;
    uint parameter;
    uint width;
    uint height;
};
[[vk::push_constant]] ConstantBuffer<FilterRoot> root;

[shader("vertex")]
float4 vertexMain(uint id : SV_VertexID) : SV_Position
{
    return float4(float2((id << 1) & 2, id & 2) * 2 - 1, 0, 1);
}

float4 readPixel(Texture2D<float4> image, int2 p, int2 size)
{ return image.Load(int3(clamp(p, int2(0), size - 1), 0)); }

[shader("fragment")]
float4 pixelMain(float4 position : SV_Position) : SV_Target0
{
    Texture2D<float4> image = ResourceDescriptorHeap[root.textureIndex];
    uint w, h; image.GetDimensions(w, h);
    int2 size = int2(w, h);
    int2 p = int2(position.xy);
    if (root.mode <= 1)
    {
        float2 coordinate = position.xy * float2(w, h) / float2(root.width, root.height);
        if (root.mode == 0) return readPixel(image, int2(floor(coordinate)), size);
        coordinate -= 0.5;
        int2 lo = int2(floor(coordinate)); float2 t = frac(coordinate);
        return lerp(lerp(readPixel(image, lo, size), readPixel(image, lo + int2(1,0), size), t.x),
                    lerp(readPixel(image, lo + int2(0,1), size), readPixel(image, lo + 1, size), t.x), t.y);
    }
    if (root.mode <= 3)
    {
        // Count clamped edge contributions without iterating over an unbounded radius.
        int axis = root.mode == 2 ? p.x : p.y;
        int extent = root.mode == 2 ? size.x : size.y;
        int radius = int(root.parameter);
        int first = axis - min(radius, axis);
        int last = axis + min(radius, extent - 1 - axis);
        float denominator = float(radius) * 2.0 + 1.0;
        float4 value = 0;
        for (int i = first; i <= last; i++)
        {
            float count = 1;
            if (i == 0) count += float(max(0, radius - axis));
            if (i == extent - 1) count += float(max(0, radius - (extent - 1 - axis)));
            value += readPixel(image, root.mode == 2 ? int2(i,p.y) : int2(p.x,i), size) * (count / denominator);
        }
        return value;
    }
    if (root.mode == 6)
    {
        float2 ratio = float2(w,h)/float2(root.width,root.height);
        float2 first = floor(position.xy)*ratio, last = (floor(position.xy)+1)*ratio;
        float4 sum = 0;
        for(int y=int(floor(first.y));y<int(ceil(last.y));y++)
        for(int x=int(floor(first.x));x<int(ceil(last.x));x++)
        {
            float2 weight = min(last,float2(x+1,y+1))-max(first,float2(x,y));
            sum += readPixel(image,int2(x,y),size)*weight.x*weight.y;
        }
        return sum/(ratio.x*ratio.y);
    }
    float4 value = readPixel(image, p, size);
    if (root.mode == 4)
    {
        if (value.a <= 0) return 0;
        // Logistic form of Reinhard remains finite even for extreme finite exposure.
        float3 straight = max(value.rgb / value.a, 0);
        float3 mapped = 1.0 / (1.0 + exp2(-asfloat(root.parameter) - log2(straight)));
        return float4(select(straight > 0, mapped, float3(0)) * value.a, value.a);
    }
    return value * asfloat(root.parameter);
}
