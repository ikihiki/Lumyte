using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using S = Silk.NET.WebGPU;
using N = WebGpuSharp;
using F = WebGpuSharp.FFI;

namespace Lumyte.Graphics.WebGPU;

/// <summary>Converts legacy managed descriptors to the pinned Dawn C ABI; no legacy native structs cross the ABI.</summary>
internal sealed unsafe class ModernWebGpuApi
{
    private F.InstanceHandle instance;
    private GCHandle self;
    private readonly HashSet<GCHandle> callbackHandles = [];
    internal string? Error { get; private set; }
    private static T ConvertEnum<T>(Enum value) where T : struct, Enum
    {
        string name = value.ToString();
        name = name.Replace("Dimension1D", "D1").Replace("Dimension2D", "D2").Replace("Dimension3D", "D3")
            .Replace("Dimension2DArray", "D2Array").Replace("TextureViewD", "D").Replace("TextureD", "D");
        return Enum.Parse<T>(name, ignoreCase: true);
    }
    private static F.StringViewFFI Text(byte* value) => value == null ? F.StringViewFFI.NullValue
        : F.StringViewFFI.CreateExplicitlySized(value, MemoryMarshal.CreateReadOnlySpanFromNullTerminated(value).Length);
    private static N.Extent3D Extent(S.Extent3D value) => new() { Width = value.Width, Height = value.Height, DepthOrArrayLayers = value.DepthOrArrayLayers };
    private static F.TexelCopyTextureInfoFFI TextureCopy(S.ImageCopyTexture value) => new()
    {
        Texture = new((nuint)value.Texture), MipLevel = value.MipLevel, Aspect = ConvertEnum<N.TextureAspect>(value.Aspect),
        Origin = new() { X = value.Origin.X, Y = value.Origin.Y, Z = value.Origin.Z },
    };
    private static N.TexelCopyBufferLayout BufferLayout(S.TextureDataLayout value) => new()
    { Offset = value.Offset, BytesPerRow = value.BytesPerRow, RowsPerImage = value.RowsPerImage };

    internal S.Instance* CreateInstance()
    {
        N.InstanceFeatureName feature = N.InstanceFeatureName.TimedWaitAny;
        var description = new F.InstanceDescriptorFFI { RequiredFeatureCount = 1, RequiredFeatures = &feature };
        instance = F.WebGPU_FFI.CreateInstance(&description);
        return (S.Instance*)(nuint)instance;
    }
    internal sealed class CallbackState
    {
        public nuint Handle;
        public int Status;
        public N.Future Future;
        public GCHandle Root;
    }
    private CallbackState NewCallback()
    {
        var state = new CallbackState();
        state.Root = GCHandle.Alloc(state);
        callbackHandles.Add(state.Root);
        return state;
    }
    private void ReleaseCallback(CallbackState state)
    {
        if (callbackHandles.Remove(state.Root)) { state.Root.Free(); }
    }
    private static CallbackState State(void* data) => (CallbackState)GCHandle.FromIntPtr((nint)data).Target!;
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void AdapterCallback(N.RequestAdapterStatus status, F.AdapterHandle adapter, F.StringViewFFI message, void* data, void* unused)
    { CallbackState state = State(data); state.Handle = (nuint)adapter; state.Status = (int)status; }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void DeviceCallback(N.RequestDeviceStatus status, F.DeviceHandle device, F.StringViewFFI message, void* data, void* unused)
    { CallbackState state = State(data); state.Handle = (nuint)device; state.Status = (int)status; }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void ErrorCallback(F.DeviceHandle* device, N.ErrorType type, F.StringViewFFI message, void* data, void* unused)
    {
        var owner = (ModernWebGpuApi)GCHandle.FromIntPtr((nint)data).Target!;
        owner.Error ??= Encoding.UTF8.GetString(new ReadOnlySpan<byte>(message.Data, checked((int)message.Length)));
    }
    private void CheckError()
    {
        if (Error is { } error) { Error = null; throw new InvalidOperationException($"WebGPU validation: {error}"); }
    }

    internal S.Adapter* RequestAdapter()
    {
        CallbackState result = NewCallback();
        var options = new F.RequestAdapterOptionsFFI { PowerPreference = N.PowerPreference.HighPerformance };
        var callback = new F.RequestAdapterCallbackInfoFFI { Mode = N.CallbackMode.WaitAnyOnly, Callback = &AdapterCallback, Userdata1 = (void*)GCHandle.ToIntPtr(result.Root) };
        Wait(F.WebGPU_FFI.InstanceRequestAdapter(instance, &options, callback));
        ReleaseCallback(result);
        if (result.Handle == 0) { throw new NotSupportedException($"WebGPU adapter request failed ({result.Status})."); }
        return (S.Adapter*)result.Handle;
    }
    internal S.Device* RequestDevice(S.Adapter* adapter)
    {
        N.Limits supported = new();
        if (F.WebGPU_FFI.AdapterGetLimits(new((nuint)adapter), &supported) != N.Status.Success
            || supported.MaxImmediateSize < GpuShaderBindingConvention.RootDataSize)
        { throw new NotSupportedException("WebGPU requires 64 bytes of immediate data. Buffer fallback is disabled."); }
        N.Limits limits = new() { MaxImmediateSize = GpuShaderBindingConvention.RootDataSize };
        self = GCHandle.Alloc(this);
        var description = new F.DeviceDescriptorFFI
        {
            RequiredLimits = &limits,
            UncapturedErrorCallbackInfo = new() { Callback = &ErrorCallback, Userdata1 = (void*)GCHandle.ToIntPtr(self) },
        };
        CallbackState result = NewCallback();
        var callback = new F.RequestDeviceCallbackInfoFFI { Mode = N.CallbackMode.WaitAnyOnly, Callback = &DeviceCallback, Userdata1 = (void*)GCHandle.ToIntPtr(result.Root) };
        Wait(F.WebGPU_FFI.AdapterRequestDevice(new((nuint)adapter), &description, callback));
        ReleaseCallback(result);
        if (result.Handle == 0) { throw new NotSupportedException($"WebGPU device request failed ({result.Status})."); }
        return (S.Device*)result.Handle;
    }

    internal bool DeviceGetLimits(S.Device* device, ref S.SupportedLimits result)
    {
        N.Limits limits = new();
        if (F.WebGPU_FFI.DeviceGetLimits(new((nuint)device), &limits) != N.Status.Success) { return false; }
        result.Limits.MaxTextureDimension2D = limits.MaxTextureDimension2D;
        result.Limits.MaxUniformBufferBindingSize = limits.MaxUniformBufferBindingSize;
        result.Limits.MinUniformBufferOffsetAlignment = limits.MinUniformBufferOffsetAlignment;
        result.Limits.MaxDynamicUniformBuffersPerPipelineLayout = limits.MaxDynamicUniformBuffersPerPipelineLayout;
        result.Limits.MaxBindGroups = limits.MaxBindGroups;
        result.Limits.MaxStorageBuffersPerShaderStage = limits.MaxStorageBuffersPerShaderStage;
        result.Limits.MaxSampledTexturesPerShaderStage = limits.MaxSampledTexturesPerShaderStage;
        result.Limits.MaxSamplersPerShaderStage = limits.MaxSamplersPerShaderStage;
        result.Limits.MaxStorageTexturesPerShaderStage = limits.MaxStorageTexturesPerShaderStage;
        result.Limits.MaxBufferSize = limits.MaxBufferSize;
        return true;
    }
    internal bool Poll(N.Future future)
    {
        var info = new N.FutureWaitInfo { Future = future };
        N.WaitStatus status = F.WebGPU_FFI.InstanceWaitAny(instance, 1, &info, 0);
        if (status == N.WaitStatus.Error) { throw new GpuDeviceLostException("WebGPU completion wait failed."); }
        return info.Completed;
    }
    internal void Wait(N.Future future)
    {
        var info = new N.FutureWaitInfo { Future = future };
        if (F.WebGPU_FFI.InstanceWaitAny(instance, 1, &info, ulong.MaxValue) != N.WaitStatus.Success)
        { throw new GpuDeviceLostException("WebGPU completion wait failed."); }
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void WorkCallback(N.QueueWorkDoneStatus status, F.StringViewFFI message, void* data, void* unused) => State(data).Status = (int)status;
    internal CallbackState Completion(S.Queue* queue)
    {
        CallbackState state = NewCallback();
        state.Future = F.WebGPU_FFI.QueueOnSubmittedWorkDone(new((nuint)queue),
            new F.QueueWorkDoneCallbackInfoFFI { Mode = N.CallbackMode.WaitAnyOnly, Callback = &WorkCallback, Userdata1 = (void*)GCHandle.ToIntPtr(state.Root) });
        return state;
    }
    internal void Wait(CallbackState state)
    {
        Wait(state.Future);
        CheckCompletion(state);
    }
    internal bool Poll(CallbackState state)
    {
        if (!Poll(state.Future)) { return false; }
        CheckCompletion(state);
        return true;
    }
    private void CheckCompletion(CallbackState state)
    {
        ReleaseCallback(state);
        if (state.Status != (int)N.QueueWorkDoneStatus.Success)
        { throw new GpuDeviceLostException($"WebGPU submitted work failed: {(N.QueueWorkDoneStatus)state.Status}."); }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void MapCallback(N.MapAsyncStatus status, F.StringViewFFI message, void* data, void* unused) => State(data).Status = (int)status;
    internal byte[] ReadMapped(S.Buffer* buffer, nuint size)
    {
        CallbackState state = NewCallback();
        var callback = new F.BufferMapCallbackInfoFFI { Mode = N.CallbackMode.WaitAnyOnly, Callback = &MapCallback, Userdata1 = (void*)GCHandle.ToIntPtr(state.Root) };
        Wait(F.WebGPU_FFI.BufferMapAsync(new((nuint)buffer), N.MapMode.Read, 0, size, callback));
        ReleaseCallback(state);
        N.MapAsyncStatus status = (N.MapAsyncStatus)state.Status;
        if (status != N.MapAsyncStatus.Success) { throw new InvalidOperationException($"WebGPU map failed: {status}."); }
        try { return new ReadOnlySpan<byte>(F.WebGPU_FFI.BufferGetConstMappedRange(new((nuint)buffer), 0, size), checked((int)size)).ToArray(); }
        finally { F.WebGPU_FFI.BufferUnmap(new((nuint)buffer)); }
    }

    internal S.Buffer* DeviceCreateBuffer(S.Device* device, in S.BufferDescriptor value)
    {
        var description = new F.BufferDescriptorFFI { Size = value.Size, Usage = ConvertEnum<N.BufferUsage>(value.Usage), MappedAtCreation = (bool)value.MappedAtCreation };
        return (S.Buffer*)(nuint)F.WebGPU_FFI.DeviceCreateBuffer(new((nuint)device), &description);
    }
    internal S.Texture* DeviceCreateTexture(S.Device* device, in S.TextureDescriptor value)
    {
        var description = new F.TextureDescriptorFFI
        {
            Usage = ConvertEnum<N.TextureUsage>(value.Usage), Dimension = ConvertEnum<N.TextureDimension>(value.Dimension),
            Size = Extent(value.Size), Format = ConvertEnum<N.TextureFormat>(value.Format), MipLevelCount = value.MipLevelCount, SampleCount = value.SampleCount,
        };
        return (S.Texture*)(nuint)F.WebGPU_FFI.DeviceCreateTexture(new((nuint)device), &description);
    }
    internal S.TextureView* TextureCreateView(S.Texture* texture, in S.TextureViewDescriptor value)
    {
        var description = new F.TextureViewDescriptorFFI
        {
            Format = ConvertEnum<N.TextureFormat>(value.Format), Dimension = ConvertEnum<N.TextureViewDimension>(value.Dimension),
            BaseMipLevel = value.BaseMipLevel, MipLevelCount = value.MipLevelCount, BaseArrayLayer = value.BaseArrayLayer,
            ArrayLayerCount = value.ArrayLayerCount, Aspect = ConvertEnum<N.TextureAspect>(value.Aspect),
        };
        return (S.TextureView*)(nuint)F.WebGPU_FFI.TextureCreateView(new((nuint)texture), &description);
    }
    internal S.TextureView* TextureCreateView(S.Texture* texture, S.TextureViewDescriptor* unused)
        => (S.TextureView*)(nuint)F.WebGPU_FFI.TextureCreateView(new((nuint)texture), null);
    internal S.Sampler* DeviceCreateSampler(S.Device* device, in S.SamplerDescriptor value)
    {
        var description = new F.SamplerDescriptorFFI
        {
            AddressModeU = ConvertEnum<N.AddressMode>(value.AddressModeU), AddressModeV = ConvertEnum<N.AddressMode>(value.AddressModeV),
            AddressModeW = ConvertEnum<N.AddressMode>(value.AddressModeW), MagFilter = ConvertEnum<N.FilterMode>(value.MagFilter),
            MinFilter = ConvertEnum<N.FilterMode>(value.MinFilter), MipmapFilter = ConvertEnum<N.MipmapFilterMode>(value.MipmapFilter),
            LodMinClamp = value.LodMinClamp, LodMaxClamp = value.LodMaxClamp, MaxAnisotropy = value.MaxAnisotropy,
        };
        return (S.Sampler*)(nuint)F.WebGPU_FFI.DeviceCreateSampler(new((nuint)device), &description);
    }
    internal S.BindGroup* DeviceCreateBindGroup(S.Device* device, in S.BindGroupDescriptor value)
    {
        F.BindGroupEntryFFI* entries = stackalloc F.BindGroupEntryFFI[checked((int)value.EntryCount)];
        for (int index = 0; index < (int)value.EntryCount; index++)
        {
            S.BindGroupEntry entry = value.Entries[index];
            entries[index] = new()
            {
                Binding = entry.Binding, Buffer = new((nuint)entry.Buffer), Offset = entry.Offset, Size = entry.Size,
                TextureView = new((nuint)entry.TextureView), Sampler = new((nuint)entry.Sampler),
            };
        }
        var description = new F.BindGroupDescriptorFFI { Layout = new((nuint)value.Layout), EntryCount = value.EntryCount, Entries = entries };
        return (S.BindGroup*)(nuint)F.WebGPU_FFI.DeviceCreateBindGroup(new((nuint)device), &description);
    }
    internal S.BindGroupLayout* DeviceCreateBindGroupLayout(S.Device* device, in S.BindGroupLayoutDescriptor value)
    {
        N.BindGroupLayoutEntry* entries = stackalloc N.BindGroupLayoutEntry[checked((int)value.EntryCount)];
        for (int index = 0; index < (int)value.EntryCount; index++)
        {
            S.BindGroupLayoutEntry entry = value.Entries[index];
            entries[index] = new() { Binding = entry.Binding, Visibility = ConvertEnum<N.ShaderStage>(entry.Visibility) };
            if (entry.Buffer.Type != S.BufferBindingType.Undefined)
            { entries[index].Buffer = new() { Type = ConvertEnum<N.BufferBindingType>(entry.Buffer.Type), HasDynamicOffset = (bool)entry.Buffer.HasDynamicOffset, MinBindingSize = entry.Buffer.MinBindingSize }; }
            if (entry.Sampler.Type != S.SamplerBindingType.Undefined)
            { entries[index].Sampler = new() { Type = ConvertEnum<N.SamplerBindingType>(entry.Sampler.Type) }; }
            if (entry.Texture.SampleType != S.TextureSampleType.Undefined)
            { entries[index].Texture = new() { SampleType = ConvertEnum<N.TextureSampleType>(entry.Texture.SampleType), ViewDimension = ConvertEnum<N.TextureViewDimension>(entry.Texture.ViewDimension), Multisampled = (bool)entry.Texture.Multisampled }; }
            if (entry.StorageTexture.Access != S.StorageTextureAccess.Undefined)
            { entries[index].StorageTexture = new() { Access = ConvertEnum<N.StorageTextureAccess>(entry.StorageTexture.Access), Format = ConvertEnum<N.TextureFormat>(entry.StorageTexture.Format), ViewDimension = ConvertEnum<N.TextureViewDimension>(entry.StorageTexture.ViewDimension) }; }
        }
        var description = new F.BindGroupLayoutDescriptorFFI { EntryCount = value.EntryCount, Entries = entries };
        return (S.BindGroupLayout*)(nuint)F.WebGPU_FFI.DeviceCreateBindGroupLayout(new((nuint)device), &description);
    }
    internal S.PipelineLayout* DeviceCreatePipelineLayout(S.Device* device, in S.PipelineLayoutDescriptor value)
    {
        F.BindGroupLayoutHandle* layouts = stackalloc F.BindGroupLayoutHandle[checked((int)value.BindGroupLayoutCount)];
        for (int index = 0; index < (int)value.BindGroupLayoutCount; index++) { layouts[index] = new((nuint)value.BindGroupLayouts[index]); }
        var description = new F.PipelineLayoutDescriptorFFI { BindGroupLayoutCount = value.BindGroupLayoutCount, BindGroupLayouts = layouts, ImmediateSize = GpuShaderBindingConvention.RootDataSize };
        return (S.PipelineLayout*)(nuint)F.WebGPU_FFI.DeviceCreatePipelineLayout(new((nuint)device), &description);
    }
    internal S.ShaderModule* DeviceCreateShaderModule(S.Device* device, in S.ShaderModuleDescriptor value)
    {
        var source = (S.ShaderModuleWGSLDescriptor*)value.NextInChain;
        var wgsl = new F.ShaderSourceWGSLFFI { Chain = new() { SType = N.SType.ShaderSourceWGSL }, Code = Text(source->Code) };
        var description = new F.ShaderModuleDescriptorFFI { NextInChain = &wgsl.Chain };
        var module = F.WebGPU_FFI.DeviceCreateShaderModule(new((nuint)device), &description);
        try { CheckError(); }
        catch { F.WebGPU_FFI.ShaderModuleRelease(module); throw; }
        return (S.ShaderModule*)(nuint)module;
    }
    internal S.ComputePipeline* DeviceCreateComputePipeline(S.Device* device, in S.ComputePipelineDescriptor value)
    {
        var description = new F.ComputePipelineDescriptorFFI
        {
            Layout = new((nuint)value.Layout),
            Compute = new F.ComputeStateFFI { Module = new((nuint)value.Compute.Module), EntryPoint = Text(value.Compute.EntryPoint) },
        };
        return (S.ComputePipeline*)(nuint)F.WebGPU_FFI.DeviceCreateComputePipeline(new((nuint)device), &description);
    }
    private static N.BlendComponent Blend(S.BlendComponent value) => new()
    { Operation = ConvertEnum<N.BlendOperation>(value.Operation), SrcFactor = ConvertEnum<N.BlendFactor>(value.SrcFactor), DstFactor = ConvertEnum<N.BlendFactor>(value.DstFactor) };
    private static N.StencilFaceState Stencil(S.StencilFaceState value) => new()
    { Compare = ConvertEnum<N.CompareFunction>(value.Compare), FailOp = ConvertEnum<N.StencilOperation>(value.FailOp), DepthFailOp = ConvertEnum<N.StencilOperation>(value.DepthFailOp), PassOp = ConvertEnum<N.StencilOperation>(value.PassOp) };
    internal S.RenderPipeline* DeviceCreateRenderPipeline(S.Device* device, in S.RenderPipelineDescriptor value)
    {
        F.ColorTargetStateFFI* targets = stackalloc F.ColorTargetStateFFI[(int)value.Fragment->TargetCount];
        N.BlendState* blends = stackalloc N.BlendState[(int)value.Fragment->TargetCount];
        for (int i = 0; i < (int)value.Fragment->TargetCount; i++)
        {
            S.ColorTargetState target = value.Fragment->Targets[i];
            if (target.Blend != null) { blends[i] = new() { Color = Blend(target.Blend->Color), Alpha = Blend(target.Blend->Alpha) }; }
            targets[i] = new() { Format = ConvertEnum<N.TextureFormat>(target.Format), WriteMask = ConvertEnum<N.ColorWriteMask>(target.WriteMask), Blend = target.Blend == null ? null : &blends[i] };
        }
        var fragment = new F.FragmentStateFFI { Module = new((nuint)value.Fragment->Module), EntryPoint = Text(value.Fragment->EntryPoint), TargetCount = value.Fragment->TargetCount, Targets = targets };
        N.DepthStencilState depth = default;
        if (value.DepthStencil != null)
        {
            var d = *value.DepthStencil;
            depth = new()
            {
                Format = ConvertEnum<N.TextureFormat>(d.Format), DepthWriteEnabled = (bool)d.DepthWriteEnabled ? N.OptionalBool.True : N.OptionalBool.False,
                DepthCompare = ConvertEnum<N.CompareFunction>(d.DepthCompare), StencilFront = Stencil(d.StencilFront), StencilBack = Stencil(d.StencilBack),
                StencilReadMask = d.StencilReadMask, StencilWriteMask = d.StencilWriteMask,
            };
        }
        var description = new F.RenderPipelineDescriptorFFI
        {
            Layout = new((nuint)value.Layout), Vertex = new() { Module = new((nuint)value.Vertex.Module), EntryPoint = Text(value.Vertex.EntryPoint) },
            Fragment = &fragment, DepthStencil = value.DepthStencil == null ? null : &depth,
            Primitive = new() { Topology = ConvertEnum<N.PrimitiveTopology>(value.Primitive.Topology), FrontFace = ConvertEnum<N.FrontFace>(value.Primitive.FrontFace), CullMode = ConvertEnum<N.CullMode>(value.Primitive.CullMode) },
            Multisample = new() { Count = value.Multisample.Count, Mask = value.Multisample.Mask, AlphaToCoverageEnabled = (bool)value.Multisample.AlphaToCoverageEnabled },
        };
        var result = F.WebGPU_FFI.DeviceCreateRenderPipeline(new((nuint)device), &description);
        try { CheckError(); }
        catch { F.WebGPU_FFI.RenderPipelineRelease(result); throw; }
        return (S.RenderPipeline*)(nuint)result;
    }
    internal S.RenderPassEncoder* CommandEncoderBeginRenderPass(S.CommandEncoder* encoder, in S.RenderPassDescriptor value)
    {
        F.RenderPassColorAttachmentFFI* colors = stackalloc F.RenderPassColorAttachmentFFI[(int)value.ColorAttachmentCount];
        for (int i = 0; i < (int)value.ColorAttachmentCount; i++)
        {
            var color = value.ColorAttachments[i];
            colors[i] = new()
            {
                View = new((nuint)color.View), DepthSlice = color.DepthSlice, ResolveTarget = new((nuint)color.ResolveTarget),
                LoadOp = ConvertEnum<N.LoadOp>(color.LoadOp), StoreOp = ConvertEnum<N.StoreOp>(color.StoreOp),
                ClearValue = new() { R = color.ClearValue.R, G = color.ClearValue.G, B = color.ClearValue.B, A = color.ClearValue.A },
            };
        }
        F.RenderPassDepthStencilAttachmentFFI depth = default;
        if (value.DepthStencilAttachment != null)
        {
            var d = *value.DepthStencilAttachment;
            depth = new()
            {
                View = new((nuint)d.View), DepthLoadOp = ConvertEnum<N.LoadOp>(d.DepthLoadOp), DepthStoreOp = ConvertEnum<N.StoreOp>(d.DepthStoreOp),
                DepthClearValue = d.DepthClearValue, DepthReadOnly = (bool)d.DepthReadOnly,
                StencilLoadOp = ConvertEnum<N.LoadOp>(d.StencilLoadOp), StencilStoreOp = ConvertEnum<N.StoreOp>(d.StencilStoreOp),
                StencilClearValue = d.StencilClearValue, StencilReadOnly = (bool)d.StencilReadOnly,
            };
        }
        var description = new F.RenderPassDescriptorFFI { ColorAttachmentCount = value.ColorAttachmentCount, ColorAttachments = colors, DepthStencilAttachment = value.DepthStencilAttachment == null ? null : &depth };
        return (S.RenderPassEncoder*)(nuint)F.WebGPU_FFI.CommandEncoderBeginRenderPass(new((nuint)encoder), &description);
    }
    internal S.CommandEncoder* DeviceCreateCommandEncoder(S.Device* device, in S.CommandEncoderDescriptor unused)
        => (S.CommandEncoder*)(nuint)F.WebGPU_FFI.DeviceCreateCommandEncoder(new((nuint)device), null);
    internal S.CommandBuffer* CommandEncoderFinish(S.CommandEncoder* encoder, in S.CommandBufferDescriptor unused)
    {
        var commands = F.WebGPU_FFI.CommandEncoderFinish(new((nuint)encoder), null);
        try { CheckError(); }
        catch { F.WebGPU_FFI.CommandBufferRelease(commands); throw; }
        return (S.CommandBuffer*)(nuint)commands;
    }
    internal S.ComputePassEncoder* CommandEncoderBeginComputePass(S.CommandEncoder* encoder, in S.ComputePassDescriptor unused)
        => (S.ComputePassEncoder*)(nuint)F.WebGPU_FFI.CommandEncoderBeginComputePass(new((nuint)encoder), null);
    internal void QueueWriteBuffer(S.Queue* queue, S.Buffer* buffer, ulong offset, void* data, nuint size)
        => F.WebGPU_FFI.QueueWriteBuffer(new((nuint)queue), new((nuint)buffer), offset, data, size);
    internal void QueueWriteTexture(S.Queue* queue, in S.ImageCopyTexture target, void* data, nuint size, in S.TextureDataLayout layout, in S.Extent3D extent)
    {
        var t = TextureCopy(target); var l = BufferLayout(layout); var e = Extent(extent);
        F.WebGPU_FFI.QueueWriteTexture(new((nuint)queue), &t, data, size, &l, &e);
    }
    internal void CommandEncoderCopyTextureToBuffer(S.CommandEncoder* encoder, in S.ImageCopyTexture source, in S.ImageCopyBuffer target, in S.Extent3D extent)
    {
        var s = TextureCopy(source); var d = new F.TexelCopyBufferInfoFFI { Buffer = new((nuint)target.Buffer), Layout = BufferLayout(target.Layout) }; var e = Extent(extent);
        F.WebGPU_FFI.CommandEncoderCopyTextureToBuffer(new((nuint)encoder), &s, &d, &e);
    }
    internal void CommandEncoderCopyBufferToBuffer(S.CommandEncoder* encoder, S.Buffer* source, ulong sourceOffset, S.Buffer* target, ulong targetOffset, ulong size)
        => F.WebGPU_FFI.CommandEncoderCopyBufferToBuffer(new((nuint)encoder), new((nuint)source), sourceOffset, new((nuint)target), targetOffset, size);
    internal void QueueSubmit(S.Queue* queue, nuint count, S.CommandBuffer** commands)
    {
        F.CommandBufferHandle* handles = stackalloc F.CommandBufferHandle[(int)count];
        for (int i = 0; i < (int)count; i++) { handles[i] = new((nuint)commands[i]); }
        F.WebGPU_FFI.QueueSubmit(new((nuint)queue), count, handles);
    }
    internal void QueueSubmit(S.Queue* queue, nuint count, ref S.CommandBuffer* commands)
    { fixed (S.CommandBuffer** pointer = &commands) { QueueSubmit(queue, count, pointer); } }
    internal void RenderPassEncoderSetImmediates(S.RenderPassEncoder* pass, ReadOnlySpan<byte> data)
    { fixed (byte* p = data) { F.WebGPU_FFI.RenderPassEncoderSetImmediates(new((nuint)pass), 0, p, (nuint)data.Length); } }
    internal void ComputePassEncoderSetImmediates(S.ComputePassEncoder* pass, ReadOnlySpan<byte> data)
    { fixed (byte* p = data) { F.WebGPU_FFI.ComputePassEncoderSetImmediates(new((nuint)pass), 0, p, (nuint)data.Length); } }
    internal void InstanceRelease(S.Instance* value)
    {
        F.WebGPU_FFI.InstanceRelease(new((nuint)value));
        foreach (GCHandle handle in callbackHandles) { handle.Free(); }
        callbackHandles.Clear();
        if (self.IsAllocated) { self.Free(); }
    }
    internal void AdapterRelease(S.Adapter* value) => F.WebGPU_FFI.AdapterRelease(new((nuint)value));
    internal void DeviceRelease(S.Device* value) => F.WebGPU_FFI.DeviceRelease(new((nuint)value));
    internal void QueueRelease(S.Queue* value) => F.WebGPU_FFI.QueueRelease(new((nuint)value));
    internal void BufferRelease(S.Buffer* value) => F.WebGPU_FFI.BufferRelease(new((nuint)value));
    internal void TextureRelease(S.Texture* value) => F.WebGPU_FFI.TextureRelease(new((nuint)value));
    internal void TextureViewRelease(S.TextureView* value) => F.WebGPU_FFI.TextureViewRelease(new((nuint)value));
    internal void SamplerRelease(S.Sampler* value) => F.WebGPU_FFI.SamplerRelease(new((nuint)value));
    internal void BindGroupRelease(S.BindGroup* value) => F.WebGPU_FFI.BindGroupRelease(new((nuint)value));
    internal void BindGroupLayoutRelease(S.BindGroupLayout* value) => F.WebGPU_FFI.BindGroupLayoutRelease(new((nuint)value));
    internal void PipelineLayoutRelease(S.PipelineLayout* value) => F.WebGPU_FFI.PipelineLayoutRelease(new((nuint)value));
    internal void ShaderModuleRelease(S.ShaderModule* value) => F.WebGPU_FFI.ShaderModuleRelease(new((nuint)value));
    internal void RenderPipelineRelease(S.RenderPipeline* value) => F.WebGPU_FFI.RenderPipelineRelease(new((nuint)value));
    internal void ComputePipelineRelease(S.ComputePipeline* value) => F.WebGPU_FFI.ComputePipelineRelease(new((nuint)value));
    internal void CommandEncoderRelease(S.CommandEncoder* value) => F.WebGPU_FFI.CommandEncoderRelease(new((nuint)value));
    internal void CommandBufferRelease(S.CommandBuffer* value) => F.WebGPU_FFI.CommandBufferRelease(new((nuint)value));
    internal void RenderPassEncoderRelease(S.RenderPassEncoder* value) => F.WebGPU_FFI.RenderPassEncoderRelease(new((nuint)value));
    internal void ComputePassEncoderRelease(S.ComputePassEncoder* value) => F.WebGPU_FFI.ComputePassEncoderRelease(new((nuint)value));
    internal void BindGroupLayoutReference(S.BindGroupLayout* value) => F.WebGPU_FFI.BindGroupLayoutAddRef(new((nuint)value));
    internal S.Queue* DeviceGetQueue(S.Device* device) => (S.Queue*)(nuint)F.WebGPU_FFI.DeviceGetQueue(new((nuint)device));
    internal void RenderPassEncoderEnd(S.RenderPassEncoder* value) => F.WebGPU_FFI.RenderPassEncoderEnd(new((nuint)value));
    internal void RenderPassEncoderSetPipeline(S.RenderPassEncoder* pass, S.RenderPipeline* pipeline) => F.WebGPU_FFI.RenderPassEncoderSetPipeline(new((nuint)pass), new((nuint)pipeline));
    internal void RenderPassEncoderSetBindGroup(S.RenderPassEncoder* pass, uint index, S.BindGroup* group, nuint count, uint* offsets)
        => F.WebGPU_FFI.RenderPassEncoderSetBindGroup(new((nuint)pass), index, new((nuint)group), count, offsets);
    internal S.BindGroupLayout* RenderPipelineGetBindGroupLayout(S.RenderPipeline* pipeline, uint index)
        => (S.BindGroupLayout*)(nuint)F.WebGPU_FFI.RenderPipelineGetBindGroupLayout(new((nuint)pipeline), index);
    internal void ComputePassEncoderEnd(S.ComputePassEncoder* value) => F.WebGPU_FFI.ComputePassEncoderEnd(new((nuint)value));
    internal void ComputePassEncoderSetPipeline(S.ComputePassEncoder* pass, S.ComputePipeline* pipeline) => F.WebGPU_FFI.ComputePassEncoderSetPipeline(new((nuint)pass), new((nuint)pipeline));
    internal void ComputePassEncoderSetBindGroup(S.ComputePassEncoder* pass, uint index, S.BindGroup* group, nuint count, uint* offsets)
        => F.WebGPU_FFI.ComputePassEncoderSetBindGroup(new((nuint)pass), index, new((nuint)group), count, offsets);
    internal S.BindGroupLayout* ComputePipelineGetBindGroupLayout(S.ComputePipeline* pipeline, uint index)
        => (S.BindGroupLayout*)(nuint)F.WebGPU_FFI.ComputePipelineGetBindGroupLayout(new((nuint)pipeline), index);
    internal void RenderPassEncoderSetViewport(S.RenderPassEncoder* pass, float x, float y, float width, float height, float min, float max)
        => F.WebGPU_FFI.RenderPassEncoderSetViewport(new((nuint)pass), x, y, width, height, min, max);
    internal void RenderPassEncoderSetScissorRect(S.RenderPassEncoder* pass, uint x, uint y, uint width, uint height)
        => F.WebGPU_FFI.RenderPassEncoderSetScissorRect(new((nuint)pass), x, y, width, height);
    internal void RenderPassEncoderDraw(S.RenderPassEncoder* pass, uint vertices, uint instances, uint firstVertex, uint firstInstance)
        => F.WebGPU_FFI.RenderPassEncoderDraw(new((nuint)pass), vertices, instances, firstVertex, firstInstance);
    internal void ComputePassEncoderDispatchWorkgroups(S.ComputePassEncoder* pass, uint x, uint y, uint z)
        => F.WebGPU_FFI.ComputePassEncoderDispatchWorkgroups(new((nuint)pass), x, y, z);
}
