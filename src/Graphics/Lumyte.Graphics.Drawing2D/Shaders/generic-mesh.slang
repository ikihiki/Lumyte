struct Output { float4 Position:SV_Position; };
[shader("mesh")]
[numthreads(1,1,1)]
[outputtopology("triangle")]
void main(out vertices Output vertices[4],out indices uint3 triangles[2]) {
 SetMeshOutputCounts(4,2);
 vertices[0].Position=float4(-1,1,0,1); vertices[1].Position=float4(1,1,0,1);
 vertices[2].Position=float4(-1,-1,0,1); vertices[3].Position=float4(1,-1,0,1);
 triangles[0]=uint3(0,1,2); triangles[1]=uint3(2,1,3);
}
