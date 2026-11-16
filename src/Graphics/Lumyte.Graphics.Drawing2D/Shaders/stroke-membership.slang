#ifndef LUMYTE_STROKE_MEMBERSHIP
#define LUMYTE_STROKE_MEMBERSHIP
// Strokes carry unit coverage, unlike user meshes. Use the same binary predicate
// in compute and fragment stages. Explicit fma fixes the cross-product ordering
// without relying on stage-dependent contraction (WGSL has no precise qualifier).
float strokeCross2(float2 a,float2 b)
{
    return fma(-a.y,b.x,a.x*b.y);
}
bool strokeTriangleContains(float2 a,float2 b,float2 c,float2 p)
{
    float area=strokeCross2(b-a,c-a);
    if(abs(area)<1e-20) return false;
    float u=strokeCross2(b-p,c-p)/area;
    float v=strokeCross2(c-p,a-p)/area;
    float w=fma(-1,v,fma(-1,u,1));
    return u>=0 && v>=0 && w>=0;
}
#endif
