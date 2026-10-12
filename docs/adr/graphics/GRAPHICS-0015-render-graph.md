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

別 graph の final scope を import した場合も、同じ state の read が既存 scope にない stage／access へ拡張されるときは barrier を生成する。先行 graph の write→read と後続 graph の read→read を同期の連鎖として扱い、追加 reader への可視性を確保する。command の順序と queue 間同期は利用者の責務である。

pass はアクセスごとに state と BarrierScope を宣言し、Access flags から read／write を推定する。backend と同じ stage／access、usage／state の制約を登録時に検証する。同一 allocation の alias import を拒否し、pass 内の重複アクセスは一つの合併 scope で宣言する。Undefined texture に対する初回 read／read-write は Compile 時に拒否する。部分 write の初期化範囲や StoreOp.Discard の内容追跡は行わない。

### 実用版の所有権とculling

`CreateTexture`／`CreateBuffer<T>` は descriptor と非所有 handle を登録する。GPU allocation は live pass の Record 時に同じ device で遅延生成し、compiled plan が所有する。view は context の CreateTextureView で生成して plan が保持できる。plan の Dispose は記録先 command の Completed／Faulted／Disposed 状態を要求し、GPU work を待たずに view、allocation の順で解放する。記録失敗時も command を破棄してから plan を解放する。

MarkOutput は最後の producer を保持する。ExportTexture／ExportBuffer／ExportShaderDataBuffer は出力を保持し、グラフ末尾に指定の final state／scope へ barrier を記録する。Present は借用した surface frame texture の export で指定し、実際の surface 対応は backend が検証する。Acquire／Submit／Present の semaphore と frame 操作は利用者が管理する。

外部所有 resource への write と KeepAlive が指定された pass は副作用を持つ root として保持する。root から read と通常 write の直前 producer、および明示的な DependsOn を遡り、不要な pass を削除する。write-only でも部分書き込みの可能性があるため、既定では前の値を保持する。UseTexture／UseBuffer／UseShaderDataBuffer の `discard: true` は全 allocation（texture は全 mip／layer）の上書きを保証する宣言で、write-only の場合だけ認める。この宣言がある場合のみ前の producer を値の依存から除く。WAR／WAW の実行順と barrier は引き続き必要である。出力のない一時 resource の pass と allocation は生成しない。未初期化一時 buffer の read／read-write も拒否する。

### 診断システムへの接続

Graphics.RenderGraph は通信と Diagnostics 実装に依存せず、任意の IRenderGraphObserver へ固定サイズの統計と CPU 処理結果を渡す。Lumyte.Diagnostics.RenderGraph が既存の Lumyte.Diagnostics に接続し、Scoped な adapter を既存 execution point に登録する。2026-10-11 のレビューで、PIX の継続計測と要求時キャプチャの区別を参考に、軽量集計と単発キャプチャを採用した。

observer 未指定または IsEnabled=false の場合は計時・通知・trace scope を生成せず、明示的な capture option もない場合は snapshot を生成しない。有効時も通常は compile／record の件数・CPU 時間の合計と最大値、pass／culling／barrier 件数と失敗件数だけを集計し、既定で前回出力から 1 秒以上経過した通知または get-summary の際に既存 IMeterFactory へ出力する。毎 pass のイベントや毎 compile の詳細 DTO、相関 Dictionary は生成しない。時間は CPU 時間であり GPU timestamp ではない。ActivitySource の詳細 span はキャプチャ対象だけに限定する。ILogger の代表エラーログは同じ間隔あたり最大一件とし、graph／run／frame の識別子を含める。失敗総数は全件を集計する。

Observe 権限の `request-capture` は次に Compile される指定名（空文字なら任意）の graph を一回だけ捕捉し、即座に captureId を返す。`get-capture` と `get-pass`／`get-resource`／`get-dependency`／`get-barrier` は captureId を指定する。詳細 snapshot は immutable で、後続 frame によって更新されず ExpectedRevision を要求しない。compile／record の状態だけが対象 run の進行に従って変化する。GraphId は builder、RunId は Compile ごと、FrameId は呼び出し側の任意の frame 番号（0 は未指定）で、全通知と失敗を同じ run に帰属させる。

保管するキャプチャの数、snapshot の推定保持 byte 数、合計 byte 数、有効期限と待機要求数を制限する。上限を超える snapshot は DTO 生成前に拒否し、too-large として返す。期限切れ・退避済みの ID は not-found を返し、別 snapshot に置き換えない。snapshot は GPU allocation や callback を保持せず、送信時に native resource を参照しない。手動検査では Compile の capture option で単発 snapshot を要求できる。HTTP／MagicOnion と診断 UI は既存の catalog／telemetry 経路を使用する。GPU timestamp、resource 内容、再生可能な capture、複数 graph をまとめた frame capture は今回の対象外とする。

### 公開API

比較元は origin/main（`cc766f2`）。既存 backend API の変更はなく、以下の公開 API を追加する。handle は graph-local で非所有、利用者による生成／継承はできない。

```diff
+namespace Lumyte.Graphics.RenderGraph;
+
+public sealed class RenderGraph
+{
+    // 一時 allocation を使う場合は device が必要。observer は省略可能。
+    public RenderGraph(IGraphicDevice? device = null, string name = "render-graph", IRenderGraphObserver? observer = null);
+    // name は空でない 256 文字以下。Id は builder の生存中不変。
+    // 拡張メソッドも同じdeviceを借用する。import専用graphではnull。
+    public IGraphicDevice? Device { get; }
+    public string Name { get; }
+    public Guid Id { get; }
+    // 外部所有 allocation を借用し、実際の初期 state／scope を指定する。
+    public RenderGraphTexture ImportTexture(IGraphicsTexture texture, TextureState initialState, BarrierScope initialScope, string? name = null);
+    public RenderGraphBuffer<T> ImportBuffer<T>(IGraphicsBuffer<T> buffer, BarrierScope initialScope, string? name = null) where T : unmanaged;
+    public RenderGraphShaderDataBuffer<T> ImportShaderDataBuffer<T>(IGraphicsShaderDataBuffer<T> buffer, BarrierScope initialScope, string? name = null) where T : struct, IShaderData;
+    // 未初期化の descriptor を宣言し、live な Record まで GPU allocation を作らない。
+    public RenderGraphTexture CreateTexture(TextureDesc desc, string? name = null);
+    public RenderGraphBuffer<T> CreateBuffer<T>(BufferDesc<T> desc, string? name = null) where T : unmanaged;
+    // 最後の producer と必要な先行値を保持する。
+    public RenderGraph MarkOutput(RenderGraphResource resource);
+    // 出力を保持し、外部アクセスへ同期する。Present は借用 surface image と空 scope。
+    public RenderGraph ExportTexture(RenderGraphTexture texture, TextureState state, BarrierScope scope);
+    public RenderGraph ExportBuffer<T>(RenderGraphBuffer<T> buffer, BarrierScope scope) where T : unmanaged;
+    public RenderGraph ExportShaderDataBuffer<T>(RenderGraphShaderDataBuffer<T> buffer, BarrierScope scope) where T : struct, IShaderData;
+    // callback は encoder を End して返す。Finish／Submit／Wait は呼び出し側の責務。
+    public RenderGraphPass AddPass(string name, Action<RenderGraphContext> record);
+    // frameId は非負、0 は未指定。capture 未指定なら observer の単発要求だけが snapshot を有効化する。
+    // 循環・live resource の未初期化 read は InvalidOperationException。GPU work は行わない。
+    public CompiledRenderGraph Compile(long frameId = 0, RenderGraphCaptureOptions? capture = null);
+}
+public abstract class RenderGraphResource
+{
+    // graph-local な非所有 handle。利用者による生成・継承はできない。
+    public string Name { get; }
+    public bool IsTransient { get; }
+}
+public sealed class RenderGraphTexture : RenderGraphResource { }
+public sealed class RenderGraphBuffer<T> : RenderGraphResource where T : unmanaged { }
+public sealed class RenderGraphShaderDataBuffer<T> : RenderGraphResource where T : struct, IShaderData { }
+public sealed class RenderGraphPass
+{
+    public string Name { get; }
+    // 宣言外の副作用を持つ pass を root として保持する。
+    public RenderGraphPass KeepAlive();
+    // discard の既定値 false は前の producer を保持する。true は全 allocation の上書き保証。
+    // true と read-only／read-write の組合せは ArgumentException。texture は全 mip／layer が対象。
+    public RenderGraphPass UseTexture(RenderGraphTexture texture, TextureState state, BarrierScope scope, bool discard = false);
+    public RenderGraphPass UseBuffer<T>(RenderGraphBuffer<T> buffer, BarrierScope scope, bool discard = false) where T : unmanaged;
+    public RenderGraphPass UseShaderDataBuffer<T>(RenderGraphShaderDataBuffer<T> buffer, BarrierScope scope, bool discard = false) where T : struct, IShaderData;
+    // 同じ graph の別 pass を先に実行する。resource hazard の追加順と矛盾すれば Compile が拒否する。
+    public RenderGraphPass DependsOn(RenderGraphPass pass);
+}
+public sealed class RenderGraphContext
+{
+    // callback 内だけ有効。Get 系はその pass が宣言した allocation を借用として返す。
+    public IGraphicsCommandBuffer Commands { get; }
+    public IGraphicsTexture GetTexture(RenderGraphTexture texture);
+    public IGraphicsBuffer<T> GetBuffer<T>(RenderGraphBuffer<T> buffer) where T : unmanaged;
+    public IGraphicsShaderDataBuffer<T> GetShaderDataBuffer<T>(RenderGraphShaderDataBuffer<T> buffer) where T : struct, IShaderData;
+    // 戻り値は plan 所有。利用者は直接 Dispose しない。
+    public IGraphicsTextureView CreateTextureView(RenderGraphTexture texture, TextureViewDesc? desc = null);
+}
+public sealed class CompiledRenderGraph : IDisposable
+{
+    // PassNames は実行順、CulledPassNames は除去された pass。
+    public IReadOnlyList<string> PassNames { get; }
+    public IReadOnlyList<string> CulledPassNames { get; }
+    // Run は compile／record／失敗で共通。Statistics は詳細 snapshot なしでも取得可能。
+    public RenderGraphRun Run { get; }
+    public RenderGraphStatistics Statistics { get; }
+    // 要求なし／推定 byte 上限超過では null。上限超過でも実行 plan は利用できる。
+    public RenderGraphSnapshot? Snapshot { get; }
+    // 正常実行後の予定値で GPU query ではない。次の graph へ引き継げる。
+    public TextureState GetFinalTextureState(RenderGraphTexture texture);
+    public BarrierScope GetFinalScope(RenderGraphResource resource);
+    // Record 成功後の明示出力だけを借用として返す。GPU 完了は保証しない。
+    public IGraphicsTexture GetTexture(RenderGraphTexture texture);
+    public IGraphicsBuffer<T> GetBuffer<T>(RenderGraphBuffer<T> buffer) where T : unmanaged;
+    public IGraphicsShaderDataBuffer<T> GetShaderDataBuffer<T>(RenderGraphShaderDataBuffer<T> buffer) where T : struct, IShaderData;
+    // 同一 device、Recording、active encoder なしが前提。失敗時も一回分を消費する。
+    public void Record(IGraphicsCommandBuffer commands);
+    // 記録先が Completed／Faulted／Disposed になるまで拒否。GPU を待たず view→allocation の順で解放。
+    public void Dispose();
+}
+// GraphId は builder、RunId は Compile ごと、FrameId は利用者指定（0 は未指定）。
+public readonly record struct RenderGraphRun(Guid GraphId, Guid RunId, string Name, long FrameId);
+// Resources／TransientResources は unused を含む宣言数。Dependencies／Barriers は live な計画の件数。
+public readonly record struct RenderGraphStatistics(int LivePasses, int CulledPasses, int Resources, int TransientResources, int Dependencies, int Barriers);
+public sealed record RenderGraphCaptureOptions
+{
+    // 正の推定保持 byte 上限。既定 4 MiB。GPU 内容と実行 plan の byte 数は含まない。
+    public long MaxBytes { get; init; } = 4 * 1024 * 1024;
+}
+public interface IRenderGraphObserver
+{
+    // false では計時・通知・trace scope を行わない。明示 capture は独立して指定可能。
+    bool IsEnabled { get; }
+    // 対象 run の単発要求。null は固定サイズの統計だけを受け取る。
+    RenderGraphCaptureOptions? RequestCapture(RenderGraphRun run);
+    // 呼び出しは owner thread。実装は例外を投げず graph を変更しない。
+    IDisposable? Begin(RenderGraphRun run, string operation);
+    // snapshot は未要求／上限超過で null。duration は CPU 時間（ms）。
+    void Compiled(RenderGraphRun run, RenderGraphStatistics statistics, RenderGraphSnapshot? snapshot, double durationMilliseconds);
+    void Recorded(RenderGraphRun run, double durationMilliseconds);
+    void Failed(RenderGraphRun run, string operation, Exception exception);
+}
+public sealed class RenderGraphSnapshot
+{
+    // native allocation と callback を保持しない immutable な診断情報。
+    public string Name { get; }
+    public long EstimatedBytes { get; }
+    public IReadOnlyList<RenderGraphPassInfo> Passes { get; }
+    public IReadOnlyList<RenderGraphResourceInfo> Resources { get; }
+    public IReadOnlyList<RenderGraphDependencyInfo> Dependencies { get; }
+    public IReadOnlyList<RenderGraphBarrierInfo> Barriers { get; }
+}
+// pass 配列は追加順、ExecutionIndex は実行順。culled は -1。
+public sealed record RenderGraphPassInfo(string Name, int ExecutionIndex, bool HasSideEffects);
+// FirstPass／LastPass は live 実行順。未使用は -1。
+public sealed record RenderGraphResourceInfo(string Name, string Kind, bool Transient, bool Output, int FirstPass, int LastPass);
+// Before／After は live 実行順。
+public sealed record RenderGraphDependencyInfo(int Before, int After);
+// 末尾 export の PassIndex は live pass 数。
+public sealed record RenderGraphBarrierInfo(int PassIndex, int ResourceIndex, TextureState BeforeState, TextureState AfterState, BarrierScope Before, BarrierScope After);
```

Create は constructor の device を必要とし、同じ device の commands に Record する。import は所有権を移さず、初期 state／scope と既存 buffer 内容の初期化は利用者が指定する。二重 import、別 graph の handle、pass 内の同一 resource の重複 use、不整合な usage／access／stage は ArgumentException。graph／pass／resource 名は 256 文字以下である。

Compile は GPU work を行わず、循環と live resource の初回未初期化 read を InvalidOperationException で拒否する。context は宣言した resource だけを返し、callback 後は無効。CreateTextureView の戻り値は借用で、利用者は Dispose しない。plan の GetTexture／GetBuffer／GetShaderDataBuffer は成功した Record 後の明示出力だけを返し、GPU 完了を意味しない。GetFinalTextureState／GetFinalScope は compile 時点に存在した resource の正常実行後の予定値であり GPU query ではない。

Record は一度だけで、Recording 状態の command buffer を必要とし、active encoder がないことは backend が検証する。callback が失敗しても plan は消費される。observer は例外を投げず、graph を変更しない。snapshot の pass index は追加順、ExecutionIndex は実行順（culled は -1）、dependency の index は実行順。export barrier の PassIndex は live pass 数、使用されない resource の FirstPass／LastPass は -1 とする。

診断 adapter の公開 API も同じ origin/main を比較元とする追加である。生成 operation の出力 DTO は scalar のみで、累積 summary、capture 状態と構造情報を返す。

```diff
+namespace Lumyte.Diagnostics.RenderGraph;
+
+public static class RenderGraphDiagnosticsExtensions
+{
+    // AddLumyteDiagnostics と既存 execution point が必要。adapter は game scope ごとに共有する。
+    // interval／lifetime／count／byte 上限は正数、MaxRetainedBytes は管理情報を収容できる値が必要。
+    public static IServiceCollection AddRenderGraphDiagnostics<TPoint>(this IServiceCollection services, string moduleId = "graphics.render-graph", Action<RenderGraphDiagnosticOptions>? configure = null) where TPoint : class;
+}
+public sealed class RenderGraphDiagnosticOptions
+{
+    // 既存 DiagnosticOptions.Enabled と両方が true の場合だけ有効。
+    public bool Enabled { get; set; } = true;
+    // telemetry 出力と代表エラーログの最短間隔。失敗総数は省略しない。
+    public TimeSpan MetricInterval { get; set; } = TimeSpan.FromSeconds(1);
+    // pending を含む保持数の上限。pending 要求自体は同時に一つ。
+    public int MaxCaptures { get; set; } = 2;
+    // snapshot の推定 byte 上限。管理情報と GPU 内容は含まない。
+    public long MaxSnapshotBytes { get; set; } = 4 * 1024 * 1024;
+    // 管理情報と compile 中の予約を含む全 capture の推定保持 byte 上限。
+    public long MaxRetainedBytes { get; set; } = 8 * 1024 * 1024;
+    // request 時刻からの期限。対象 graph を待つ時間も含む。
+    public TimeSpan Lifetime { get; set; } = TimeSpan.FromSeconds(60);
+}
+public sealed partial class RenderGraphDiagnostics : IRenderGraphObserver, IDisposable
+{
+    // 既存 collector の meter／activity source／logger allowlist に登録する名前。
+    public const string TelemetryName = "Lumyte.Graphics.RenderGraph";
+    public RenderGraphDiagnostics(IMeterFactory meters, ILoggerFactory loggers, IGameExecutionIdentity identity, DiagnosticOptions diagnostics, RenderGraphDiagnosticOptions options, TimeProvider clock);
+    // 通常は DI で解決し、RenderGraph constructor に渡す。すべて owner thread で呼ぶ。
+    public bool IsEnabled { get; }
+    public RenderGraphCaptureOptions? RequestCapture(RenderGraphRun run);
+    // capture 対象にだけ activity を作る。通常の集計では scope を作らない。
+    public IDisposable? Begin(RenderGraphRun run, string operation);
+    public void Compiled(RenderGraphRun run, RenderGraphStatistics statistics, RenderGraphSnapshot? snapshot, double durationMilliseconds);
+    public void Recorded(RenderGraphRun run, double durationMilliseconds);
+    public void Failed(RenderGraphRun run, string operation, Exception exception);
+    // 最終集計を一度出力し、保持 snapshot と activity source を解放する。meter は既存 factory が所有する。
+    public void Dispose();
+}
+// 累積集計。RetainedBytes は capture 管理情報と compile 中の予約を含む。
+public sealed record RenderGraphSummary(long Compiles, long Records, long Failures, double CompileMilliseconds, double CompileMaximumMilliseconds, double RecordMilliseconds, double RecordMaximumMilliseconds, long LivePasses, long CulledPasses, long Barriers, long Captures, bool CapturePending, long RetainedBytes);
+// request-capture の返り値。State は pending、CaptureId は以後の検査で共通。
+public sealed record RenderGraphCaptureRequest(string CaptureId, string State);
+// pending 時の GraphId／RunId は空、FrameId は -1。確定後は対象 run の識別子を返す。
+// Resources／TransientResources は unused を含む宣言数。EstimatedBytes は管理情報を含む。
+public sealed record RenderGraphCaptureDiagnostic(string CaptureId, string GraphId, string RunId, string Name, long FrameId, string State, long LivePasses, long CulledPasses, long Resources, long TransientResources, long Dependencies, long Barriers, double CompileMilliseconds, double RecordMilliseconds, long EstimatedBytes);
+// 以下の scalar DTO を同じ capture-id と index で取得する。構造は後続 frame によって変化しない。
+public sealed record RenderGraphBarrierDiagnostic(long PassIndex, long ResourceIndex, string BeforeState, string AfterState, long BeforeStages, long BeforeAccess, long AfterStages, long AfterAccess);
+public sealed record RenderGraphDependencyDiagnostic(long Before, long After);
+public sealed record RenderGraphPassDiagnostic(string Name, long ExecutionIndex, bool HasSideEffects);
+public sealed record RenderGraphResourceDiagnostic(string Name, string Kind, bool Transient, bool Output, long FirstPass, long LastPass);
```

登録には AddLumyteDiagnostics と既存 execution point が必要で、adapter は game scope で共有する。`get-summary` は capture に依存しない累積集計を返す。Observe 権限の `request-capture(graph-name)` は一回の capture を予約し、空文字の filter は次の任意 graph を選ぶ。pending 要求は一つで、重複要求は busy、診断無効時は disabled となる。

`get-capture(capture-id)` と `get-pass`／`get-resource`／`get-dependency`／`get-barrier` の `(capture-id, index)` は ExpectedRevision を要求しない。構造の取得前は not-ready、snapshot の上限超過は too-large、期限切れ・退避・未知 ID または範囲外 index は not-found、schema の型・範囲違反は invalid-arguments となる。capture 状態は pending→capturing→compiled→recorded と進み、compile-failed／record-failed／too-large も返す。構造 snapshot は immutable であり、record 成功は GPU 完了を意味しない。

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
        new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorWrite), discard: true);

    graph.AddPass("copy", context => context.Commands.CopyTexture(
        new TextureCopyRegion { Texture = context.GetTexture(color), Width = 64, Height = 64 },
        new TextureCopyRegion { Texture = context.GetTexture(copied), Width = 64, Height = 64 }))
        .UseTexture(color, TextureState.CopySource,
            new BarrierScope(PipelineStage.Copy, ResourceAccess.CopyRead))
        .UseTexture(copied, TextureState.CopyDestination,
            new BarrierScope(PipelineStage.Copy, ResourceAccess.CopyWrite), discard: true);

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

この例は単一 mip／layer の全体を clear／copy するため discard: true を指定できる。部分 buffer copy、単一 layer／mip の更新、attachment Load は既定の discard: false を使う。read を含む scope に discard: true を渡すと登録時に拒否する。

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
services.AddRenderGraphDiagnostics<BeforeRender>(configure: options =>
{
    options.Enabled = true;
    options.MetricInterval = TimeSpan.FromSeconds(1);
    options.MaxCaptures = 2;
    options.MaxSnapshotBytes = 4 * 1024 * 1024;
    options.MaxRetainedBytes = 8 * 1024 * 1024;
    options.Lifetime = TimeSpan.FromSeconds(60);
});
// 既に登録済みの point を利用するときは AddDiagnosticExecutionPoint を重ねない。
RenderGraphDiagnostics diagnostics = gameScope.ServiceProvider.GetRequiredService<RenderGraphDiagnostics>();
var graph = new RenderGraph(device, "main-frame", diagnostics);
```

通常の Compile は固定サイズの Statistics だけを用意する。frame 番号を渡すと、capture とログの run に関連付けられる。

```csharp
using CompiledRenderGraph plan = graph.Compile(frameId: 42);
RenderGraphRun run = plan.Run; // GraphId は graph.Id、RunId はこの Compile 固有。
RenderGraphStatistics counts = plan.Statistics;
// 単発 capture 要求がない通常実行では plan.Snapshot は null。
```

手動で構造を調べる場合は、その Compile にだけ上限付き capture を指定する。次の例は前の例とは別の Compile である。

```csharp
using CompiledRenderGraph captured = graph.Compile(
    frameId: 42, capture: new RenderGraphCaptureOptions { MaxBytes = 4 * 1024 * 1024 });
if (captured.Snapshot is RenderGraphSnapshot snapshot)
{
    // snapshot.Passes／Resources／Dependencies／Barriers を表示または保存する。
    // snapshot は plan の Dispose 後も native resource を参照せず読める。
}
// 上限超過時は Snapshot が null になり、compiled plan はそのまま Record できる。
```

remote からは既存診断 module の `request-capture` に `graph-name: "main-frame"` を送り、返った `capture-id` を保持する。owner thread で対象 graph を Compile すると、その一回だけ snapshot を作成する。`get-capture(capture-id)` で pending／compiled／recorded または失敗状態を取得し、同じ `capture-id` と `index` で `get-pass` などを列挙する。後続 frame が進んでも ID が指す構造は変化せず、ExpectedRevision は不要である。GraphId／RunId／FrameId は snapshot の由来を識別し、別 frame の通知はその capture の状態を更新しない。

### 所有権と失敗

imported resource／command／submission と外部で作成した view／pipeline／Argument Table は利用者が所有する。一時 allocation と context が作成した view は plan が所有し、必要な GPU 完了まで plan を保持する。全 mip／layer／buffer 要素を barrier 範囲とする。Compile は宣言を snapshot するが callback の capture 値は利用者が管理する。

Record の callback が失敗すると途中の recording は破棄する。Finish、submit、完了待機は呼び出し側が行う。compiled plan は一回だけ使い、正常実行後の final state／scope を次の graph に渡す。内部同期を追加せず、builder／plan の並列操作と GPU 同期は利用者が保証する。

### 検証方針

native binding 不要の test project を solution に追加し、既存 CI の solution tests で実行する。fake command buffer の記録から barrier の state、scope、range、callback 順序を検証する。RAW／WAR／WAW と layout 変更による循環、read scope の合併、export の外部 reader stage、同 state write、undefined read、snapshot、foreign handle、alias、one-shot、culling、遅延確保、view／allocation の解放順、shader data と失敗を確認する。既存診断 pump の登録／Observe 権限／scope 分離、後続 frame に対する capture ID の安定性、保持上限・期限・too-large、診断無効時と集計のみの経路、標準 telemetry の metric／capture span／相関付き例外 log も検証する。共通 RenderGraphExercise の exact GPU readback を native の GpuFact と Browser verifier に組み込み、buffer の前半・後半を別 pass で upload した結果、texture clear→copy→readback と複数 layer を別 pass で clear した結果を確認する。GPU tests は対応環境で明示的に有効化する。

### 他ライブラリとの比較と境界

先行する render graph は一時 resource、出力からの不要 pass 除去、アクセス宣言からの同期を共通して扱う。今回もこの三点を含め、バックエンド固有の実行最適化は分ける。

| 参照 | 主な機能 | 今回の判断 |
| --- | --- | --- |
| [Unreal Engine RDG](https://dev.epicgames.com/documentation/en-us/unreal-engine/render-dependency-graph-in-unreal-engine) | 一時 resource、依存解析、culling、alias、async compute、可視化 | 一時生成・culling・自動同期・診断を実装。alias と複数 queue は後続 |
| [Unity RenderGraph](https://docs.unity3d.com/Packages/com.unity.render-pipelines.core@17.0/manual/render-graph-system.html) | resource 宣言、culling、resource 管理、Render Graph Viewer | import／一時生成・出力指定・culling を実装。表示は既存診断の snapshot を使用 |
| [Daxa Task Graph](https://github.com/Ipotrick/Daxa) | task のアクセス宣言、自動 barrier、一時 resource、同期・実行管理 | 明示 access から barrier を生成。Submit と GPU 完了は既存 API のまま |
| [Filament FrameGraph](https://github.com/google/filament/tree/main/filament/src/fg) | virtual resource、compile、culling、resource lifetime | plan が一時 allocation を所有。GPU完了前の自動回収と pool は行わない |

単一 queue、whole allocation、one-shot Record で実用範囲を提供する。frame をまたぐ実行 plan の再利用、subresource 同期、pool／alias、複数 queue と Timeline semaphore の効果と寿命契約は利用状況と測定を踏まえて別途判断する。診断は CPU 集計と単一 run の構造 capture を扱い、GPU timestamp、resource 内容、再生可能な capture、複数 graph／frame の一括 capture は後続とする。

## 検討した代替案

- backend ごとに graph を作る: 共通 command API があるため、依存解決を重複実装する必要がない。
- shader binding を解析してアクセスを推測する: copy／attachment／indirect と副作用を網羅できないため、明示宣言を初期契約にする。
- import のみの最小版: 複数 pass の同期は扱えるが、一時 allocation と出力管理を利用者が繰り返すため、実用版では遅延確保と culling を含める。
- pool／alias と自動 lifetime 最適化を同時導入する: frames-in-flight と外部出力の寿命契約が増えるため、今回は plan-owned な独立 allocation と明示的な GPU 完了を使う。
- pass の versioned output により追加順に依存しない DAG を作る: 複数 write と部分更新の version 管理が必要になるため、初期版では既存 command の逐次的な resource 利用を追加順で表す。

## 結果と影響

共通 API 上で pass と barrier の接続を一箇所に記述でき、native backend に依存しない検証が可能になる。既存 command の利用者には破壊的変更を加えない。

allocation 全体の同期は保守的で、独立した mip／layer／byte 範囲の並列性は活用しない。実際の命令アクセスと宣言の一致、attachment discard／部分初期化の扱いは callback の責任になる。一時 allocation の pool／alias、subresource 追跡、複数 queue、GPU timestamp、再実行可能な compile cache と性能最適化は後続で設計する。shader data buffer は import を提供し、一時生成は含めない。
