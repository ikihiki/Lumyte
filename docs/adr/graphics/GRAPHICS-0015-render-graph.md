# ADR-GRAPHICS-0015: 共通command API上のレンダーグラフ

- 状態: 採用
- 日付: 2026-10-11

## 背景

[CommandBuffer](GRAPHICS-0007-command-buffers-and-submission.md) により、Wgpu／Vulkan／Browser は共通の copy、render／compute pass、barrier を記録できる。複数 pass で resource を共有すると、実行順と scope、texture layout の追跡を利用者が重複実装する必要がある。

既存の明示的な submit と GPU 完了待機を維持し、一時リソース、出力指定と pass culling、shader data buffer、外部利用の最終状態、既存診断システムとの連携を含む実用版を採用する。2026-10-11 にこの範囲への合意を受け、状態を採用へ変更した。

## 決定

### 責務と実装範囲

独立した `Lumyte.Graphics.RenderGraph` が `Lumyte.Graphics.Abstractions` のみに依存する。backend の低水準 contract を変更せず、外部所有 texture／型付き unmanaged buffer／shader data buffer と一時 texture／buffer の全 allocation を登録し、callback pass の resource 宣言から依存関係と barrier を生成する。

resource hazard の向きは pass の追加順で決める。read→write、write→read、write→write と texture state の変更に edge を作り、明示的な DependsOn を併合する。実行可能な pass は追加順で選び、循環を Compile 時に拒否する。read-only texture の state 変更も順序を必要とする。同じ state の read は scope を合併し、後続 write の barrier の Before に全 reader を含める。

write／state 変更から連続 read へ移る barrier の After には、次の write／state 変更までの全 reader scope を含める。最初の reader の stage だけに可視化すると、異なる stage の後続 reader を同期できないためである。

pass はアクセスごとに state と BarrierScope を宣言し、Access flags から read／write を推定する。backend と同じ stage／access、usage／state の制約を登録時に検証する。同一 allocation の alias import を拒否し、pass 内の重複アクセスは一つの合併 scope で宣言する。Undefined texture に対する初回 read／read-write は Compile 時に拒否する。部分 write の初期化範囲や StoreOp.Discard の内容追跡は行わない。

### 実用版の所有権とculling

`CreateTexture`／`CreateBuffer<T>` は descriptor と非所有 handle を登録する。GPU allocation は live pass の Record 時に同じ device で遅延生成し、compiled plan が所有する。view は context の CreateTextureView で生成して plan が保持できる。plan の Dispose は記録先 command の Completed／Faulted／Disposed 状態を要求し、GPU work を待たずに view、allocation の順で解放する。記録失敗時も command を破棄してから plan を解放する。

MarkOutput は最後の producer を保持する。ExportTexture／ExportBuffer／ExportShaderDataBuffer は出力を保持し、グラフ末尾に指定の final state／scope へ barrier を記録する。Present は借用した surface frame texture の export で指定し、実際の surface 対応は backend が検証する。Acquire／Submit／Present の semaphore と frame 操作は利用者が管理する。

外部所有 resource への write と KeepAlive が指定された pass は副作用を持つ root として保持する。root から read の直前 producer と明示的な DependsOn を遡り、不要な pass を削除する。実行順と barrier 用の WAR／WAW edge と、値が必要な producer の依存を区別する。純粋な上書きは前の値を必要としない。出力のない一時 resource の pass と allocation は生成しない。未初期化一時 buffer の read／read-write も拒否する。

### 診断システムへの接続

Graphics.RenderGraph は通信と Diagnostics 実装に依存せず、任意の IRenderGraphObserver へ immutable な compile snapshot と CPU 処理結果を渡す。Lumyte.Diagnostics.RenderGraph が既存の Lumyte.Diagnostics に接続し、Scoped な adapter を既存 execution point に登録する。Operation は Observe 権限で最新 graph の summary、pass、resource、dependency、barrier を index ごとに返す。snapshot は GPU allocation や callback を保持せず、診断の送信時に native resource を参照しない。

adapter は既存の IMeterFactory、ActivitySource、ILogger と lumyte.instance.id を使い、compile／record の CPU 時間、pass と culling の件数、barrier 件数、失敗を収集する。GPU timestamp と混同しない。HTTP／MagicOnion と診断UIは既存の catalog／telemetry の経路を使用する。

### 公開API

比較元は origin/main（`cc766f2`）。既存 backend API の変更はなく、以下の公開 API を追加する。handle は graph-local で非所有、利用者による生成／継承はできない。

```csharp
namespace Lumyte.Graphics.RenderGraph;

public sealed class RenderGraph
{
    public RenderGraph(IGraphicDevice? device = null, string name = "render-graph", IRenderGraphObserver? observer = null);
    public string Name { get; }
    public RenderGraphTexture ImportTexture(IGraphicsTexture texture, TextureState initialState, BarrierScope initialScope, string? name = null);
    public RenderGraphBuffer<T> ImportBuffer<T>(IGraphicsBuffer<T> buffer, BarrierScope initialScope, string? name = null) where T : unmanaged;
    public RenderGraphShaderDataBuffer<T> ImportShaderDataBuffer<T>(IGraphicsShaderDataBuffer<T> buffer, BarrierScope initialScope, string? name = null) where T : struct, IShaderData;
    public RenderGraphTexture CreateTexture(TextureDesc desc, string? name = null);
    public RenderGraphBuffer<T> CreateBuffer<T>(BufferDesc<T> desc, string? name = null) where T : unmanaged;
    public RenderGraph MarkOutput(RenderGraphResource resource);
    public RenderGraph ExportTexture(RenderGraphTexture texture, TextureState state, BarrierScope scope);
    public RenderGraph ExportBuffer<T>(RenderGraphBuffer<T> buffer, BarrierScope scope) where T : unmanaged;
    public RenderGraph ExportShaderDataBuffer<T>(RenderGraphShaderDataBuffer<T> buffer, BarrierScope scope) where T : struct, IShaderData;
    public RenderGraphPass AddPass(string name, Action<RenderGraphContext> record);
    public CompiledRenderGraph Compile();
}
public abstract class RenderGraphResource
{
    public string Name { get; }
    public bool IsTransient { get; }
}
public sealed class RenderGraphTexture : RenderGraphResource { }
public sealed class RenderGraphBuffer<T> : RenderGraphResource where T : unmanaged { }
public sealed class RenderGraphShaderDataBuffer<T> : RenderGraphResource where T : struct, IShaderData { }
public sealed class RenderGraphPass
{
    public string Name { get; }
    public RenderGraphPass KeepAlive();
    public RenderGraphPass UseTexture(RenderGraphTexture texture, TextureState state, BarrierScope scope);
    public RenderGraphPass UseBuffer<T>(RenderGraphBuffer<T> buffer, BarrierScope scope) where T : unmanaged;
    public RenderGraphPass UseShaderDataBuffer<T>(RenderGraphShaderDataBuffer<T> buffer, BarrierScope scope) where T : struct, IShaderData;
    public RenderGraphPass DependsOn(RenderGraphPass pass);
}
public sealed class RenderGraphContext
{
    public IGraphicsCommandBuffer Commands { get; }
    public IGraphicsTexture GetTexture(RenderGraphTexture texture);
    public IGraphicsBuffer<T> GetBuffer<T>(RenderGraphBuffer<T> buffer) where T : unmanaged;
    public IGraphicsShaderDataBuffer<T> GetShaderDataBuffer<T>(RenderGraphShaderDataBuffer<T> buffer) where T : struct, IShaderData;
    public IGraphicsTextureView CreateTextureView(RenderGraphTexture texture, TextureViewDesc? desc = null);
}
public sealed class CompiledRenderGraph : IDisposable
{
    public IReadOnlyList<string> PassNames { get; }
    public IReadOnlyList<string> CulledPassNames { get; }
    public RenderGraphSnapshot Snapshot { get; }
    public TextureState GetFinalTextureState(RenderGraphTexture texture);
    public BarrierScope GetFinalScope(RenderGraphResource resource);
    public IGraphicsTexture GetTexture(RenderGraphTexture texture);
    public IGraphicsBuffer<T> GetBuffer<T>(RenderGraphBuffer<T> buffer) where T : unmanaged;
    public IGraphicsShaderDataBuffer<T> GetShaderDataBuffer<T>(RenderGraphShaderDataBuffer<T> buffer) where T : struct, IShaderData;
    public void Record(IGraphicsCommandBuffer commands);
    public void Dispose();
}
public interface IRenderGraphObserver
{
    IDisposable? Begin(string operation);
    void Compiled(RenderGraphSnapshot snapshot, double durationMilliseconds);
    void Recorded(RenderGraphSnapshot snapshot, double durationMilliseconds);
    void Failed(string operation, Exception exception);
}
public sealed class RenderGraphSnapshot
{
    public string Name { get; }
    public IReadOnlyList<RenderGraphPassInfo> Passes { get; }
    public IReadOnlyList<RenderGraphResourceInfo> Resources { get; }
    public IReadOnlyList<RenderGraphDependencyInfo> Dependencies { get; }
    public IReadOnlyList<RenderGraphBarrierInfo> Barriers { get; }
}
public sealed record RenderGraphPassInfo(string Name, int ExecutionIndex, bool HasSideEffects);
public sealed record RenderGraphResourceInfo(string Name, string Kind, bool Transient, bool Output, int FirstPass, int LastPass);
public sealed record RenderGraphDependencyInfo(int Before, int After);
public sealed record RenderGraphBarrierInfo(int PassIndex, int ResourceIndex, TextureState BeforeState, TextureState AfterState, BarrierScope Before, BarrierScope After);
```

Create は constructor の device を必要とし、同じ device の commands に Record する。import は所有権を移さず、初期 state／scope と既存 buffer 内容の初期化は利用者が指定する。二重 import、別 graph の handle、pass 内の同一 resource の重複 use、不整合な usage／access／stage は ArgumentException。graph／pass／resource 名は 256 文字以下である。

Compile は GPU work を行わず、循環と live resource の初回未初期化 read を InvalidOperationException で拒否する。context は宣言した resource だけを返し、callback 後は無効。CreateTextureView の戻り値は借用で、利用者は Dispose しない。plan の GetTexture／GetBuffer／GetShaderDataBuffer は成功した Record 後の明示出力だけを返し、GPU 完了を意味しない。GetFinalTextureState／GetFinalScope は compile 時点に存在した resource の正常実行後の予定値であり GPU query ではない。

Record は一度だけで、Recording 状態の command buffer を必要とし、active encoder がないことは backend が検証する。callback が失敗しても plan は消費される。observer は例外を投げず、graph を変更しない。snapshot の pass index は追加順、ExecutionIndex は実行順（culled は -1）、dependency の index は実行順。export barrier の PassIndex は live pass 数、使用されない resource の FirstPass／LastPass は -1 とする。

診断 adapter の公開 API は次のとおり。生成 operation の出力 DTO は scalar のみで、summary、pass、resource、dependency、barrier の情報を保持する。

```csharp
namespace Lumyte.Diagnostics.RenderGraph;

public static class RenderGraphDiagnosticsExtensions
{
    public static IServiceCollection AddRenderGraphDiagnostics<TPoint>(this IServiceCollection services, string moduleId = "graphics.render-graph") where TPoint : class;
}
public sealed partial class RenderGraphDiagnostics : IRenderGraphObserver, IDisposable
{
    public const string TelemetryName = "Lumyte.Graphics.RenderGraph";
    public RenderGraphDiagnostics(IMeterFactory meters, ILoggerFactory loggers, IGameExecutionIdentity identity);
    // IRenderGraphObserver の実装。通常は RenderGraph constructor に渡す。
    public IDisposable? Begin(string operation);
    public void Compiled(RenderGraphSnapshot snapshot, double durationMilliseconds);
    public void Recorded(RenderGraphSnapshot snapshot, double durationMilliseconds);
    public void Failed(string operation, Exception exception);
    public void Dispose();
}
public sealed record RenderGraphBarrierDiagnostic(long PassIndex, long ResourceIndex, string BeforeState, string AfterState, long BeforeStages, long BeforeAccess, long AfterStages, long AfterAccess);
public sealed record RenderGraphDependencyDiagnostic(long Before, long After);
public sealed record RenderGraphPassDiagnostic(string Name, long ExecutionIndex, bool HasSideEffects);
public sealed record RenderGraphResourceDiagnostic(string Name, string Kind, bool Transient, bool Output, long FirstPass, long LastPass);
public sealed record RenderGraphSummary(string Name, string Status, long LivePasses, long CulledPasses, long Resources, long Dependencies, long Barriers, double CompileMilliseconds, double RecordMilliseconds);
```

登録には AddLumyteDiagnostics と既存 execution point が必要で、adapter は game scope で共有する。`get-summary` の返した revision を `get-pass`／`get-resource`／`get-dependency`／`get-barrier` の ExpectedRevision に渡す。更新後は conflict、不正 index は既存 schema による invalid-arguments／not-found、未 compile は not-ready となる。最新 snapshot 一つだけを保持し、履歴を蓄積しない。

### 使用例

一時 color attachment を clear し、一時 texture へ copy して外部 shader の sampled input として export する。device は同一 device で、encoder は callback 内で End する。

```csharp
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.RenderGraph;

static async Task ClearAndCopyAsync(IGraphicDevice device)
{
    var graph = new RenderGraph(device, "clear-and-copy");
    RenderGraphTexture color = graph.CreateTexture(new TextureDesc
    {
        Width = 64,
        Height = 64,
        Format = TextureFormat.Rgba8Unorm,
        Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource,
    }, "color");
    RenderGraphTexture copied = graph.CreateTexture(new TextureDesc
    {
        Width = 64,
        Height = 64,
        Format = TextureFormat.Rgba8Unorm,
        Usage = TextureUsage.CopyDestination | TextureUsage.Sampled,
    }, "copied");

    graph.AddPass("clear", context =>
    {
        IRenderEncoder encoder = context.Commands.BeginRenderPass(new RenderPassDesc
        {
            ColorAttachments = new[]
            {
                new RenderColorAttachmentDesc
                {
                    View = context.CreateTextureView(color),
                    LoadOp = AttachmentLoadOp.Clear,
                    StoreOp = AttachmentStoreOp.Store,
                    ClearValue = new ClearColor(0, 0, 0, 1),
                },
            },
        });
        encoder.End();
    }).UseTexture(color, TextureState.ColorAttachment,
        new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorWrite));

    graph.AddPass("copy", context => context.Commands.CopyTexture(
        new TextureCopyRegion { Texture = context.GetTexture(color), Width = 64, Height = 64 },
        new TextureCopyRegion { Texture = context.GetTexture(copied), Width = 64, Height = 64 }))
        .UseTexture(color, TextureState.CopySource,
            new BarrierScope(PipelineStage.Copy, ResourceAccess.CopyRead))
        .UseTexture(copied, TextureState.CopyDestination,
            new BarrierScope(PipelineStage.Copy, ResourceAccess.CopyWrite));

    graph.ExportTexture(copied, TextureState.Sampled,
        new BarrierScope(PipelineStage.FragmentShader, ResourceAccess.ShaderRead));
    using CompiledRenderGraph plan = graph.Compile();
    using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new CommandBufferDesc());
    plan.Record(commands);
    commands.Finish();
    using IGraphicsSubmission submission = device.Queue.Submit(new[] { commands });
    await submission.WaitAsync();
    IGraphicsTexture result = plan.GetTexture(copied); // 借用。plan を Dispose するまで有効。
    // 外部 pass で result を使うなら、その submission の完了まで plan を保持する。
}
```

export が copy を保持し、copy の read が clear を保持するため実行順は clear→copy。source の Undefined→ColorAttachment、ColorAttachment→CopySource と destination の Undefined→CopyDestination、末尾の CopyDestination→Sampled を自動記録する。出力に繋がらない一時 resource の pass は除去され、allocation を生成しない。外部への write がない callback の副作用は KeepAlive で指定する。独立した pass の順序は DependsOn で指定する。

plan-owned な一時出力を次の graph へ渡す場合、両方の GPU work の完了まで元 plan を保持する。正常実行後の状態だけを引き継ぐ。

```csharp
var nextGraph = new RenderGraph(device);
RenderGraphTexture nextInput = nextGraph.ImportTexture(
    plan.GetTexture(copied), plan.GetFinalTextureState(copied), plan.GetFinalScope(copied));
```

surface frame の借用 texture を import し、描画の末尾で Present を指定する。初期 state は取得した frame の実際の契約に合わせる。Acquire／Submit の wait・signal semaphore と Present は [GRAPHICS-0013](GRAPHICS-0013-surface-swapchain-presentation.md) に従い、graph は代行しない。

```csharp
RenderGraphTexture present = graph.ImportTexture(frameTexture, initialState, initialScope, "surface");
// draw pass で present を UseTexture(...ColorAttachment, ...ColorWrite) として宣言する。
graph.ExportTexture(present, TextureState.Present, default);
```

shader data は通常 buffer と別 handle で登録し、typed shader-data barrier の transfer／dependency metadata を保持する。callback 内の Argument Table と、shader data が参照する texture／buffer への実アクセスもそれぞれ宣言する。shader data allocation と pipeline／Argument Table の所有権は利用者に残る。

```csharp
RenderGraphShaderDataBuffer<MyShaderData> constants = graph.ImportShaderDataBuffer(
    shaderData, previousScope, "constants");
graph.AddPass("shade", context =>
{
    IGraphicsShaderDataBuffer<MyShaderData> data = context.GetShaderDataBuffer(constants);
    // data を既存 Argument Table API へ設定し、render／compute commands を記録する。
}).UseShaderDataBuffer(constants,
    new BarrierScope(PipelineStage.FragmentShader, ResourceAccess.ShaderRead)).KeepAlive();
```

診断登録例を示す。BeforeRender はアプリケーションの既存 owner-thread execution point の marker とする。pump の activation／Pump と collector の起動、通信 endpoint の登録は既存診断のライフサイクルを使う。

```csharp
using Lumyte.Diagnostics;
using Lumyte.Diagnostics.RenderGraph;
using Microsoft.Extensions.DependencyInjection;

services.AddLumyteDiagnostics(options =>
{
    options.Enabled = true;
    options.AllowedMeterNames = [RenderGraphDiagnostics.TelemetryName];
    options.AllowedActivitySourceNames = [RenderGraphDiagnostics.TelemetryName];
    options.AllowedLogCategoryPrefixes = [RenderGraphDiagnostics.TelemetryName];
});
services.AddDiagnosticExecutionPoint<BeforeRender>("graphics.before-render");
services.AddRenderGraphDiagnostics<BeforeRender>();
// 既に登録済みの point を利用するときは AddDiagnosticExecutionPoint を重ねない。
RenderGraphDiagnostics diagnostics = gameScope.ServiceProvider.GetRequiredService<RenderGraphDiagnostics>();
var graph = new RenderGraph(device, "main-frame", diagnostics);
```

### 所有権と失敗

imported resource／command／submission と外部で作成した view／pipeline／Argument Table は利用者が所有する。一時 allocation と context が作成した view は plan が所有し、必要な GPU 完了まで plan を保持する。全 mip／layer／buffer 要素を barrier 範囲とする。Compile は宣言を snapshot するが callback の capture 値は利用者が管理する。

Record の callback が失敗すると途中の recording は破棄する。Finish、submit、完了待機は呼び出し側が行う。compiled plan は一回だけ使い、正常実行後の final state／scope を次の graph に渡す。内部同期を追加せず、builder／plan の並列操作と GPU 同期は利用者が保証する。

### 検証方針

native binding 不要の test project を solution に追加し、既存 CI の solution tests で実行する。fake command buffer の記録から barrier の state、scope、range、callback 順序を検証する。RAW／WAR／WAW と layout 変更による循環、read scope の合併、export の外部 reader stage、同 state write、undefined read、snapshot、foreign handle、alias、one-shot、culling、遅延確保、view／allocation の解放順、shader data と失敗を確認する。既存診断 pump の登録／権限／revision／scope 分離と、標準 telemetry の metric／span／例外 log も検証する。共通 RenderGraphExercise の exact GPU readback を native の GpuFact と Browser verifier に組み込み、buffer upload→temporary→readback と texture clear→copy→readback を確認する。GPU tests は対応環境で明示的に有効化する。

### 他ライブラリとの比較と境界

先行する render graph は一時 resource、出力からの不要 pass 除去、アクセス宣言からの同期を共通して扱う。今回もこの三点を含め、バックエンド固有の実行最適化は分ける。

| 参照 | 主な機能 | 今回の判断 |
| --- | --- | --- |
| [Unreal Engine RDG](https://dev.epicgames.com/documentation/en-us/unreal-engine/render-dependency-graph-in-unreal-engine) | 一時 resource、依存解析、culling、alias、async compute、可視化 | 一時生成・culling・自動同期・診断を実装。alias と複数 queue は後続 |
| [Unity RenderGraph](https://docs.unity3d.com/Packages/com.unity.render-pipelines.core@17.0/manual/render-graph-system.html) | resource 宣言、culling、resource 管理、Render Graph Viewer | import／一時生成・出力指定・culling を実装。表示は既存診断の snapshot を使用 |
| [Daxa Task Graph](https://github.com/Ipotrick/Daxa) | task のアクセス宣言、自動 barrier、一時 resource、同期・実行管理 | 明示 access から barrier を生成。Submit と GPU 完了は既存 API のまま |
| [Filament FrameGraph](https://github.com/google/filament/tree/main/filament/src/fg) | virtual resource、compile、culling、resource lifetime | plan が一時 allocation を所有。GPU完了前の自動回収と pool は行わない |

単一 queue、whole allocation、one-shot Record で実用範囲を提供する。frame をまたぐ再利用、subresource 同期、pool／alias の効果は利用状況と測定を踏まえて別途判断する。

## 検討した代替案

- backend ごとに graph を作る: 共通 command API があるため、依存解決を重複実装する必要がない。
- shader binding を解析してアクセスを推測する: copy／attachment／indirect と副作用を網羅できないため、明示宣言を初期契約にする。
- import のみの最小版: 複数 pass の同期は扱えるが、一時 allocation と出力管理を利用者が繰り返すため、実用版では遅延確保と culling を含める。
- pool／alias と自動 lifetime 最適化を同時導入する: frames-in-flight と外部出力の寿命契約が増えるため、今回は plan-owned な独立 allocation と明示的な GPU 完了を使う。
- pass の versioned output により追加順に依存しない DAG を作る: 複数 write と部分更新の version 管理が必要になるため、初期版では既存 command の逐次的な resource 利用を追加順で表す。

## 結果と影響

共通 API 上で pass と barrier の接続を一箇所に記述でき、native backend に依存しない検証が可能になる。既存 command の利用者には破壊的変更を加えない。

allocation 全体の同期は保守的で、独立した mip／layer／byte 範囲の並列性は活用しない。実際の命令アクセスと宣言の一致、attachment discard／部分初期化の扱いは callback の責任になる。一時 allocation の pool／alias、subresource 追跡、複数 queue、GPU timestamp、再実行可能な compile cache と性能最適化は後続で設計する。shader data buffer は import を提供し、一時生成は含めない。
