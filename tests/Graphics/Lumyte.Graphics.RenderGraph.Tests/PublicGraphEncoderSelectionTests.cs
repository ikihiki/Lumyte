using System.Reflection;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.RenderGraph.Tests;

/// <summary>Verifies separated logical argument selection from the consumer boundary.</summary>
public sealed partial class PublicGraphEncoderConsumerTests
{
    /// <summary>Execution signatures including names and defaults exactly match the ordinary encoders.</summary>
    [Fact]
    public void PublicExecutionSignaturesMatchOrdinaryEncoders()
    {
        Check(typeof(RenderGraphRenderEncoder), typeof(IRenderEncoder), ["Draw", "DrawIndexed", "DrawIndirect", "DrawIndexedIndirect"]);
        Check(typeof(RenderGraphComputeEncoder), typeof(IComputeEncoder), ["Dispatch", "DispatchIndirect"]);

        static void Check(Type graph, Type ordinary, string[] names)
        {
            MethodInfo[] setters = graph.GetMethods().Where(method => method.Name == "SetArguments").ToArray();
            Assert.Equal(2, setters.Length);
            MethodInfo setter = Assert.Single(setters, method => method.GetParameters().Length == 1);
            Assert.True(setter.IsGenericMethodDefinition);
            Assert.True(Assert.Single(setter.GetParameters()).ParameterType.IsByRef);
            Assert.Contains(typeof(IShaderArguments), Assert.Single(setter.GetGenericArguments()).GetGenericParameterConstraints());
            MethodInfo numeric = Assert.Single(setters, method => method.GetParameters().Length == 3);
            Assert.True(numeric.IsGenericMethodDefinition);
            Assert.True(numeric.GetParameters()[0].ParameterType.IsByRef);
            Type numericParameter = Assert.Single(numeric.GetGenericArguments());
            Assert.True((numericParameter.GenericParameterAttributes & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0);
            MethodInfo ordinaryNumeric = Assert.Single(ordinary.GetMethods(), method => method.Name == "SetArguments" && method.GetParameters().Length == 3);
            Assert.Equal(ordinaryNumeric.GetParameters().Skip(1).Select(Parameter), numeric.GetParameters().Skip(1).Select(Parameter));
            foreach (string name in names)
            {
                MethodInfo actual = Assert.Single(graph.GetMethods(), method => method.Name == name);
                MethodInfo expected = ordinary.GetMethod(name)!;
                Assert.Equal(expected.ReturnType, actual.ReturnType);
                Assert.Equal(expected.GetParameters().Select(Parameter), actual.GetParameters().Select(Parameter));
            }
        }

        static (Type Type, string? Name, bool Optional, object? Default) Parameter(ParameterInfo parameter)
            => (parameter.ParameterType, parameter.Name, parameter.IsOptional, parameter.DefaultValue);
    }

    /// <summary>The entire exported graph surface cannot transitively leak registration or tables through logical encoders.</summary>
    [Fact]
    public void ExportedLogicalSurfaceHasNoTransitiveNativeEscape()
    {
        Type[] exported = typeof(RenderGraphContext).Assembly.GetExportedTypes();
        Assert.DoesNotContain(exported, type => type.Name.Contains("ArgumentRegistration", StringComparison.Ordinal));
        foreach (Type type in exported.Where(type => type == typeof(RenderGraphRenderEncoder) || type == typeof(RenderGraphComputeEncoder)))
        {
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                Check(method.ReturnType);
                foreach (ParameterInfo parameter in method.GetParameters())
                {
                    Check(parameter.ParameterType);
                }
            }
        }

        static void Check(Type type)
        {
            Assert.False(type == typeof(IArgumentTable) || type == typeof(IRenderEncoder) || type == typeof(IComputeEncoder) || type == typeof(ShaderValueSnapshot) || type == typeof(ShaderResources));
            Assert.DoesNotContain("ArgumentRegistration", type.Name, StringComparison.Ordinal);
            if (type.HasElementType)
            {
                Check(type.GetElementType()!);
            }

            foreach (Type argument in type.GetGenericArguments())
            {
                Check(argument);
            }
        }
    }

    /// <summary>Selection freezes closures, survives failed replacement and render-state changes, and is pipeline-specific.</summary>
    [Fact]
    public void RenderSelectionFreezesValuesAndPreservesLastValidSelection()
    {
        using var graph = new RenderGraph(new PublicGraphConsumerDevice());
        ShaderArtifact artifact = Artifact(ShaderStage.Vertex);
        using var pipeline = new PublicGraphConsumerPipeline(artifact);
        using var other = new PublicGraphConsumerPipeline(artifact);
        using var wrong = new PublicGraphConsumerPipeline(Artifact(ShaderStage.Vertex));
        using var image = new TestTexture();
        RenderGraphTexture target = graph.ImportTexture(image, TextureState.Undefined, default);
        float value = 1;
        int reads = 0;
        bool fail = false;
        var logical = new ConsumerArguments(0, () =>
        {
            reads++;
            return fail ? throw new ApplicationException("capture failed") : value;
        });
        graph.AddPass("selection", context =>
        {
            using RenderGraphRenderEncoder encoder = context.BeginRenderPass(Attachments(target));
            Assert.Throws<InvalidOperationException>(() => encoder.Draw(3));
            encoder.SetPipeline(pipeline);
            Assert.Throws<InvalidOperationException>(() => encoder.Draw(3));
            encoder.SetArguments(logical);
            value = 2;
            encoder.Draw(3);
            encoder.SetViewport(new(0, 0, 1, 1, 0, 1));
            encoder.SetRenderState(new() { ColorTargets = [new()] });
            encoder.Draw(3);
            fail = true;
            Assert.Throws<ApplicationException>(() => encoder.SetArguments(logical));
            encoder.Draw(3);
            fail = false;
            encoder.SetPipeline(other);
            Assert.Throws<InvalidOperationException>(() => encoder.Draw(3));
            encoder.SetPipeline(wrong);
            Assert.Throws<InvalidOperationException>(() => encoder.Draw(3));
            encoder.SetPipeline(pipeline);
            encoder.Draw(3);
            encoder.SetArguments(logical);
            encoder.Draw(3);
            value = 9;
            using var indirect = new TestTypedBuffer<DrawIndirectArguments>(new() { Count = 1, Usage = BufferUsage.Indirect }, []);
            encoder.DrawIndirect(indirect.SliceElements(0, 1));
            using var indexed = new TestTypedBuffer<DrawIndexedIndirectArguments>(new() { Count = 1, Usage = BufferUsage.Indirect }, []);
            encoder.DrawIndexedIndirect(indexed.SliceElements(0, 1));
        }).UseTexture(target, TextureState.ColorAttachment, new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite)).KeepAlive();
        using CompiledRenderGraph plan = graph.Compile();
        using var commands = new PublicGraphConsumerCommands();
        plan.Record(commands);
        Assert.Equal(3, reads);
        Assert.Equal(new[] { 1f, 1f, 1f, 1f, 2f, 2f, 2f }, commands.Encoder.Executions.Select(ReadFloat));
    }

    /// <summary>Compute shares set-once semantics for direct and indirect work.</summary>
    [Fact]
    public void ComputeSelectionFreezesValuesForDirectAndIndirectDispatch()
    {
        using var graph = new RenderGraph(new PublicGraphConsumerDevice());
        ShaderArtifact artifact = Artifact(ShaderStage.Compute);
        using var pipeline = new PublicGraphConsumerComputePipeline(artifact);
        using var other = new PublicGraphConsumerComputePipeline(artifact);
        float value = 1;
        var logical = new ConsumerArguments(0, () => value);
        graph.AddPass("selection", context =>
        {
            using RenderGraphComputeEncoder encoder = context.BeginComputePass();
            Assert.Throws<InvalidOperationException>(() => encoder.Dispatch(1));
            encoder.SetPipeline(pipeline);
            Assert.Throws<InvalidOperationException>(() => encoder.Dispatch(1));
            encoder.SetArguments(logical);
            value = 2;
            encoder.Dispatch(1);
            encoder.Dispatch(2);
            encoder.SetPipeline(other);
            Assert.Throws<InvalidOperationException>(() => encoder.Dispatch(1));
            encoder.SetPipeline(pipeline);
            encoder.Dispatch(1);
            encoder.SetArguments(logical);
            using var indirect = new TestTypedBuffer<DispatchIndirectArguments>(new() { Count = 1, Usage = BufferUsage.Indirect }, []);
            encoder.DispatchIndirect(indirect.SliceElements(0, 1));
        }).KeepAlive();
        using CompiledRenderGraph plan = graph.Compile();
        using var commands = new PublicGraphConsumerCommands();
        plan.Record(commands);
        Assert.Equal(new[] { 1f, 1f, 1f, 2f }, commands.Encoder.Executions.Select(ReadFloat));
    }

    /// <summary>Native argument-free program semantics do not acquire an artificial graph root requirement.</summary>
    [Fact]
    public void ArgumentFreeProgramsExecuteWithoutSetArguments()
    {
        using var graph = new RenderGraph(new PublicGraphConsumerDevice());
        using var graphics = new PublicGraphConsumerPipeline(Artifact(ShaderStage.Vertex, hasRoot: false));
        using var compute = new PublicGraphConsumerComputePipeline(Artifact(ShaderStage.Compute, hasRoot: false));
        using var image = new TestTexture();
        RenderGraphTexture target = graph.ImportTexture(image, TextureState.Undefined, default);
        graph.AddPass("no render root", context =>
        {
            using RenderGraphRenderEncoder encoder = context.BeginRenderPass(Attachments(target));
            encoder.SetPipeline(graphics);
            encoder.Draw(3);
        }).UseTexture(target, TextureState.ColorAttachment, new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite)).KeepAlive();
        graph.AddPass("no compute root", context =>
        {
            using RenderGraphComputeEncoder encoder = context.BeginComputePass();
            encoder.SetPipeline(compute);
            encoder.Dispatch(1);
        }).KeepAlive();
        using CompiledRenderGraph plan = graph.Compile();
        using var commands = new PublicGraphConsumerCommands();
        plan.Record(commands);
        Assert.Equal(1, commands.Encoder.Draws);
        Assert.Equal(1, commands.Encoder.Dispatches);
        Assert.Empty(commands.Encoder.Snapshots);
    }

    /// <summary>A failed native setter does not replace the graph selection with a different cohort.</summary>
    [Fact]
    public void NativeSetFailurePreservesPreviousResourceSelection()
    {
        using var graph = new RenderGraph(new PublicGraphConsumerDevice());
        ShaderArtifact artifact = Artifact(ShaderStage.Compute);
        using var pipeline = new PublicGraphConsumerComputePipeline(artifact);
        var first = new ConsumerArguments(1f);
        var second = new ConsumerArguments(2f);
        using var commands = new PublicGraphConsumerCommands();
        graph.AddPass("native rejection", context =>
        {
            using RenderGraphComputeEncoder encoder = context.BeginComputePass();
            encoder.SetPipeline(pipeline);
            encoder.SetArguments(first);
            commands.Encoder.SetArgumentsException = new ArgumentException("native validation");
            Assert.Throws<ArgumentException>(() => encoder.SetArguments(second));
            commands.Encoder.SetArgumentsException = null;
            encoder.Dispatch(1);
        }).KeepAlive();
        using CompiledRenderGraph plan = graph.Compile();
        plan.Record(commands);
        Assert.Equal(1f, ReadFloat(Assert.Single(commands.Encoder.Executions)));
        Assert.Single(commands.Encoder.Snapshots);
        commands.Dispose();
    }

    private static float ReadFloat(ShaderValueSnapshot snapshot) => MemoryMarshal.Read<float>(snapshot.Data.Span[snapshot.Fields.Single(field => field.Path == "Float").Offset..]);
}
