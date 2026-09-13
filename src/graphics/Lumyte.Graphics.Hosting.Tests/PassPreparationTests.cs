using Lumyte.Graphics.Native;
using Lumyte.Graphics.Native.Hosting;
using Lumyte.Graphics.Native.RenderGraph;
using Lumyte.Graphics.Portable;
using Lumyte.Graphics.Portable.Hosting;
using Lumyte.Graphics.Portable.RenderGraph;
using Lumyte.Graphics.RenderGraph;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Lumyte.Graphics.Hosting.Tests;

public sealed class PassPreparationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PreparationAndBackendCreationShareTheSelectedScope(bool native)
    {
        var events = new List<string>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var prepared = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Dependency? borrowed = null;
        var expected = new InvalidOperationException("Backend probe completed.");
        using IHost host = new HostBuilder().ConfigureServices(services =>
        {
            services.AddScoped(_ => new Dependency(() => events.Add("scope disposed")));
            LumyteGraphicsBuilder builder = services.AddLumyteGraphics();
            if (native)
            {
                builder.AddNativeProvider("selected", (scope, _, _) =>
                {
                    Assert.Same(borrowed, scope.GetRequiredService<Dependency>());
                    events.Add("backend");
                    return ValueTask.FromException<INativeGpuBackend>(expected);
                }).AddNativePass(Contract.Instance, async (scope, cancellation) =>
                {
                    borrowed = scope.GetRequiredService<Dependency>();
                    events.Add("preparing"); entered.SetResult();
                    await prepared.Task.WaitAsync(cancellation);
                    events.Add("prepared");
                    return _ => new NativePass();
                });
            }
            else
            {
                builder.AddPortableProvider("selected", (scope, _, _) =>
                {
                    Assert.Same(borrowed, scope.GetRequiredService<Dependency>());
                    events.Add("backend");
                    return ValueTask.FromException<IPortableGpuBackend>(expected);
                }).AddPortablePass(Contract.Instance, async (scope, cancellation) =>
                {
                    borrowed = scope.GetRequiredService<Dependency>();
                    events.Add("preparing"); entered.SetResult();
                    await prepared.Task.WaitAsync(cancellation);
                    events.Add("prepared");
                    return _ => new PortablePass();
                });
            }
        }).Build();
        Task startup = host.StartAsync();
        await entered.Task;

        Assert.Equal(["preparing"], events);
        prepared.SetResult();
        InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(() => startup);

        Assert.Same(expected, actual);
        Assert.Equal(["preparing", "prepared", "backend", "scope disposed"], events);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DuplicateTypedPreparationRunsOnlyOnce(bool native)
    {
        int preparations = 0;
        var expected = new InvalidOperationException("Backend probe completed.");
        using IHost host = new HostBuilder().ConfigureServices(services =>
        {
            LumyteGraphicsBuilder builder = services.AddLumyteGraphics(options => options.Runtime = new()
            {
                RequiredPasses = [new(Contract.Instance.Id, Contract.Instance.Version)],
            });
            if (native)
            {
                Func<IServiceProvider, CancellationToken, ValueTask<NativeRenderPassFactory<int, int>>> prepare = (_, _) =>
                { preparations++; return ValueTask.FromResult<NativeRenderPassFactory<int, int>>(_ => new NativePass()); };
                builder.AddNativeProvider("selected", (_, _, _) => ValueTask.FromException<INativeGpuBackend>(expected))
                    .AddNativePass(Contract.Instance, prepare).AddNativePass(Contract.Instance, prepare);
            }
            else
            {
                Func<IServiceProvider, CancellationToken, ValueTask<PortableRenderPassFactory<int, int>>> prepare = (_, _) =>
                { preparations++; return ValueTask.FromResult<PortableRenderPassFactory<int, int>>(_ => new PortablePass()); };
                builder.AddPortableProvider("selected", (_, _, _) => ValueTask.FromException<IPortableGpuBackend>(expected))
                    .AddPortablePass(Contract.Instance, prepare).AddPortablePass(Contract.Instance, prepare);
            }
        }).Build();

        InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());

        Assert.Same(expected, actual);
        Assert.Equal(1, preparations);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DifferentPreparationForTheSameContractIsRejected(bool native)
    {
        LumyteGraphicsBuilder builder = new ServiceCollection().AddLumyteGraphics();
        if (native)
        {
            builder.AddNativePass(Contract.Instance, static (_, _) =>
                ValueTask.FromResult<NativeRenderPassFactory<int, int>>(_ => new NativePass()));

            ArgumentException error = Assert.Throws<ArgumentException>(() => builder.AddNativePass(Contract.Instance, static (_, _) =>
                ValueTask.FromResult<NativeRenderPassFactory<int, int>>(_ => new NativePass())));

            Assert.Contains("different Native preparation", error.Message);
        }
        else
        {
            builder.AddPortablePass(Contract.Instance, static (_, _) =>
                ValueTask.FromResult<PortableRenderPassFactory<int, int>>(_ => new PortablePass()));

            ArgumentException error = Assert.Throws<ArgumentException>(() => builder.AddPortablePass(Contract.Instance, static (_, _) =>
                ValueTask.FromResult<PortableRenderPassFactory<int, int>>(_ => new PortablePass())));

            Assert.Contains("different Portable preparation", error.Message);
        }
    }

    private sealed class Dependency(Action dispose) : IDisposable
    { public void Dispose() => dispose(); }
    private sealed class Contract : IGpuRenderPassContract<int, int>
    {
        internal static Contract Instance { get; } = new();
        public string Id => "test.prepared";
        public int Version => 1;
        public int Snapshot(int request) => request;
        public int Declare(GpuPassDeclarationContext context, int request) { context.Preserve(); return request; }
    }
    private sealed class NativePass : INativeRenderPass<int, int>
    {
        public ValueTask BuildAsync(NativePassBuildContext context, int request, int result, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class PortablePass : IPortableRenderPass<int, int>
    {
        public ValueTask BuildAsync(PortablePassBuildContext context, int request, int result, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
