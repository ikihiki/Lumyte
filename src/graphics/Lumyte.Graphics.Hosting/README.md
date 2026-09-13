# Graphics の Generic Host 接続

`Lumyte.Graphics.Hosting` は provider の登録定義と設定を起動時に固定し、非同期に作成した runtime を所有する。
描画 library は `IGpuGraphicsSessionAccessor` を受け取り、利用準備を一度待ってから共通 RenderGraph を提出する。
runtime や GPU object を DI の所有 object として重複登録しない。

```csharp
using Lumyte.Graphics.Hosting;
using Lumyte.Graphics.Native.Hosting;
using Lumyte.Graphics.Passes.Hosting;
using Lumyte.Graphics.Portable.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using IHost host = new HostBuilder()
    .ConfigureServices(services =>
    {
        services.AddLumyteGraphics(options =>
            options.Runtime = new() { ProviderId = selectedProviderId })
            .AddNativeProvider("dx12", createDirectX12Backend)
            .AddNativeProvider("vulkan", createVulkanBackend)
            .AddPortableProvider("webgpu", createWebGpuBackend)
            .AddImageProcessing();
    })
    .Build();

try
{
    await host.StartAsync(cancellationToken);
    var accessor = host.Services.GetRequiredService<IGpuGraphicsSessionAccessor>();
    var session = await accessor.GetAsync(cancellationToken);

    // renderer は共通 RenderGraph と Passes のみを参照するコンパイル済み library。
    await renderer.RenderAsync(session.Runtime, cancellationToken);
}
finally
{
    await host.StopAsync(CancellationToken.None);
}
```

各 `create...Backend` は `(IServiceProvider, GpuRenderRuntimeOptions, CancellationToken)` を受け取り、
`ValueTask<INativeGpuBackend>` または `ValueTask<IPortableGpuBackend>` を返す host 側の factory である。
登録時や DI の同期解決では backend を作らず、選ばれた provider が非同期起動時に呼び出す。GetAsync が先に呼ばれた場合も同じ初期化を開始する。
provider の ID と設定を照合してから、選択した定義だけを一つの DI scope で準備する。
`Auto` は登録順の先頭を選ぶ。未選択 provider の shader 準備や backend 作成は実行しない。

新しい pass の非同期準備は `AddNativePass`／`AddPortablePass` で登録する。
callback は `(IServiceProvider, CancellationToken)` を受け、準備済み package と CPU 依存を
capture した同期の `NativeRenderPassFactory`／`PortableRenderPassFactory` を返す。
同じ scope を backend factory も使う。`AddNativePasses`／`AddPortablePasses` は
非同期準備を必要としない registry 構成にも使える。描画ごとの DI 解決は行わない。

`GpuGraphicsOptions.PlanCacheMaximumEntries`（既定 64、正数）は presentation context が
単発 frame を Compile するときの構造 cache 上限を指定する。GPU memory budget とは別である。

任意の表示接続は `UsePresentation<TFactory>()` で登録し、`session.RenderContext` を借用する。
未指定なら headless であり、window／canvas を自動生成しない。consumer の hosted service は
StopAsync で描画 loop を止め、自分の scope／pin／execution を返す。Graphics はその後の
StoppedAsync で GPU と presentation を終了するため、サービスの登録順へ依存しない。

runtime に貸す CPU 依存は一つの DI scope に保持する。終了時は runtime の提出停止・GPU 使用終了・
所有資源の破棄を先に待ち、その後に scope を破棄する。GPU 終了処理が失敗した場合、CPU scope の寿命を
先に終わらせない。利用準備の待機に渡した cancellation token は、その観測だけを取り消す。

標準 pass の現在の登録は Clear、Texture Copy、Output である。shader のファイル取得や decode は
この integration の責務ではない。追加の外部 shader package を必要とする機能は、
型付き pass 準備 callback または `IGpuRenderProviderDefinition.CreateAsync` から
Lumyte.Resources へ取得を委譲できる。provider 定義の `Id` は不変の登録識別子である。
