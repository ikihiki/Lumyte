struct BindingCameraArguments
{
    float4x4 ViewProjection;
};
ConstantBuffer<BindingCameraArguments> arguments;

[shader("fragment")]
float4 main() : SV_Target0
{
    return float4(arguments.ViewProjection[0][0], arguments.ViewProjection[1][1], arguments.ViewProjection[2][2], 1);
}
