# ADR 0037: Generic Host による Graphics の設定、登録と DI

## 状態

採用（段階 0 を実装、残りは目標設計）。起動時の Graphics 登録・設定を Generic Host へ集約し、描画 library は DI された session accessor から共通 runtime／render context を借用する。実装した API と後続機能を末尾で区別する。

## 依存 ADR

| ADR | 利用する契約 |
| --- | --- |
| [0001 Graphics](0001-graphics-api.md) | 共通契約と二系統、ロードと GPU 処理の境界 |
| [0015 Native Shader](0015-native-shader-package-api.md)・[0023 Portable Shader](0023-shader-design-and-api.md) | 準備済み package と shader program の初期化 |
| [0029 Resource Management](0029-resource-management-api.md) | GPU 資源の所有、使用保持と回収 |
| [0030 RenderGraph](0030-render-graph-api.md) | provider 選択、runtime、入力 bindings と presentation |
| [0031 Native provider](0031-native-render-graph-implementation.md)・[0032 Portable provider](0032-portable-render-graph-implementation.md) | backend／pass factory と runtime の所有 |
| [0033 機能 pass](0033-feature-render-passes.md) | 機能契約と二系統の登録 |
| [0034 標準機能](0034-render-pass-categories.md)・[0035 Model](0035-model-render-passes.md)・[0036 2D](0036-2d-render-passes.md) | 起動時の機能登録と実行時の描画 |

## 決定

provider、backend factory、pass factory、shader の準備方針と presentation 接続は、application の composition root で一度登録する。標準入口は `Host.CreateApplicationBuilder` と `IServiceCollection` とし、設定には Options、診断には Logging を使う。Generic Host は DI・設定・起動・終了をまとめる基盤として利用する。[.NET Generic Host](https://learn.microsoft.com/en-us/dotnet/core/extensions/generic-host)

Graphics の登録 builder は登録定義を集める。登録中と DI の同期 constructor／factory では、GPU device 作成、shader のファイル取得、非同期処理の同期待機をしない。Host の非同期起動で登録を確定し、選択する provider の runtime と必要な接続を準備する。

DI の生成は同期、GPU と platform の準備は非同期であるため、利用側には `IGpuGraphicsSessionAccessor` を注入する。利用側の非同期初期化で一度 GetAsync を待ち、完全に準備された session の runtime／render context を借用する。描画 loop では取得済みの参照を使い、毎 frame の service 解決、DI scope 作成や registry の再登録を行わない。

Host は起動・終了の所有者、runtime は GPU 実体の所有者とする。低レベル API、共通 RenderGraph の値型、pass の構築／記録 context は Microsoft.Extensions や IServiceProvider に依存させない。renderer が service locator で必要な backend や shader を探す方式にはしない。

## Package の境界

| Package／namespace | 責務 |
| --- | --- |
| `Lumyte.Graphics.Hosting` | 共通の登録 builder、Options、session accessor、Host の起動・終了への接続。共通 RenderGraph と Microsoft.Extensions に依存する |
| `Lumyte.Graphics.Native.Hosting` | DirectX 12／Vulkan の provider 登録、Native の backend／pass factory を Host の登録から構成する |
| `Lumyte.Graphics.Portable.Hosting` | WebGPU の provider 登録と Portable の backend／pass factory の構成 |
| `Lumyte.Graphics.Passes.Hosting` | 標準の Model／2D／画像処理を登録する extension。共通機能契約と各系統の実装を明示的に対応付ける |
| Platform／application の integration | window／canvas、event loop、UI thread と presentation factory。Resources の既存サービスを使う非同期準備の接続 |

各 extension は実装 package 側に置く。共通 Hosting が全 backend を直接参照したり、共通の描画 library が Hosting を参照したりする必要はない。登録済みの型と delegate を使い、assembly scanning、型名による動的ロード、runtime C# compilation を必須にしない。

## API

### 起動時の登録と設定

| API | 契約 |
| --- | --- |
| `services.AddLumyteGraphics()` | 共通 Hosting、Options と内部 hosted service、非所有 accessor を登録し、`LumyteGraphicsBuilder` を返す。IServiceCollection に対する extension |
| `LumyteGraphicsBuilder.Services` | 同じ IServiceCollection。機能 library が自分の CPU 依存を標準 DI へ登録するために使う。実行時の service provider ではない |
| `Configure(Action<GpuGraphicsOptions>)` | code から設定を追加する。登録処理を実行したり runtime を作ったりしない |
| `BindConfiguration(IConfigurationSection)` | Host の configuration を GpuGraphicsOptions へ bind する。appsettings、環境変数等の取得機構は Host に任せる |
| `GpuGraphicsOptions.Runtime` | ADR 0030 の GpuRenderRuntimeOptions。ProviderId、RequiredPasses、EnableValidation を設定する |
| `GpuGraphicsOptions.PlanCacheMaximumEntries` | Host が作る render context の論理構造 cache の上限。GPU の memory budget や frame 数ではない |
| `AddDirectX12()`／`AddVulkan()`／`AddWebGpu()` | 標準 ID の directx12／vulkan／webgpu と対応 backend の生成定義を登録する。device は作成しない |
| `AddModelRendering()`／`Add2DRendering()`／`AddImageProcessing()` | 共通の機能契約と対応する Native／Portable factory、必要な shader の準備定義を登録する。登録した標準機能を RequiredPasses の要件へ加える |
| `UsePresentation<TFactory>()` | IGpuGraphicsPresentationFactory を実装する型を登録し、runtime 単位の CPU scope から構成する。未指定なら headless とし、window／canvas を自動作成しない |

AddImageProcessing は ADR 0034 の Clear、Copy、Blit、Blur、Composite、ToneMap、Output を登録する。機能 package の登録は実装と入力 contract を一度対応付ける操作であり、ModelDrawList への項目登録、2D node の編集、graph の AddPass と区別する。

AddLumyteGraphics の繰返しで別の既定 session や hosted service を追加しない。標準 extension による同一の定義の再登録は一つへまとめる。同じ provider ID または系統・機能 ID・版に対する異なる定義は競合として報告し、最後の登録で黙って置き換えない。コード・型・delegate の登録順と、設定値の上書き順は別の規約として扱う。

### 独自 provider と pass の登録

| API | 契約 |
| --- | --- |
| `AddNativeProvider(id, createBackend)` | Native の backend factory を DI と接続して登録する。callback は IServiceProvider、GpuRenderRuntimeOptions、CancellationToken を受け、所有を移譲する INativeGpuBackend を ValueTask で返す |
| `AddPortableProvider(id, createBackend)` | 同じ構成で IPortableGpuBackend を返す Portable 用の登録 |
| `AddProvider(definition)`／`IGpuRenderProviderDefinition.CreateAsync(services, cancellationToken)` | integration が明示的な provider の非同期準備定義を登録し、runtime 単位の CPU scope から構成する。描画 library は呼ばない |
| `AddNativePasses(configure)`／`AddPortablePasses(configure)` | 段階 0 の構成入口。起動時の `(IServiceProvider, 専用 registry)` callback で CPU 依存を解決し、専用の型付き factory をまとめて登録する |
| `AddNativePass<TRequest, TResult>(contract, prepareFactory)` | 共通契約と Native の非同期準備 callback を登録する |
| `AddPortablePass<TRequest, TResult>(contract, prepareFactory)` | 共通契約と Portable の非同期準備 callback を登録する |
| 各 `prepareFactory(services, cancellationToken)` | 起動時に CPU 依存と準備済み shader package を揃え、ADR 0031／0032 の同期 pass factory を ValueTask で返す。services は当該 runtime の DI scope |
| `IGpuGraphicsPresentationFactory.CreateAsync(runtime, cancellationToken)` | 対応する window／canvas と runtime の presentation 接続を準備し、所有を移譲する IGpuGraphicsPresentationConnection を返す。表示先と event loop の寿命保持も接続へ渡す |
| `IGpuGraphicsPresentationConnection.Presentation` | ADR 0030 の IGpuGraphPresentation を借用で返す |
| `IGpuGraphicsPresentationConnection.DisposeAsync()` | 接続が保持する target と表示側の使用を終了し、接続を解放する。借用 runtime と外部所有の window は破棄しない |

IServiceProvider を使うのは Hosting の composition callback に限る。prepareFactory は Resources のサービス等から不変の package を受け取り、それを capture した型付き pass factory を返す。ここで file decoder や新しい Graphics loader を定義しない。通常の pass constructor は解決済みの依存を引数で受け、BuildAsync や recording callback で DI を検索しない。

標準 feature extension もこの登録へ展開する。共通機能の ID／版と型は登録時に分かるため、実装の存在確認に GPU や shader のロードは必要ない。各候補 provider の非同期準備は選択処理の中で行い、候補の失敗では生成済みの部分を回収する。明示選択した provider が失敗したときは、別系統へ黙って切り替えない。Auto の候補順・適合判断は共通 registry の規約に従う。

### DI から利用するサービス

| API | 契約 |
| --- | --- |
| `IGpuGraphicsSessionAccessor.GetAsync(cancellationToken)` | 既定 session の一度限りの非同期初期化を開始または共有し、完全に準備された GpuGraphicsSession を返す。caller は所有を得ない |
| `GpuGraphicsSession.Runtime` | Host が所有する IGpuRenderRuntime の借用参照。描画 library はこの共通型を使う |
| `GpuGraphicsSession.RenderContext` | presentation を設定した場合の GpuRenderContext。headless では null。これも借用参照 |
| 内部 hosted lifecycle service の `StartAsync`／`StopAsync`／`StoppedAsync` | 同じ初期化を開始・待機する／新規受理を閉じる／consumer の停止後に drain と破棄を待つ。application が別途登録・呼出しする API ではない |

accessor 自体は CPU 上の singleton として同期生成できる。GetAsync の待機取消しは、その caller の待機だけを取り消し、他の caller と共有する初期化を止めない。Host の起動取消し・停止は所有者の token で初期化を中止し、rollback する。初期化の失敗は同じ原因として待機者へ伝え、次の GetAsync で無制限に再試行しない。新しい設定／device で再開始する場合は、新しい Host または別の明示的な所有単位を作る。

Host の StartAsync と GetAsync は、どちらが先でも同じ初期化を開始して待つ。consumer の StartAsync が accessor を呼んだときに、後で呼ばれる Graphics hosted service の StartAsync だけを待つ実装にしない。これにより hosted service の登録順に依存する相互待機を避ける。DI constructor／同期 factory で GetAsync を同期待機する使い方は認めない。

presentation factory も、未起動の別 hosted service が window を作ることだけを待ってはならない。platform の明示的な非同期準備を呼ぶか、既に使用できる window を受け取る。UI thread と event loop は platform 側が所有し、Graphics が新しい UI thread 管理機構を重ねない。初期化 callback から同じ session の GetAsync を呼び直す循環も作らない。

## 設定と登録の確定

構成の流れは次のとおりとする。

1. Host を Build する前に services、Options と型付き登録定義を集める。
2. 初回 StartAsync／GetAsync で設定と登録定義の不変 snapshot を確定する。
3. provider の候補ごとに必要な runtime 単位の DI scope を作り、typed CPU 依存と shader 準備を接続する。
4. 確定済み定義から GpuRenderProviderRegistry と各系統の pass registry を構成し、選択する runtime を非同期に生成する。
5. 必要なら presentation 接続と render context を生成し、全成功後に session を公開する。

IServiceCollection の変更は Host.Build 前で完了する。生成後の provider／pass registry は不変の登録集合として使う。GetAsync や frame の開始を registration callback の再実行契機にしない。runtime の設定に IOptionsMonitor の変更をそのまま流して、実行中に device／provider／shader ABI を差し替えることはしない。フレームの camera や値の変更は ADR 0030 の bindings に渡す。

Options では provider ID、必須機能の登録、型の対応、cache 上限等の自分の構成契約を確認する。Options の bind／検査の仕組みは Microsoft.Extensions.Options を利用し、構成時に別の service provider を Build しない。[Options pattern](https://learn.microsoft.com/en-us/dotnet/core/extensions/options)

GPU の format、binding、pipeline、usage 等の合法性は native API／WebGPU／compiler が判断する。Hosting の ValidateOnStart 相当処理に native validator の複製を追加しない。configuration に任意の assembly 名や型名を入れて GPU 実装を動的生成する機構も設けない。

## Lifetime と所有

| 対象 | Lifetime／所有者 |
| --- | --- |
| 登録定義・Options の確定値 | Host 単位の不変 CPU 値 |
| session accessor と Hosting の所有者 | DI の singleton。GPU 初期化は別の一回限りの非同期処理 |
| 注入する CPU 依存 | singleton または runtime 単位の明示 scope。Scoped を root から解決しない |
| runtime | session を管理する Hosting 所有者の所有 |
| backend、GPU manager、pass instance | runtime の所有。pass の GPU cache を Host 全体の singleton として共有しない |
| presentation connection と render context | session の所有。window／event loop の所有は platform の契約に従う |
| ModelDrawList、2D store、plan、bindings | 利用 library の用途に応じた所有。frame ごとの DI resolve／scope 作成を要求しない |
| execution、GPU 使用保持 | ADR 0030 の完了・退役契約。DI scope の終了を GPU 完了とみなさない |

DI scope が所有するのは、その scope から解決した CPU 依存である。pass factory が直接 new または ActivatorUtilities で作って返した pass は runtime が所有し、同じ pass を DI container にも所有させない。注入された logger や CPU dependency を pass が Dispose しない。

同様に、生成済み runtime／context／connection を AddSingleton の factory から別々に返して DI の破棄対象へ重複登録しない。DI には Hosting の所有者と非所有 accessor を登録し、session はその所有者が返す借用値とする。利用 library は注入された session の runtime／context を Dispose せず、自分が作った graph scope、plan の保持、execution 等だけを終了する。一般の DI における scoped／singleton の所有原則に合わせる。[Dependency injection guidelines](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection/guidelines)

### 終了と失敗

Host の停止では、session の取得と新しい描画／acquire の受理を閉じる。同じ終了制御を借用済みの runtime.Resources と既存 scope にも接続し、新規 scope、import／upload、pin の取得を停止する。既に受理した操作の内部処理と、scope／pin／execution 等の Release・Dispose は可能なままにする。受理を閉じてから進行中の初期化・構築・提出を drain し、WaitIdle 後に別の upload が増える競合を防ぐ。この制御は共通 runtime の所有範囲に限定し、低レベル backend の呼出しを Host で監視しない。

内部所有者は標準の IHostedLifecycleService に接続する。Graphics の StopAsync は新規受理を閉じ、consumer の停止完了をそこで待たない。consumer の hosted service は StopAsync で frame loop と開始済み操作を終了し、自分の scope／pin／execution／frame を返す。Graphics の StoppedAsync が、全 consumer の StopAsync の後に GPU と接続を終了する。consumer singleton の最終 DI Dispose まで GPU 保持の返却を先延ばしにしない。全 StopAsync が StoppedAsync より前に処理される標準 Host の段階を使い、サービス同士の登録順で破棄を調整しない。[.NET Host の lifecycle 実装](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/Microsoft.Extensions.Hosting/src/Internal/Host.cs)

描画 loop は ApplicationStopping または自分の StopAsync による取消しに応答する。Graphics 側の受理停止も終了として処理し、同じ session を再取得して再試行しない。通常の描画 library は Host の lifecycle を実装する必要はなく、application 側の hosted service がその library の利用と所有保持を終了する。

受理済みの GPU work と staging の保持を drain し、context の未提出 target と保持を返す。presentation connection は必要な表示側の使用終了を待って閉じ、その後に runtime を DisposeAsync して pass、manager、backend を終了する。CPU 依存の DI scope は、それらを使う初期化・描画・cleanup が終わった後に破棄する。

presentation connection は platform の既存の寿命保持を使い、接続の終了まで window／canvas と必要な event loop が生存することを保証する。platform の StopAsync は閉鎖を要求しても、保持が残る間に物理的に破棄しない。後の Graphics の StoppedAsync を StopAsync 内で待つこともせず、保持返却後に物理終了を進める。寿命保持を提供できない platform では、application の同じ所有者が接続の終了と window の破棄を順に実行する。単に借用 window を保存するだけで、この契約を満たしたことにはしない。

StartAsync 中に失敗した場合は、公開前の部分生成物を逆順に回収する。先行 upload が既に受理されていれば、その GPU completion までは保持する。Host の起動失敗で StopAsync が呼ばれない場合も、所有者が公開前の rollback と最終 DisposeAsync を担当する。StoppedAsync と DisposeAsync は同じ終了処理を共有し、二重に native handle を破棄しない。

session 公開後に別のサービスの StartAsync が失敗した場合は、Host の所有者が DI の破棄より前に StopAsync を呼び、consumer の保持も返却する。consumer の StopAsync は未起動・途中起動・繰返しの終了にも対応し、開始済み操作の終了と保持返却を必ず行う。起動時の取消済み token を cleanup の token に使い回さない。標準 RunAsync は StartAsync の失敗時には StopAsync を経ず Host を破棄するため、この所有契約の例では StartAsync と失敗時の StopAsync を明示する。Graphics owner が外部所有の scope／pin の返却を代行する設計にはしない。[Host の実行 helper](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/Microsoft.Extensions.Hosting.Abstractions/src/HostingAbstractionsHostExtensions.cs)

shutdown の timeout や token の取消しは、GPU／表示側の完了を意味しない。未完了資源を pool に返したり借用の window を先に破棄したりせず、所有者の最終非同期 cleanup または device loss の終了処理へ引き継ぐ。異常を成功完了へ変換しない。停止順序をサービス登録順だけに依存させない。

## コード配置

以下は repository root 相対の配置である。Graphics の四つの Hosting project と共通 Hosting.Tests は段階 0 で新設した。既存の DevTools.Host を含む application への組込みは後続とする。登録を行う project だけが Microsoft.Extensions を参照する。

| 配置先 | 内容 |
| --- | --- |
| `src/graphics/Lumyte.Graphics.Hosting/Registration/`、`Options/` | AddLumyteGraphics、builder、登録定義、Options の bind と不変 snapshot |
| `src/graphics/Lumyte.Graphics.Hosting/Sessions/`、`Lifecycle/` | accessor／session、非同期初期化の共有、IHostedLifecycleService、rollback と終了の所有者 |
| `src/graphics/Lumyte.Graphics.Hosting/Presentation/` | presentation factory／connection の共通契約。window や canvas の具体型は置かない |
| `src/graphics/Lumyte.Graphics.Native.Hosting/Providers/`、`Passes/` | AddDirectX12／AddVulkan、Native provider／pass factory と runtime 単位の DI scope の接続 |
| `src/graphics/Lumyte.Graphics.Portable.Hosting/Providers/`、`Passes/` | AddWebGpu、Portable provider／pass factory と DI の接続 |
| `src/graphics/Lumyte.Graphics.Passes.Hosting/Models/`、`TwoD/`、`ImageProcessing/` | 標準機能の登録 extension、二系統の本体と準備済み shader package を対応付ける非同期準備 |
| `src/graphics/Lumyte.Graphics.Hosting.Tests/` | fake による設定・初期化共有・登録順・所有・停止・presentation と例外の統合試験。系統別の登録と GPU 実行は既存 backend の Integration suite から確認する |
| `src/devtools/Lumyte.DevTools.Host/Graphics/`、`src/devtools/Lumyte.DevTools.Host/Program.cs` | 既存 host への組込み先。前者は新設予定の接続領域で、presentation factory／window の寿命接続／frame loop と Resources サービスの組合せを置く。Program.cs は起動時の登録を呼ぶ |
| `src/devtools/Lumyte.DevTools.Host.Tests/Graphics/` | 既存 test project 内の新設予定領域。実際の composition root と platform 寿命の接続を検証する |
| `samples/graphics/Lumyte.Graphics.Hosting.Sample/` | 新設予定の application。設定による provider 選択、DI からの session 取得、同じ描画 plan の反復利用を示す |

他の application も自分の host project 内に platform／Resources の接続を置く。window と event loop 自体は `src/platform/` に保ち、その project へ Graphics Hosting の逆依存を追加しない。ファイル取得と decode を Graphics の Hosting 内に複製しない。共通 Passes、RenderGraph、低レベル backend の project へ DI extension を分散させない。

## 使用例

以下は目標 API である。Resources のサービスと platform 固有の依存は host 側で登録済みとする。この例は headless で、表示する application は UsePresentation を追加する。

```csharp
using Lumyte.Graphics.Hosting;
using Lumyte.Graphics.Native.Hosting;
using Lumyte.Graphics.Portable.Hosting;
using Lumyte.Graphics.Passes.Hosting;
using Lumyte.Graphics.RenderGraph;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
var graphics = builder.Services.AddLumyteGraphics()
    .BindConfiguration(builder.Configuration.GetSection("Graphics"))
    .AddDirectX12()
    .AddVulkan()
    .AddWebGpu()
    .AddModelRendering()
    .Add2DRendering()
    .AddImageProcessing();

builder.Services.AddSingleton<ModelView>();
builder.Services.AddHostedService<RenderWorker>();

var host = builder.Build();
await using var hostLifetime = (IAsyncDisposable)host;
try
{
    await host.StartAsync();
    await host.WaitForShutdownAsync(); // 正常終了では StopAsync も行う。
}
catch (Exception failure)
{
    try { await host.StopAsync(CancellationToken.None); }
    catch (Exception cleanupFailure)
    {
        throw new AggregateException(failure, cleanupFailure);
    }
    throw;
}
```

ModelView は Hosting を利用する application 側の接続例とし、初期化時に一度共通 runtime を取得する。RenderWorker は application が持つ frame loop で、ModelView の初期化・更新・提出を呼び、StopAsync で loop を終了して自分の GPU 保持も返す。registry や backend を ModelView に注入しない。Hosting に依存しない描画 library へは、ここで借用した共通 runtime を渡せる。

```csharp
public sealed class ModelView(IGpuGraphicsSessionAccessor graphics)
{
    private IGpuRenderRuntime? runtime;

    public async ValueTask InitializeAsync(CancellationToken cancellationToken)
    {
        var session = await graphics.GetAsync(cancellationToken);
        runtime = session.Runtime; // 借用。ModelView は Dispose しない。
    }

    public ValueTask<GpuRenderGraphExecution> RenderAsync(
        GpuRenderGraphPlan plan, GpuRenderGraphBindings inputs,
        CancellationToken cancellationToken)
        => runtime!.SubmitAsync(plan, inputs, cancellationToken);
}
```

表示する host では `graphics.UsePresentation<WindowPresentationFactory>()` を Host.Build より前に登録し、初期化で取得した session.RenderContext を保持して同じ plan を繰り返し提出する。WindowPresentationFactory は platform integration が実装する型であり、Graphics の共通 API に window 型を追加するものではない。

独自機能 library の非同期準備の形は次のとおりとする。この登録も Host.Build より前に行う。MyShaderAssets は host が Resources を利用して用意する CPU サービスの例であり、Graphics が定義する loader API ではない。

```csharp
graphics.AddNativePass(MyEffectContract.Instance, async (services, ct) =>
{
    var assets = services.GetRequiredService<MyShaderAssets>();
    var package = await assets.GetNativePackageAsync(ct);
    return runtimeServices => new NativeMyEffectPass(runtimeServices, package);
});
```

Portable 側も AddPortablePass で独立した package と本体を登録する。callback は起動時の factory 準備だけを行い、毎 frame の BuildAsync へサービス検索や I/O を持ち込まない。標準機能を使う application はこの factory を一件ずつ登録せず、AddModelRendering 等でまとめて設定する。

## 適合確認

xUnit の Host 統合試験では GPU を必要としない fake provider／connection を使い、以下を個別に確認する。

- 登録・Host.Build・DI 解決だけでは device 生成や shader I/O を開始しない。
- Host.StartAsync と複数 GetAsync が、順序によらず同じ初期化と完全な session を共有する。
- 一 caller の待機取消しが、他の caller の初期化を取り消さない。
- consumer が Graphics の hosted service より先に StartAsync しても、起動順による相互待機にならない。
- 同一定義の重複は一つとなり、競合定義と必須機能不足は自分の構成エラーになる。
- runtime ごとに生成する pass と注入する CPU 依存の scope が対応し、factory と frame ごとの再解決を混同しない。
- 初期化の途中失敗、Host 起動失敗、取消し、二重終了で資源の漏れと二重破棄を起こさない。
- 借用済み scope の新規 upload も停止し、受理停止後の返却は引き続き可能である。
- 登録順と並列停止の設定によらず、consumer の StopAsync で保持を返してから Graphics の StoppedAsync で破棄する。
- 未完了の GPU／presentation 使用より前に CPU dependency scope や借用 window を終了しない。
- 同じ injected session から plan と bindings を反復利用でき、登録定義が実行中に変わらない。

device と window が必要な試験は別の統合 suite とし、対応する platform 上で起動・停止、thread affinity と device loss を確認する。IServiceCollection と Microsoft.Extensions の一般的な動作を複製する試験は作らない。

## 採用範囲と未実装事項

Generic Host の登録 extension、Options、明示的な module／factory、非同期起動の共有、DI からの非所有 session 取得、runtime 単位の CPU scope と一つの所有者による終了を採用する。通常の library 利用では GpuRenderProviderRegistry 等を手動で組み立てず、低レベル integration・テストだけが直接 composition API を使う。

段階 0 として四つの Hosting package、LumyteGraphicsBuilder、GpuGraphicsOptions.Runtime、Configure／BindConfiguration、非所有 session accessor、UsePresentation、非同期初期化／rollback、runtime 単位の DI scope、IHostedLifecycleService の停止と終了を実装した。StartAsync と GetAsync は先に呼ばれた側から同じ初期化を共有する。停止開始で runtime と presentation acquire の新規受理を閉じ、consumer の StopAsync 後の StoppedAsync で GPU、presentation connection、runtime、CPU scope を順に終了する。

実装済みの個別登録は AddNativeProvider／AddPortableProvider の専用 backend factory と任意の registry 構成 callback、AddNativePasses／AddPortablePasses の `(IServiceProvider, 専用 registry)` callback である。callback は起動時に一度だけ scope 内の CPU 依存を解決する。AddImageProcessing は段階 0 の Clear／Copy／Output を両系統へ登録し、この三機能を RequiredPasses に加える。繰返しの標準機能登録は一度にまとめる。

AddDirectX12／AddVulkan／AddWebGpu の既定 backend 作成、個別 AddNativePass／AddPortablePass の非同期 package 準備、PlanCacheMaximumEntries、Model／2D と残る画像機能の登録は未実装である。platform 固有の window／canvas factory と thread affinity、実 device loss の試験も後続とする。UsePresentation の所有接続と headless 接続の確認を、OS の初回表示や resize 対応の完了とは扱わない。

最初の統合では一つの Host に一つの既定 session を提供する。複数の named session、実行中の provider 差替え、GPU device の透過的な再生成、DI container の hot reload は未採用とする。複数 runtime を明示所有する低層の利用は引き続き可能であり、frame ごとのスコープや新しい汎用 DI framework を追加する理由にはしない。
