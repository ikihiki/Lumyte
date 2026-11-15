using System.Reflection;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Xunit;

namespace Lumyte.Graphics.Validation.Tests;

/// <summary>Protects the opt-in assembly boundary without creating native devices.</summary>
public sealed class ApiBoundaryTests
{
    /// <summary>Checks backends have no dependency on validation.</summary>
    [Fact]
    public void RawBackendAssembliesDoNotReferenceValidation()
    {
        foreach (Assembly assembly in new[] { typeof(VulkanDevice).Assembly, typeof(WgpuDevice).Assembly })
        {
            Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference => reference.Name == typeof(ValidationDevice).Assembly.GetName().Name);
        }
    }

    /// <summary>Checks the decorator is portable across backend assemblies.</summary>
    [Fact]
    public void ValidationAssemblyDoesNotReferenceBackends() => Assert.DoesNotContain(typeof(ValidationDevice).Assembly.GetReferencedAssemblies(), reference => reference.Name is "Lumyte.Graphics.Vulkan" or "Lumyte.Graphics.Wgpu" or "Lumyte.Graphics.Browser" or "Lumyte.Graphics.Direct3D12");

    /// <summary>Checks native devices cannot silently enable a hidden managed engine.</summary>
    [Fact]
    public void RawDevicesExposeNoValidationToggleOrEngineFields()
    {
        foreach (Type type in new[] { typeof(VulkanDevice), typeof(WgpuDevice) })
        {
            Assert.DoesNotContain(type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static), method => method.Name == "EnableValidation");
            Assert.DoesNotContain(type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static), field => field.FieldType.Assembly == typeof(ValidationDevice).Assembly);
        }
    }
}
