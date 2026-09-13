using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using Lumyte.Graphics.WebGPU.Browser;
using P = Lumyte.Graphics.Portable;

public static partial class BrowserCases
{
    private static async Task<object> ExactBufferSizeAsync()
    {
        using BrowserCaseFixture fixture = await BrowserCaseFixture.CreateAsync();
        string parameter = "";
        try { _ = fixture.Buffer(9_007_199_254_740_992, P.GpuBufferUsage.CopyDestination); }
        catch (ArgumentOutOfRangeException error) { parameter = error.ParamName ?? ""; }
        P.GpuBufferHandle valid = fixture.Buffer(4, P.GpuBufferUsage.MapRead | P.GpuBufferUsage.CopyDestination);
        return new { parameter, actual = (await fixture.ReadAsync(valid, 4)).Select(item => (int)item).ToArray() };
    }

    private static async Task<object> SynchronousEncodingFailureAsync()
    {
        using BrowserCaseFixture fixture = await BrowserCaseFixture.CreateAsync();
        P.GpuTextureHandle target = fixture.Texture();
        P.GpuCommandBuffer invalid = fixture.Record();
        invalid.BeginRendering([new(new(target), P.GpuAttachmentLoadOperation.Clear)]);
        invalid.SetViewportAndScissor(new(float.NaN, 0, 1, 1, 0, 1), new(0, 0, 1, 1));
        invalid.EndRendering();
        P.GpuSemaphore completion = fixture.Semaphore();
        string operation = "";
        string[] kinds = [];
        try { fixture.Queue.Submit([invalid], completion, 1); }
        catch (P.GpuOperationException error)
        {
            operation = error.Operation;
            kinds = error.Diagnostics.Select(item => item.Kind.ToString()).ToArray();
        }
        bool unissued = false;
        try { fixture.Queue.IsComplete(completion, 1); }
        catch (ArgumentOutOfRangeException) { unissued = true; }
        P.GpuCommandBuffer valid = fixture.Record();
        fixture.Queue.Submit([valid], completion, 1);
        await fixture.Queue.WaitAsync(completion, 1);
        return new { operation, kinds, unissued, completed = fixture.Queue.IsComplete(completion, 1) };
    }

    private static async Task<object> RuntimeImportRetryAsync()
    {
        bool failed = false;
        try { using WebGpuBrowserRuntime missing = await WebGpuBrowserRuntime.LoadAsync("/missing-lumyte-webgpu.js"); }
        catch (JSException) { failed = true; }
        try
        {
            using WebGpuBrowserRuntime runtime = await WebGpuBrowserRuntime.LoadAsync("/lumyte-webgpu.js");
            using WebGpuBackend backend = await WebGpuBackend.CreateAsync(runtime);
            return new { failed, direct = backend.Capabilities.DirectRootData, retryError = "" };
        }
        catch (Exception error) { return new { failed, direct = false, retryError = error.ToString() }; }
    }

    private static async Task<object> ConcurrentMappingAsync()
    {
        using BrowserCaseFixture fixture = await BrowserCaseFixture.CreateAsync();
        P.GpuBufferHandle buffer = fixture.Buffer(16, P.GpuBufferUsage.MapWrite | P.GpuBufferUsage.CopySource);
        using (P.GpuMappedBufferRange first = await fixture.Backend.MapBufferAsync(buffer, P.GpuMapMode.Write, 0, 8))
        {
            string[] kinds = [];
            try { using P.GpuMappedBufferRange second = await fixture.Backend.MapBufferAsync(buffer, P.GpuMapMode.Write, 8, 8); }
            catch (P.GpuOperationException error) { kinds = error.Diagnostics.Select(item => item.Kind.ToString()).ToArray(); }
            if (kinds.Length == 0) { throw new InvalidOperationException("A concurrent mapping unexpectedly succeeded without browser diagnostics."); }
            first.Memory.Span.Fill(27);
        }
        P.GpuBufferHandle readback = fixture.Buffer(8, P.GpuBufferUsage.MapRead | P.GpuBufferUsage.CopyDestination);
        P.GpuCommandBuffer commands = fixture.Record();
        commands.CopyBuffer(new(buffer, 0, 8), new(readback));
        await fixture.SubmitAsync(commands);
        return new { actual = (await fixture.ReadAsync(readback, 8)).Select(item => (int)item).ToArray() };
    }

    private static async Task<object> PartialMappingAsync()
    {
        using BrowserCaseFixture fixture = await BrowserCaseFixture.CreateAsync();
        P.GpuBufferHandle upload = await fixture.UploadAsync(Enumerable.Range(0, 24).Select(item => (byte)item).ToArray());
        using (P.GpuMappedBufferRange mapped = await fixture.Backend.MapBufferAsync(upload, P.GpuMapMode.Write, 8, 8))
        {
            mapped.Memory.Span[3] = 99;
        }
        P.GpuBufferHandle readback = fixture.Buffer(24, P.GpuBufferUsage.MapRead | P.GpuBufferUsage.CopyDestination);
        P.GpuCommandBuffer commands = fixture.Record();
        commands.CopyBuffer(new(upload), new(readback));
        await fixture.SubmitAsync(commands);
        return new { actual = (await fixture.ReadAsync(readback, 24)).Select(item => (int)item).ToArray() };
    }

    private static async Task<object> MappingLeaseAsync()
    {
        using BrowserCaseFixture fixture = await BrowserCaseFixture.CreateAsync();
        P.GpuBufferHandle upload = fixture.Buffer(24, P.GpuBufferUsage.MapWrite | P.GpuBufferUsage.CopySource);
        P.GpuMappedBufferRange mapping = await fixture.Backend.MapBufferAsync(upload, P.GpuMapMode.Write, 8, 8);
        Memory<byte> escaped = mapping.Memory;
        byte[] expected = [1, 2, 3, 4, 5, 6, 7, 8];
        expected.CopyTo(escaped);
        mapping.Dispose();
        string expired = "none";
        try { escaped.Span.Fill(0); }
        catch (Exception error) { expired = error.GetType().Name; }
        P.GpuBufferHandle readback = fixture.Buffer(8, P.GpuBufferUsage.MapRead | P.GpuBufferUsage.CopyDestination);
        P.GpuCommandBuffer commands = fixture.Record();
        commands.CopyBuffer(new(upload, 8, 8), new(readback));
        await fixture.SubmitAsync(commands);
        using P.GpuMappedBufferRange read = await fixture.Backend.MapBufferAsync(readback, P.GpuMapMode.Read, 0, 8);
        string writable = "none";
        try { _ = read.Memory; }
        catch (Exception error) { writable = error.GetType().Name; }
        return new { actual = read.ReadOnlyMemory.ToArray().Select(item => (int)item).ToArray(), expired, writable };
    }

    private static async Task<object> InvalidMappingAsync()
    {
        using BrowserCaseFixture fixture = await BrowserCaseFixture.CreateAsync();
        P.GpuBufferHandle invalid = fixture.Buffer(16, P.GpuBufferUsage.MapRead | P.GpuBufferUsage.Storage);
        try
        {
            using P.GpuMappedBufferRange mapping = await fixture.Backend.MapBufferAsync(invalid, P.GpuMapMode.Read, 0, 16);
            return new { failed = false, kinds = Array.Empty<string>() };
        }
        catch (P.GpuOperationException error)
        {
            return new { failed = true, kinds = error.Diagnostics.Select(item => item.Kind.ToString()).ToArray() };
        }
    }

    private static async Task<object> InvalidPipelineAsync(bool invalidModule)
    {
        using BrowserCaseFixture fixture = await BrowserCaseFixture.CreateAsync();
        P.GpuComputePipelineHandle pipeline = fixture.Compute(invalidModule ? "invalid WGSL" : "@compute @workgroup_size(1) fn main() { }",
            entry: invalidModule ? "main" : "missing");
        P.GpuCommandBuffer commands = fixture.Record();
        commands.BeginCompute();
        commands.SetComputePipeline(pipeline);
        commands.Dispatch(1);
        commands.EndCompute();
        P.GpuSemaphore completion = fixture.Semaphore();
        fixture.Queue.Submit([commands], completion, 1);
        try
        {
            await fixture.Queue.WaitAsync(completion, 1);
            return new { failed = false, complete = fixture.Queue.IsComplete(completion, 1), value = 1UL, kinds = Array.Empty<string>() };
        }
        catch (P.GpuExecutionException error)
        {
            return new { failed = true, complete = fixture.Queue.IsComplete(completion, 1), value = error.FenceValue.Value,
                kinds = error.Diagnostics.Select(item => item.Kind.ToString()).ToArray() };
        }
    }

    private static async Task<object> InvalidBatchAsync()
    {
        using BrowserCaseFixture fixture = await BrowserCaseFixture.CreateAsync();
        P.GpuBufferHandle upload = await fixture.UploadAsync(MemoryMarshal.AsBytes(new uint[] { 41, 42 }.AsSpan()).ToArray());
        P.GpuBufferHandle gpu = fixture.Buffer(8, P.GpuBufferUsage.CopySource | P.GpuBufferUsage.CopyDestination);
        P.GpuBufferHandle readback = fixture.Buffer(8, P.GpuBufferUsage.MapRead | P.GpuBufferUsage.CopyDestination);
        P.GpuCommandBuffer valid = fixture.Record();
        P.GpuCommandBuffer invalid = fixture.Record();
        valid.CopyBuffer(new(upload), new(gpu));
        invalid.CopyBuffer(new(upload, 1, 4), new(gpu, 0, 4));
        P.GpuSemaphore completion = fixture.Semaphore();
        fixture.Queue.Submit([valid, invalid], completion, 1);
        P.GpuCommandBuffer read = fixture.Record();
        read.CopyBuffer(new(gpu), new(readback));
        fixture.Queue.Submit([read], completion, 2);
        await fixture.Queue.WaitAsync(completion, 2);
        string[] diagnostics = [];
        try { await fixture.Queue.WaitAsync(completion, 1); }
        catch (P.GpuExecutionException error) { diagnostics = error.Diagnostics.Select(item => item.Kind.ToString()).ToArray(); }
        byte[] actual = await fixture.ReadAsync(readback, 8);
        return new { actual = MemoryMarshal.Cast<byte, uint>(actual).ToArray(), diagnostics, laterComplete = fixture.Queue.IsComplete(completion, 2) };
    }

    private static async Task<object> TimelineAsync()
    {
        using BrowserCaseFixture fixture = await BrowserCaseFixture.CreateAsync();
        const ulong initial = 9_007_199_254_740_993;
        P.GpuSemaphore completion = fixture.Semaphore(initial);
        bool initialComplete = fixture.Queue.IsComplete(completion, initial);
        P.GpuCommandBuffer first = fixture.Record();
        fixture.Queue.Submit([first], completion, initial + 2);
        P.GpuCommandBuffer second = fixture.Record();
        fixture.Queue.Submit([second], completion, initial + 5);
        await fixture.Queue.WaitAsync(completion, initial + 5);
        await fixture.Queue.WaitAsync(completion, initial + 2);
        string gap = "none";
        try { fixture.Queue.IsComplete(completion, initial + 1); }
        catch (Exception error) { gap = error.GetType().Name; }
        string reused = "none";
        try { fixture.Queue.Submit([first], completion, initial + 6); }
        catch (Exception error) { reused = error.GetType().Name; }
        return new { initialComplete, firstComplete = fixture.Queue.IsComplete(completion, initial + 2), lastComplete = fixture.Queue.IsComplete(completion, initial + 5), gap, reused };
    }

    private static async Task<object> CancellationAsync()
    {
        using BrowserCaseFixture fixture = await BrowserCaseFixture.CreateAsync();
        P.GpuCommandBuffer commands = fixture.Record();
        P.GpuSemaphore completion = fixture.Semaphore();
        fixture.Queue.Submit([commands], completion, 1);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        bool cancelled = false;
        try { await fixture.Queue.WaitAsync(completion, 1, cancellation.Token); }
        catch (OperationCanceledException) { cancelled = true; }
        await fixture.Queue.WaitAsync(completion, 1);
        return new { cancelled, completed = fixture.Queue.IsComplete(completion, 1) };
    }
}
