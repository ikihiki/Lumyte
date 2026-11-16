#include "lumyte.slang"
#include "stroke-membership.slang"
struct DrawRecord2D {
    uint Kind; uint Flags; uint PaintId; uint ClipId;
    float4 DeviceBounds; float4 InverseX; float4 InverseY; float4 Shape0;
    uint PayloadOffset16; uint PayloadCount; uint Mode; uint ExtraBits;
};
struct ShapeMembershipArguments2D {
    GpuBufferRef<DrawRecord2D> Records;
    GpuBufferRef<float4> Numeric;
    GpuRWBufferRef<uint> Output;
    uint RecordIndex; uint Width; uint Height; uint Offset;
};
ConstantBuffer<ShapeMembershipArguments2D> arguments;
float cross2(float2 a,float2 b) { return a.x*b.y-a.y*b.x; }
float4 loadNumeric(uint index) { return arguments.Numeric.Load(min(index,arguments.Numeric.Count-1)); }
bool contains(DrawRecord2D r,float2 pixel) {
    float2 p=float2(dot(r.InverseX.xyz,float3(pixel,1)),dot(r.InverseY.xyz,float3(pixel,1)));
    if(r.Kind==4) {
        int winding=0;
        for(uint i=0;i<r.PayloadCount;i++) {
            float4 edge=loadNumeric(r.PayloadOffset16+i);
            float side=cross2(edge.zw-edge.xy,p-edge.xy);
            if(edge.y<=p.y && edge.w>p.y && side>0) winding++;
            if(edge.w<=p.y && edge.y>p.y && side<0) winding--;
        }
        return r.Mode==0 ? winding!=0 : (abs(winding)%2)!=0;
    }
    if(r.Kind==5) {
        for(uint i=0;i<r.PayloadCount;i++) {
            float2 a=loadNumeric(r.PayloadOffset16+i*3).xy;
            float2 b=loadNumeric(r.PayloadOffset16+i*3+1).xy;
            float2 c=loadNumeric(r.PayloadOffset16+i*3+2).xy;
            if(strokeTriangleContains(a,b,c,p)) return true;
        }
    }
    return false;
}
[shader("compute")]
[numthreads(16,16,1)]
void main(uint3 thread:SV_DispatchThreadID) {
    if(thread.x>=arguments.Width || thread.y>=arguments.Height || arguments.RecordIndex>=arguments.Records.Count) return;
    uint index=arguments.Offset+thread.y*arguments.Width+thread.x;
    if(index>=arguments.Output.Count) return;
    DrawRecord2D r=arguments.Records.Load(arguments.RecordIndex);
    float2 origin=r.DeviceBounds.xy+float2(thread.xy);
    uint bits=0;
    for(uint sample=0;sample<16;sample++)
        if(contains(r,origin+(float2(sample%4,sample/4)+0.5)/4)) bits|=1u<<sample;
    arguments.Output.Store(index,bits);
}
