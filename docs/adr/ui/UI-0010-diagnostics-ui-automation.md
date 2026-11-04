# ADR-UI-0010: Diagnostics経由のUI自動操作と非同期ジョブ

- 状態: 採用
- 日付: 2026-10-28
- 対応: 実装PR 9、前提: PR 8

## 背景

[UI-0009](UI-0009-ui-automation.md)のLocator・自動待機・通常入力を、別プロセスのテストや診断利用者から呼び出したい。ゲーム側へ新しい待受サーバーを作らず、既存Diagnosticsサービスの認証・カタログ・要求配送を使う。

DiagnosticsのOperationは所有スレッドのPumpで同期実行する。要素の出現や次frameをOperation内で待つと、待機条件を更新するframe自体が進まない。操作の受付と完了を分け、再配送による二重クリック、操作権の期限切れ、結果不明、大きなsnapshot/PNGの取得を明示する必要がある。

## 決定

### 配置と依存方向

ゲーム側の`src/Diagnostics/Lumyte.Diagnostics.UI.Automation`はDiagnosticsとUI.Automationへ依存し、HTTP/MagicOnionや診断サーバーの実装を参照しない。任意のScopedサブシステム`ui.automation`、schema version 1として既存execution pointへ登録する。

通信文書・結果モデルはBCLのみの`Lumyte.Diagnostics.UI.Automation.Contracts`、外部C# clientはHttpClient・Contracts・既存Diagnostics.Transportへ依存する`Lumyte.Diagnostics.UI.Automation.Client`へ置く。clientはゲームのWidget型やnative backendをロードせず、roleとKeyは規定の文字列で送る。UI本体・UI.AutomationはDiagnosticsへ依存しない。

```csharp
services.AddDiagnosticExecutionPoint<BeforeUiAutomation>("ui.before-automation");
services.AddUiAutomationDiagnostics<BeforeUiAutomation>();
services.AddDiagnosticAgent<BeforeUiAutomation>();
// transportと診断有効化は既存のcomposition rootで設定する。
var runtime = scope.ServiceProvider.GetRequiredService<UiAutomationDiagnosticsRuntime>();
var registration = runtime.Register("settings", surface, host,
    new UiAutomationTargetOptions { AllowInput = true, AllowScreenshots = true });
```

例のBeforeUiAutomationはアプリ側のexecution point markerで、RegisterはUI owner上で呼ぶ。診断無効時はJob・画像取得・周期snapshotを開始しない。

```text
外部client / 診断サービスのOperations画面
 → 診断サーバーの認証済みOperation呼出
 → 既存Agent（HTTPまたはMagicOnion）→ owner threadのPump
 → ui.automationがジョブ受付結果を即時返却
 → frame間でUiAutomationPage → host → 通常UiSurface.Dispatch
 → ジョブ状態照会 / 結果artifactのチャンク取得
```

HTTP clientは既存の`POST /diagnostics/v1/sessions/{id}/operations`へOperationInvocationを送る。ゲーム側に新しいsocket、任意コード実行、直接OnClick実行の入口を作らない。ゲーム接続のHTTP/MagicOnion選択は従来どおりDIで行い、Operatorはどちらのゲーム接続も同じ診断サービスAPIで操作する。

### 登録・所有スレッド・frame進行

アプリが診断対象を名前付きで明示登録し、借用するUiSurfaceとUI-0009のhostを渡す。名前は1〜128文字の小文字ASCII識別子（a-z、0-9、ハイフン、underscore、dot）で、同じruntime内で重複できない。登録ごとに一意なTargetIdを生成し、名前を再利用してもTargetIdは再利用しない。一つのsurfaceを同じruntimeへ重複登録できない。hostのsurfaceが登録時のSurfaceIdと異なれば失敗させる。

初期runtimeは一つのUI owner threadを扱う。複数surfaceは同じthreadなら登録でき、別threadは別execution point・runtime・診断scopeへ分ける。登録、通常Operation、Advance、登録解除は同じownerで行う。ネットワークthreadは検証済み要求の投入とdetachedな結果の送信だけを行う。

OperationのStartは引数・権限・所有者・容量を検証してQueuedジョブを予約し、JobIdを返す。Pump内ではUI.AutomationのTaskを待たず、Task.RunでUIへアクセスすることもしない。Pump終了後にアプリがAdvanceを呼び、期限・切断・取消を先に処理してから新しいジョブを開始する。

UI操作と待機は[UI-0009](UI-0009-ui-automation.md)のUiAutomationPage・host契約に従う。各Jobが独立したPageを作り同じhostを借用し、Pageの同時操作禁止を迂回して共有しない。入力JobはTargetごとに一つに限定する。

await後のUIアクセスはhost.RunOnUiThreadAsyncを使う。hostのUIキューとframe barrierをアプリの通常loopで処理し、NextFrameAsyncを実際のframeで完了させる。Advanceは完了したTaskを観測して結果を固定し、未完了Taskを同期的に待たない。

```text
入力取得 → Diagnostics.Pump → runtime.Advance（失効・取消を優先）
 → hostのUIキュー処理・許可された実入力配送 → layout/Record・描画
 → hostの新frame完了通知 → 次frame
```

診断の実行budgetとhostのUI処理budgetは別々に適用する。同期callbackの途中を強制中断せず、ownerが停止している間の期限処理の即時性は保証しない。

### 観測と操作権

Observeで対象一覧、snapshot、count、Expect、許可されたScreenshotを利用できる。Click/Focus/Pressと操作権の取得・更新・解放はOverrideInputを要求する。権限は公開Operationのdescriptorから既存サーバーとPumpが検証し、要求本文のActorIdやPermissionは受け取らない。

入力操作にはTargetごとに一つのcontrol leaseを要求し、所有者を信頼済みcontextの`(SessionId, ActorId)`へ結び付ける。leaseの既定寿命は5秒、要求値は1〜5秒とし、単調時計で判定する。clientの更新間隔は寿命の3分の1または1秒の小さい方とする。Observe権限だけでlease取得や入力Jobを開始できない。

登録時の入力許可・Screenshot許可は既定falseとし、ホストが明示して有効化する。初期は登録済みsurface全体を一つの公開範囲とする。非公開の部分木を自動で秘匿できるとはせず、公開できない内容を持つsurfaceは登録しない。Screenshotの対象許可もサーバーのObserve権限も両方を確認する。

leaseを解放・失効したら、そのleaseの入力JobをCancelRequestedにし、hostの入力leaseと未解放キー・pointerの後処理を待ってからReleasedにする。後処理中は別のcontrolを取得できない。所有者が同じ再取得要求は生存中の同じleaseを返し、それだけでは寿命を延ばさない。

Popupの親子Targetは[UI-0011](UI-0011-popup-windowing-and-fallback.md)の実装PR 10で追加する。rootとsubmenuまで一つのcontrol groupにまとめ、lease・入力Job一つ・Sequence high-waterを共有する。このPR単独では一Targetが一groupに相当し、Popupの表示方式へ依存するOperationを作らない。追加metadata、RegisterPopupとclientのPageForはUI-0011のAPI diffを正本とする。

IME/TextBoxは[UI-0012](UI-0012-ime-and-text-editing.md)のPR 11で追加する。text入力/検査許可、version 2 planのfill/expect-value、version 1との受付互換をそこで定義する。Diagnostics protocol 2、Operation IDと親子control groupの規則は継続する。

同PRの[UI-0015](UI-0015-custom-ime-candidate-presentation.md)で候補専用の閉じたversion 3 planを追加する。候補文字列には本文検査許可、候補操作には本文入力許可と同じgroupのcontrol leaseを要求し、現compositionを維持する専用入力leaseを使う。任意capabilityの転送とInputMayHaveBeenSent更新は下記のwrapper契約に従う。version 1/2の互換と送信後の自動再実行禁止を維持する。

### Operationと公開API

比較元はDiagnostics実装のmain `20f666ff8f64a63e65407936d32fb98cfa19c9d8`。UI側はUI-0009のAPIを前提とする。DiagnosticOperation、DiagnosticResult、DiagnosticMember、Traffic等は既存のDiagnostics型を使う。

```diff
+namespace Lumyte.Diagnostics.UI.Automation
+{
+    public sealed record UiAutomationDiagnosticsOptions
+    {
+        public int MaximumTargets { get; init; } = 16;
+        public int MaximumJobs { get; init; } = 64;
+        public int MaximumRunningJobs { get; init; } = 4;
+        public long MaximumArtifactBytes { get; init; } = 8 * 1024 * 1024;
+        public long MaximumStoredArtifactBytes { get; init; } = 32 * 1024 * 1024;
+        // 終了済みJob・artifactの絶対保持期間。読取では延長しない。
+        public TimeSpan Retention { get; init; } = TimeSpan.FromSeconds(120);
+    }
+    public sealed record UiAutomationTargetOptions
+    {
+        public bool AllowInput { get; init; } = false;
+        public bool AllowScreenshots { get; init; } = false;
+    }
+    public sealed class UiAutomationDiagnosticsRuntime : IDisposable
+    {
+        public UiAutomationDiagnosticsRuntime(TimeProvider clock, UiAutomationDiagnosticsOptions options);
+        // owner threadから呼ぶ。surface/hostを借用し、初期snapshotでIDを確認する。
+        public UiAutomationDiagnosticRegistration Register(string name, UiSurface surface,
+            IUiAutomationHost host, UiAutomationTargetOptions? options = null);
+        // Pumpの後で一frame一回。期限・取消→完了固定→新規開始の順。
+        public void Advance(DiagnosticFrame frame);
+        // thread-safeな通知だけを行い、UIへ直接触れない。実解除はAdvance。
+        public void NotifySessionClosed(Guid sessionId);
+        // 新規受付を止め、全Jobの取消を開始する。Advanceは継続する。
+        public void BeginStop();
+        public bool IsStopped { get; }
+        // owner上でIsStoppedになってから呼ぶ。Window/UI/hostは破棄しない。
+        public void Dispose();
+    }
+    public sealed class UiAutomationDiagnosticRegistration : IDisposable
+    {
+        public Guid TargetId { get; }
+        // Disposeで新規受付停止と取消を開始。完了はIsReleasedで確認する。
+        public bool IsReleased { get; }
+        public void Dispose();
+    }
+    public static class UiAutomationDiagnosticExtensions
+    {
+        // 既存pointへScoped runtimeとui.automation contributorを登録する。
+        public static IServiceCollection AddUiAutomationDiagnostics<TPoint>(
+            this IServiceCollection services, UiAutomationDiagnosticsOptions? options = null)
+            where TPoint : class;
+    }
+    public sealed partial class UiAutomationDiagnostics
+    {
+        public UiAutomationDiagnostics(UiAutomationDiagnosticsRuntime runtime);
+        [DiagnosticOperation(DiagnosticPermission.Observe, Id = "targets")]
+        private DiagnosticResult<UiAutomationTargetsReceipt> Targets();
+        [DiagnosticOperation(DiagnosticPermission.OverrideInput, Id = "control-acquire",
+            Traffic = DiagnosticOperationTraffic.Control)]
+        private DiagnosticResult<UiAutomationControlReceipt> Acquire(DiagnosticOperationContext context,
+            string targetId, [DiagnosticArgument(Minimum = 1000, Maximum = 5000)] long durationMs);
+        [DiagnosticOperation(DiagnosticPermission.OverrideInput, Id = "control-get",
+            Traffic = DiagnosticOperationTraffic.Control)]
+        private DiagnosticResult<UiAutomationControlReceipt> GetControl(
+            DiagnosticOperationContext context, string controlId);
+        [DiagnosticOperation(DiagnosticPermission.OverrideInput, Id = "control-renew",
+            Traffic = DiagnosticOperationTraffic.Control, RequiresRevision = true)]
+        private DiagnosticResult<UiAutomationControlReceipt> Renew(DiagnosticOperationContext context,
+            string controlId, [DiagnosticArgument(Minimum = 1000, Maximum = 5000)] long durationMs);
+        [DiagnosticOperation(DiagnosticPermission.OverrideInput, Id = "control-release",
+            Traffic = DiagnosticOperationTraffic.Control, RequiresRevision = true)]
+        private DiagnosticResult<UiAutomationControlReceipt> Release(
+            DiagnosticOperationContext context, string controlId);
+        // 入力可能なのはclick/focus/pressのみ。受付後はJobIdを即時に返す。
+        [DiagnosticOperation(DiagnosticPermission.OverrideInput, Id = "action-start")]
+        private DiagnosticResult<UiAutomationJobReceipt> StartAction(DiagnosticOperationContext context,
+            string targetId, string controlId, long sequence,
+            [DiagnosticArgument(MaxLength = 32768)] string planJson,
+            [DiagnosticArgument(Minimum = 100, Maximum = 10000)] long jobTimeoutMs);
+        // snapshot/count/expect/screenshotのみ。入力を含むkindは拒否する。
+        [DiagnosticOperation(DiagnosticPermission.Observe, Id = "observe-start")]
+        private DiagnosticResult<UiAutomationJobReceipt> StartObservation(DiagnosticOperationContext context,
+            string targetId, [DiagnosticArgument(MaxLength = 32768)] string planJson,
+            [DiagnosticArgument(Minimum = 100, Maximum = 10000)] long jobTimeoutMs);
+        [DiagnosticOperation(DiagnosticPermission.OverrideInput, Id = "action-get",
+            Traffic = DiagnosticOperationTraffic.Stream)]
+        private DiagnosticResult<UiAutomationJobReceipt> GetAction(DiagnosticOperationContext context, string jobId);
+        [DiagnosticOperation(DiagnosticPermission.Observe, Id = "observe-get",
+            Traffic = DiagnosticOperationTraffic.Stream)]
+        private DiagnosticResult<UiAutomationJobReceipt> GetObservation(DiagnosticOperationContext context, string jobId);
+        [DiagnosticOperation(DiagnosticPermission.OverrideInput, Id = "action-cancel",
+            Traffic = DiagnosticOperationTraffic.Control)]
+        private DiagnosticResult<UiAutomationJobReceipt> CancelAction(DiagnosticOperationContext context, string jobId);
+        [DiagnosticOperation(DiagnosticPermission.Observe, Id = "observe-cancel",
+            Traffic = DiagnosticOperationTraffic.Control)]
+        private DiagnosticResult<UiAutomationJobReceipt> CancelObservation(DiagnosticOperationContext context, string jobId);
+        // 完了結果を確認済みのclientが保持枠を返す。実行中Jobは解除できない。
+        [DiagnosticOperation(DiagnosticPermission.OverrideInput, Id = "action-forget",
+            Traffic = DiagnosticOperationTraffic.Control)]
+        private DiagnosticResult<UiAutomationJobReceipt> ForgetAction(DiagnosticOperationContext context, string jobId);
+        [DiagnosticOperation(DiagnosticPermission.Observe, Id = "observe-forget",
+            Traffic = DiagnosticOperationTraffic.Control)]
+        private DiagnosticResult<UiAutomationJobReceipt> ForgetObservation(DiagnosticOperationContext context, string jobId);
+        [DiagnosticOperation(DiagnosticPermission.Observe, Id = "artifact-read")]
+        private DiagnosticResult<UiAutomationArtifactReceipt> ReadArtifact(DiagnosticOperationContext context,
+            string jobId, string artifactId, [DiagnosticArgument(Minimum = 0)] long offset,
+            [DiagnosticArgument(Minimum = 1, Maximum = 65536)] long count);
+    }
+    public sealed record UiAutomationTargetsReceipt(
+        [property: DiagnosticMember(MaxLength = 32768)] string Json);
+    // 成功結果はscalarのみ。未設定ID/Codeは空文字列を使いnullableにしない。
+    public sealed record UiAutomationControlReceipt(string ControlId, string State,
+        long RemainingMilliseconds, long Revision, long NextSequence);
+    public sealed record UiAutomationJobReceipt(string JobId, string State, string Code,
+        bool InputMayHaveBeenSent, string ResultArtifactId, string TraceArtifactId, long Revision);
+    public sealed record UiAutomationArtifactReceipt(string ArtifactId, string ContentType,
+        long TotalBytes, string Sha256, long Offset, long Bytes,
+        [property: DiagnosticMember(MaxLength = 131072)] string Base64);
+}
```

上記のTargets結果とartifact内容は実行時にdetachedな値へ固定する。UIやhostのgetterをネットワークの送信threadから呼ばない。入力開始・観測開始のOperationを分け、ObserveがplanJson内のkindを変えて入力操作を実行できないようにする。

action/observationの照会・取消はそれぞれの権限とJobの所属種別を検証する。全Jobとartifactをcontextの所有者へ結び付け、別Actor/SessionのIDはnot-foundとする。OverrideInputだけの利用者も自分の入力Jobの状態は取得できるが、snapshot・PNG・詳細traceの読取にはObserveを要求する。

Renew/ReleaseはExpectedRevisionをcontrolのRevisionと比較する。ActionStartにはlayoutのRevisionを要求せず、対象の一意性・位置・遮蔽は実行時のLocatorとprobeで検証する。

### UI用JSON schemaとclient

Diagnosticsの外側envelopeは既存ProtocolVersion 2を使い、新しいDiagnosticMessageKindやRPC routeは追加しない。UI用のplanJsonは独立した`schemaVersion: 1`を持つ。例は次の通りで、outerのscalar wrapperは既存encoderが扱う。

```json
{
  "schemaVersion": 1,
  "kind": "click",
  "locator": [
    { "kind": "role", "role": "group", "name": "設定", "includeHidden": false },
    { "kind": "test-id", "value": "save" }
  ],
  "position": null
}
```

locatorはUI-0009の検索条件を最大8段保持する。roleはgroup/button、Keyとmodifierは既存UI/Inputが定める正規名の文字列とし、別のキー番号体系を作らない。TestId/nameは各256文字、planJsonは32768文字、JSONの深さは16まで。source-generated JSONの命名はcamelCaseとする。

kindごとのフィールド規則は次のとおりとする。

- click/focus/press/expect/countは1〜8段のlocatorを要求し、snapshot/screenshotのlocatorは空とする。
- clickだけがPositionを持ち、pressはKeyと最大4個の重複しないmodifierを持つ。
- expectのPredicateはvisible/hidden/enabled/focused/countとし、countの場合だけ非負のCountを要求する。
- Kindに関係しないフィールドは省略またはnullに限定する。

型、必須フィールド、未知フィールド、重複プロパティ、未知enum、非finite座標、負のoffsetを受信時に拒否する。関数名・delegate・C#式・JavaScript・privateフィールドへのpathを受け取らない。

Contractsはこの閉じたschemaのrequest、target、job、snapshotモデルとsource-generated JSON metadataを持つ。snapshotのID・Sequence等のUInt64はdecimal string、GuidはD形式、幾何はfiniteなJSON numberとする。PNGとtraceはbyte artifactとし、ネストしたUIドメインオブジェクトを直接シリアライズしない。

```diff
+namespace Lumyte.Diagnostics.UI.Automation.Contracts
+{
+    public sealed record UiRemoteLocatorStep(string Kind, string? Value = null,
+        string? Role = null, string? Name = null, bool IncludeHidden = false);
+    public sealed record UiRemotePoint(double X, double Y);
+    // kindごとの規則はschemaで検証。配列は受信・受付時にコピーする。
+    public sealed record UiRemotePlan(int SchemaVersion, string Kind,
+        UiRemoteLocatorStep[] Locator, string? Key = null, string[]? Modifiers = null,
+        UiRemotePoint? Position = null, string? Predicate = null, int? Count = null);
+    public sealed record UiRemoteTarget(Guid Id, string Name, bool AllowInput, bool AllowScreenshots);
+    public sealed record UiRemoteJobStatus(Guid JobId, string State, string Code,
+        bool InputMayHaveBeenSent, Guid? ResultArtifactId, Guid? TraceArtifactId, long Revision);
+    public sealed record UiRemoteRect(double X, double Y, double Width, double Height);
+    public sealed record UiRemoteNode(string Id, string? ParentId, string? TestId, string? Name,
+        string Role, UiRemoteRect Bounds, UiRemoteRect ClippedBounds, bool IsVisible,
+        bool IsEnabled, bool HitTestVisible, bool IsFocusable, bool IsFocused,
+        bool IsPressed, bool HasActivePress, string[] Actions);
+    public sealed record UiRemoteSnapshot(Guid SurfaceId, string Sequence, UiRemoteNode[] Nodes);
+    public static class UiRemoteSchema
+    {
+        public static UiRemotePlan ParsePlan(string json);
+        public static string SerializePlan(UiRemotePlan plan);
+        public static bool IsAction(UiRemotePlan plan);
+    }
+    // source-generated JsonSerializerContext。UI型を列挙・Reflection探索しない。
+    public partial class UiAutomationJsonContext : JsonSerializerContext { }

+}
+namespace Lumyte.Diagnostics.UI.Automation.Client
+{
+    public sealed record UiAutomationDiagnosticsClientOptions
+    {
+        // 100〜10000ms。RPCの受付期限とは独立したgame上のJob期限。
+        public int JobTimeoutMilliseconds { get; init; } = 5000;
+        // 100〜30000ms。Stream照会だけは常に1000ms以下へ制限する。
+        public int RpcTimeoutMilliseconds { get; init; } = 5000;
+        // 受付・照会・取得を含む高水準呼出の期限。game上のJob期限は延長しない。
+        public TimeSpan OperationTimeout { get; init; } = TimeSpan.FromSeconds(30);
+        // 通信断時の解放待機も無限に続けない。
+        public TimeSpan CleanupTimeout { get; init; } = TimeSpan.FromSeconds(5);
+        // 250ms以上。固定Sleepではなく非同期delayを使う。
+        public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(250);
+    }
+    public sealed class UiAutomationDiagnosticsClient : IAsyncDisposable
+    {
+        // Operator認証とBaseAddressを設定済みのHttpClientを借用。ゲーム用tokenは使わない。
+        public UiAutomationDiagnosticsClient(HttpClient http, Guid sessionId,
+            UiAutomationDiagnosticsClientOptions? options = null);
+        public Task<IReadOnlyList<UiRemoteTarget>> ListTargetsAsync(CancellationToken cancellationToken = default);
+        // このPageは観測専用。入力操作はcontrol.Pageで行う。
+        public UiRemotePage Page(Guid targetId);
+        // 失敗した高水準操作の既知JobIdから、保持中のstatus/traceを回収する。
+        public UiRemoteJob Job(Guid jobId, bool inputJob);
+        public Task<UiRemoteControl> AcquireControlAsync(Guid targetId, CancellationToken cancellationToken = default);
+        // 全controlの更新を止め、取消・解放を試みる。httpやゲーム接続は閉じない。
+        public ValueTask DisposeAsync();
+    }
+    public sealed class UiRemoteControl : IAsyncDisposable
+    {
+        public Guid Id { get; }
+        public UiRemotePage Page { get; }
+        public Task<UiRemoteJob> StartAsync(UiRemotePlan action,
+            CancellationToken cancellationToken = default);
+        // heartbeat停止→進行中更新の回収→Revision確認→release→解除確認。
+        public ValueTask DisposeAsync();
+    }
+    public sealed class UiRemotePage
+    {
+        public UiRemoteLocator GetByTestId(string testId);
+        public UiRemoteLocator GetByRole(string role, string? name = null, bool includeHidden = false);
+        public UiRemoteExpect Expect(UiRemoteLocator locator);
+        public Task<UiRemoteJob> StartObservationAsync(UiRemotePlan observation,
+            CancellationToken cancellationToken = default);
+        public Task<byte[]> ScreenshotAsync(CancellationToken cancellationToken = default);
+    }
+    public sealed class UiRemoteLocator
+    {
+        public UiRemoteLocator GetByTestId(string testId);
+        public UiRemoteLocator GetByRole(string role, string? name = null, bool includeHidden = false);
+        public Task<int> CountAsync(CancellationToken cancellationToken = default);
+        public Task ClickAsync(UiRemotePoint? position = null, CancellationToken cancellationToken = default);
+        public Task FocusAsync(CancellationToken cancellationToken = default);
+        public Task PressAsync(string key, IReadOnlyList<string>? modifiers = null,
+            CancellationToken cancellationToken = default);
+    }
+    public sealed class UiRemoteExpect
+    {
+        public Task ToBeVisibleAsync(CancellationToken cancellationToken = default);
+        public Task ToBeHiddenAsync(CancellationToken cancellationToken = default);
+        public Task ToBeEnabledAsync(CancellationToken cancellationToken = default);
+        public Task ToBeFocusedAsync(CancellationToken cancellationToken = default);
+        public Task ToHaveCountAsync(int count, CancellationToken cancellationToken = default);
+    }
+    public sealed class UiRemoteJob
+    {
+        public Guid Id { get; }
+        public Task<UiRemoteJobStatus> GetStatusAsync(CancellationToken cancellationToken = default);
+        public Task<UiRemoteJobStatus> WaitAsync(CancellationToken cancellationToken = default);
+        public Task CancelAsync(CancellationToken cancellationToken = default);
+        public Task<byte[]> DownloadArtifactAsync(Guid artifactId, CancellationToken cancellationToken = default);
+        // terminalなJobとartifactの保持枠を返す。入力の再実行は許可しない。
+        public Task ReleaseAsync(CancellationToken cancellationToken = default);
+    }
+    public sealed class UiRemoteAutomationException : Exception
+    {
+        public string Code { get; }
+        public Guid? JobId { get; }
+        public bool InputMayHaveBeenSent { get; }
+    }
+}
```

clientの入力メソッドはStart→JobId受領→状態照会をまとめ、待機とassertion自体はゲーム側UiAutomationPageで実行する。clientがsnapshotを周期取得してクリックを何度も送る方式にはしない。観測専用Pageの入力呼出は送信前に拒否する。同じcontrolで入力操作を並列実行した場合も拒否し、自動的に順序を変更しない。

高水準メソッドは成功確認と必要なartifact取得の後、ForgetでJob枠を返す。低水準Job handleは利用側がReleaseAsyncを呼び、失敗時のtraceは保持期限内に取得する。Forgetは終了済みの自分のJobだけを対象とし、actionのSequence high-water markは消さない。すでに解放済みの場合はnot-foundを返し、再受付へ変換しない。

Operationの引数・結果名は既存Generatorのkebab-case規則で、例えばtargetIdはtarget-id、JobIdはjob-idになる。外側のOperationInvocation/DiagnosticValueのJSON規約をplanJsonのcamelCase規約と混同しない。clientはui.automationのschema version・Operation ID・権限・field制約をカタログで照合してから呼ぶ。

```csharp
// httpは診断サービスのOperator認証を設定済み。
await using var client = new UiAutomationDiagnosticsClient(http, sessionId);
var target = (await client.ListTargetsAsync()).Single(x => x.Name == "settings");
await using var control = await client.AcquireControlAsync(target.Id);
await control.Page.GetByRole("button", name: "保存").ClickAsync();
await control.Page.Expect(control.Page.GetByTestId("saved-indicator")).ToBeVisibleAsync();
byte[] png = await control.Page.ScreenshotAsync(); // Observeと対象のScreenshot許可が必要
```

### ジョブ状態・期限・結果

状態はQueued→Running→Succeeded/Failed、またはQueued/Running→CancelRequested→Canceled/TimedOutとする。期限切れでも後処理が未完了ならCancelRequestedと期限切れのCodeを保持する。terminalへの移行は入力・hostの後処理後とし、取消要求の受理を操作停止の完了として返さない。CancelAsyncは取消受理まで、WaitAsyncはterminalまでを待つ。

statusのCodeはUI-0009の失敗種別と診断固有の理由を保持する。bridgeはJobごとにhostをwrapperで包み、UI入力leaseを取得した時点でInputMayHaveBeenSentをatomicにtrueへ更新する。これは入力の可能性を示す保守的な値で、実際のDown前に対象が変わった場合もtrueのままにする。UIやprivateな入力表を通信threadから参照したり、文字列traceを解析して送信有無を推測したりしない。

wrapperは元hostの任意capabilityの対応有無を維持して転送する。Screenshotと後続のrouting/text機能を隠したり、未対応interfaceを見かけ上実装したりしない。後続PRで入力leaseの取得口を追加する場合も、成功時のInputMayHaveBeenSent更新、同じleaseによる取消・清掃、元hostへの転送を一緒に追加する。取得待ちの取消だけで入力済みとせず、取得成功後の例外ではtrueを戻さない。

状態値は照会した時点のsnapshotである。最後のInputMayHaveBeenSentがfalseでも、未完了Jobがその後入力を開始していないとは断定しない。terminalな記録へ必要な結果を固定したら、Page・host wrapper・Task等の参照は保持せず、UIの寿命を結果保持期間へ延ばさない。

RPCの配送・受付期限とJobの実行期限は独立である。RPC完了後もJobは動作し、context.CancellationTokenをそのままJobへ保持しない。Job期限は受付時にruntimeのTimeProvider.GetTimestampで固定し、要求の再送や状態照会では延長しない。DiagnosticFrame.TimestampTicksやclientのUTCをこの絶対時刻の代用にしない。観測Jobも最大10秒で終了・取消を開始する。入力JobにはJob期限とcontrolの期限の両方を適用する。

callback・GPU完了等の停滞、入力送信後の失敗、画面変更の確認は[UI-0009](UI-0009-ui-automation.md)と[UI-0008](UI-0008-render-graph-and-resource-lifetime.md)の契約に従う。同期処理を強制中断せず、成立済みのアプリの副作用のrollbackを保証しない。

### 再配送と二重実行の防止

既存Diagnosticsは保持中の同じSessionId/RequestIdを重複排除する。しかし受付結果が失われ、新しいRequestIdでClickを再送しても安全であるという保証にはならない。そのため入力JobにはcontrolごとのSequenceを追加する。

1. 生存中controlのNextSequenceは1から開始する。正しい次のSequenceを受け付けると、Job枠の予約・payload指紋の固定・NextSequenceの加算を同じowner処理でcommitする。
2. 小さいSequenceで同じJobが保持されていれば、同じ正規化payloadと期限設定に対して元のJobIdを返す。別内容ならaction-id-conflictとする。RPCのRequestId・配送期限はこのpayload指紋へ含めない。
3. 小さいSequenceのJobを既に破棄していても、action-forgottenを返して実行しない。NextSequenceのhigh-water markはcontrol終了まで保持する。
4. 大きいSequenceはsequence-gap、無効controlはlease-goneとする。busy等の受付拒否ではSequenceを消費しない。

clientは同じ論理操作の再照会でSequence・payloadを変えない。入力を送った可能性がある場合に新Sequence・新control・新Sessionで自動的に操作をやり直さない。既知JobIdはcontrol終了後も保持期間内なら所有者が照会できるが、失効controlで入力開始を再受付しない。受付応答を失いJobIdが不明のままleaseが失効した場合、結果保持期限切れ、Session終了後などで結果を回収できなければoutcome-unknownとして停止し、非実行や成功と断定しない。ネットワーク全体の永続exactly-onceは主張しない。

既存RequestIdを使うRPCの再試行では、OperationInvocationの本文とTimeoutMillisecondsも同一に保つ。保持期間内の再送は元の結果へ合流し、clientが独自にexpiryを書き換えない。観測の再取得は入力を伴わないが、新しい観測結果になることを区別する。

### 切断・取消・終了

ゲームのAgent接続時にSessionIdを保存し、RunAsync終了時にcomposition rootがNotifySessionClosedへ通知する。次のAdvanceで当該SessionのcontrolとJobを取消する。破棄済みruntimeへの遅い通知は無視し、UIへ再アクセスしない。Operator側HTTPの切断だけでゲームSessionが終わるわけではないため、即時の切断通知が届くとは仮定しない。clientのheartbeat停止によりcontrolが失効し、観測Jobは固定のJob期限で回収する。

client取消時は同じJobのCancelをControl枠で送信し、入力の再送では解決しない。Cancel後は[UI-0009のキー操作と後処理](UI-0009-ui-automation.md#キー操作と後処理)を完了させてからcontrolを別所有者へ渡す。CancelはterminalなJobに対して冪等とする。

clientのcontrol.DisposeAsyncはheartbeatの進行中renewを回収してからRevisionを確認してReleaseする。結果不明ならControlGetで状態を確認し、無制限に更新・解放を繰り返さない。通信断で送信できない場合も短い終了期限で戻り、game側のlease失効を最終的な解除手段とする。ゲーム接続自体を閉じて他Actorの操作を停止しない。

Targetの登録解除は新規受付停止→関連Job取消→入力・host後処理→IsReleasedの順。UI/hostを先に破棄しない。runtime全体の終了もBeginStop後にframe/Advanceを継続し、IsStoppedを確認してからPump.Deactivate、Agent停止・scope破棄へ進む。切断通知は専用の有界・重複統合した制御経路で保持し、通常Job満杯を理由に失わない。

### 結果artifactと負荷上限

snapshot JSON、PNG、詳細traceはゲーム側で一度固定し、Job所有者・Target・Session・artifact ID・content type・byte数・SHA-256へ結び付ける。Observe権限でartifact-readを呼び、最大64 KiBの生byteをbase64で取得する。offsetはbyte位置で、UTF-16文字列の途中分割やPNG全体のinline返却はしない。

offsetは0から総byte数までを許可し、返すbyte数は要求countと残量の小さい方とする。終端offsetでは0 byteを返すため、最初の要求時点で総サイズを知らなくても最後の短いchunkを取得できる。countは常に1〜65536の要求値とし、内部でoffset+countのoverflowを起こさない。

clientは同一artifactのoffset、実際のbyte数、総サイズ、SHA-256を検証し、最終hash一致までPNG等を成功結果として返さない。再読は同じ固定byte列を返す。別Jobのartifact、範囲外、破棄済みは拒否する。巨大なJSONやPNGを黙って切り詰めない。

既定の負荷上限は次のとおりとする。

- Targetは16、保持中Jobは64、実行中Jobは4。同一Targetの入力JobとScreenshotはそれぞれ一つ。
- snapshotは10000ノード、artifactは一つ8 MiB、全体32 MiBまで。
- Jobとartifactは終了後120秒または明示Forgetまで保持し、読取で延命しない。

上限超過はbusy/result-too-largeで返す。生存中Jobや未解放の結果を容量確保のために追い出さず、新規受付を拒否する。成功結果を確認したclientはForgetして連続操作の保持枠を返す。

Job照会はStream、開始・artifact読取はStandard、更新・停止・解放はControlとし、DIAGNOSTICS-0003の枠・4 MiB符号化上限・fieldのMaxLengthを維持する。clientの状態pollは最大毎秒4回、一回のStream期限は1秒以下とする。artifact取得もbusyなら同じoffsetを後から読む。大きい結果の転送が停止用Controlを消費しないようにする。

artifact上限は保持するCPU結果の上限であり、すべてのGPU資源使用量の上限ではない。Screenshot hostはUI-0008/0009のGPU完了・取消後の退役と自身の有界capture枠を守り、Wait取消だけで資源枠を空いた扱いにしない。

### 対応・監査・実装範囲

初期は既存Operations画面と上記C# clientから利用できる。専用の診断画面やJavaScript clientは必須としない。Browser上のゲームは既存HTTP Agent経由で同じOperationを公開する。backgroundでframeが進まなければ待機がtimeoutとなり、通信だけでブラウザーの制限を解消しない。

Diagnosticsの既存の有効化・Operator認証・ゲームSession権限を引き継ぐ。開始、取消、完了、所有者、Target、Sequence、InputMayHaveBeenSentを既存Log/Traceへ有界に記録する。例外の内部stack、token、非公開オブジェクトを成功receiptへ載せない。詳細snapshot/trace/PNGの権限を入力操作の結果から迂回できないようにする。

外部操作の検証は既存の別プロセス診断検証へUI操作を接続し、同一プロセスのmockだけで対応済みとみなさない。

## 検討した代替案

- Operationをasync化して要素出現までPump内で待つ方式は、既存同期契約とframe進行を壊すため採用しない。
- ゲーム側に専用HTTPサーバーを作る方式は、既存の接続・認証・診断カタログを二重化するため採用しない。
- clientがClickを一定間隔で再送する方式は副作用を重複させるため、game側JobとSequenceで一つの論理操作を追跡する。
- PNGを一つのOperation結果へ載せる方式は既存のfield・符号化上限を超えるため、有界artifactを既存Operationで分割取得する。

## 結果と影響

既存Diagnosticsサービスから、同じLocator・通常入力・自動待機でUIを操作できる。通信実装をUIへ持ち込まず、停止・権限・再配送・結果不明を明示できる。一方、Job/control/artifactの保持と回収、clientのheartbeat、hostのframe進行を実装する必要がある。

## 検証方針

- HTTP/MagicOnionの実Agentと別プロセスOperator clientで、対象発見→control取得→Click→indicatorのExpect→PNG取得を往復する。
- owner thread以外からUIに触れないこと、待機Job中もframe・Controlが進むこと、callback例外を二重実行で解決しないことを確認する。
- Observeだけの入力開始、別Actor/SessionのJob・control・artifact、古いTargetId、kind偽装、未知JSON・enum・NaN・大きいpayloadを拒否する。
- 受付結果喪失、同一RequestId、異なるRequestIdで同一Sequence、内容差し替え、保持破棄後の古いSequence、Session変更で重複Clickしないことを確認する。
- 取消・lease期限・Agent切断・Operator切断・Target解除・停止時に入力後処理が終わるまで再取得できず、押下が残らないことを確認する。
- chunk境界、再読、hash不一致、期限切れ、容量不足、GPU待機取消、Standard/Stream満杯でもControlを受け付けることを確認する。
- source-generatedなJSON/診断配送とBrowser実行を確認し、未検証の環境や実GPUロストはfixture検証と区別してREADMEへ記録する。

## 参考資料

- [Diagnosticsの実行・通信契約](../diagnostics/DIAGNOSTICS-0001-diagnostics-transport.md)
- [操作ペイロード・Stream/ControlとProtocolVersion 2](../diagnostics/DIAGNOSTICS-0003-command-payloads-and-traffic.md)
- [診断基盤API](../../../src/Diagnostics/Lumyte.Diagnostics/README.md)
- [既存Agent](../../../src/Diagnostics/Lumyte.Diagnostics.Transport/README.md)
- [診断サーバー](../../../src/Diagnostics/Lumyte.Diagnostics.Server/README.md)

## 実装の補足と検証記録

ゲーム側、BCLのみのContracts、外部clientの三パッケージへ実装する。UInt64のsnapshot IDはsurface内のdecimal stringとして表し、外側のSurfaceIdと組み合わせる。artifactは返却前にbyte列を固定し、同じ内容をhash検査できる。JSON符号化も上限付きbufferを使い、上限超過を切り詰めて成功にしない。

切断通知はMaximumJobs + MaximumTargets件まで重複統合し、絶対保持期間内に上限を超えた場合は安全側に新規操作を停止して既存Jobを取消する。単にJob容量が満杯であるために切断通知を失うことはない。登録時と各JobのUI呼出でhostのsurface所属を確認し、terminal時にはJobが保持するhost/surface/Task/planへの参照を解放する。

終了済みcontrolの記録は同じTargetの次の取得、またはTarget解除まで保持する。既存controlがReleasedであることを確認してから置換し、not-foundとなった旧controlを再実行へ変換しない。clientは解放確認時のnot-foundを退役済みとして扱い、入力を再受付しない。

Browserを含むDI登録では既存AddDiagnosticSubsystemのTContributorへPublicConstructorsの保持注釈を追加する。これは生成済みcontributorのDI生成をtrim解析へ伝える補足で、警告の抑制やReflection探索の追加ではない。

専用サンプルは実入力をsurfaceへ転送しない。一般アプリに組み込むhostは、UI-0009の物理入力中立待ち・保留・予約ID・取消不能な後処理を実装する。GPUサンプルは公開前にpipelineを準備し、受付後のJob期限を延長しない。サンプルのUIキューは256件、frame処理は64件までとし、scopeとAgentは非同期破棄する。

2026-10-29にHTTP／MagicOnion双方で、診断サーバー、ゲームAgent、型付きOperatorを別プロセスとして起動し、発見→control→Click→indicatorのExpect→Enter→count→解放を確認した。GPUなし、Vulkan／wgpu（Mesa lavapipe）の各経路が成功し、PNGの分割取得・hash・CRC・240×160寸法・indicatorの64画素も確認した。外部Operatorの出力にはUI／Graphics assemblyを含めない。

実ChromiumのHTTP Agentでも、別プロセスOperatorによる同じ操作と終了を確認した。Browser publishとtrim解析を行い、wasm-toolsによるnative最適化、Native AOT、Browser GPU提示／PNG、実機GPUロストは未検証とする。Browser GPUの環境制約は既存サンプルREADMEへ記録する。

追加テスト37件はRelease／Debugで成功し、既存のUI.Automation 49件、UI統合32件、Diagnostics統合131件も成功した。三パッケージのpackと、NuGetだけを参照する外部client consumerの実行を確認した。

検証手順は[ゲームサンプル](../../../samples/Lumyte.Diagnostics.UI.Automation.Game.Sample/README.md)、[client](../../../src/Diagnostics/Lumyte.Diagnostics.UI.Automation.Client/README.md)、[Browser](../../../samples/Lumyte.UI.Browser.Sample/README.md)を参照する。

### Element分離後の再検証

ReleaseでDiagnostics UI Automation 38件が成功した。遠隔APIはコピー済みの意味情報と
Locator検索条件を扱い、再構築で変わるWidget参照やElement参照をtransportへ公開しない。
