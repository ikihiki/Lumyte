using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Validation;

internal sealed class ValidationMeshPipeline(ValidationDevice device, IMeshPipeline raw, MeshPipelineDesc desc) : ValidationResource<IMeshPipeline>(device, raw, device), IMeshPipeline
{
    public MeshPipelineDesc Desc
    {
        get
        {
            ValidateAlive();
            return desc;
        }
    }

    public override void ValidateAlive()
    {
        base.ValidateAlive();
        Device.Unwrap(desc.MeshShader);
        if (desc.TaskShader is { } task)
        {
            Device.Unwrap(task);
        }

        if (desc.FragmentShader is { } fragment)
        {
            Device.Unwrap(fragment);
        }
    }
}
