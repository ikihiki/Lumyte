# 共通の診断通信とエージェント

ゲーム側の診断サブシステムは、通信層の型や属性を参照しない。Composition Root が IDiagnosticTransportFactory の実装を DI で選び、Scoped な `DiagnosticAgent<TPoint>` がカタログ公開、要求受信、結果と標準テレメトリーの送信を行う。

```csharp
services.AddDiagnosticAgent<BeforeInputProcessing>();
// 次のどちらかを選ぶ。
services.AddHttpDiagnosticTransport(options =>
{
    options.BaseAddress = new Uri("http://127.0.0.1:5000");
    options.GameToken = gameToken;
});
// または AddMagicOnionDiagnosticTransport(...)
```

所有スレッドで Pump を Activate した後に ConnectAsync を呼び、RunAsync の実行中にエンジンが安全な時点で Pump を呼ぶ。ネットワークの継続処理は SubmitAsync と detached DTO の送信だけを行い、Input ドメインには触らない。

次は `origin/main` の 1a37a6b に対する主要 API の追加。型の namespace は Lumyte.Diagnostics.Transport。

```diff
+public interface IDiagnosticTransportFactory
+{
+    ValueTask<IDiagnosticConnection> OpenAsync(ClientHello hello,
+        CancellationToken cancellationToken);
+}
+public interface IDiagnosticConnection : IAsyncDisposable
+{
+    SessionWelcome Welcome { get; }
+    IAsyncEnumerable<DiagnosticCommand> ReadCommandsAsync(CancellationToken cancellationToken);
+    ValueTask<PublishReceipt> PublishAsync(DiagnosticMessage message,
+        CancellationToken cancellationToken);
+}
+public sealed class DiagnosticAgent<TPoint> : IAsyncDisposable where TPoint : class
+{
+    // DI で解決。接続完了後に読める。
+    public Guid SessionId { get; }
+    public Task ConnectAsync(CancellationToken cancellationToken = default);
+    // 接続後に一度開始。通信の終了・失敗・キャンセルで完了する。
+    public Task RunAsync();
+    // idempotent。Input の解除・Pump.Deactivate は所有スレッドが行う。
+    public ValueTask DisposeAsync();
+}
```

一つの Agent は一つの execution point を扱う。ConnectAsync は一度に限り、接続前に有効なカタログを要求する。RunAsync は command reception と telemetry publication のどちらかの終了で両方を停止する。停止後の再接続は新しい DI スコープ／Agent で行う。自動再接続、カタログの hot reload、複数ポイントを一接続へ束ねる処理は初期実装に含まない。

SessionWelcome は SessionId、SessionSecret、Permissions。DiagnosticCommand は RequestId、SubsystemId、OperationId、ActorId、ExpectedRevision、ExpiresUnixMilliseconds、scalar Arguments を持つ。期限は UTC で届け、ゲーム側で TimeProvider の単調増加時計へ変換する。別ホストでは時計同期を前提とし、最大 30 秒に制限する。CancellationToken は wire に含めない。

DiagnosticMessageKind は CommandResult / Telemetry / Heartbeat の閉じた集合。Game のカタログは Hello で一括公開する。PublishReceipt.Accepted はサーバーによる受理であり、永続保存を意味しない。アップロード、購読・集約、登録世代、Trace Events / Links は未対応。

送信 publication は HTTP で `DiagnosticJsonMessageEncoder` / Utf8JsonWriter、MagicOnion で `DiagnosticMessageFormatter` / MessagePackWriter へ直接書く。生成結果は snapshot から直接、テレメトリーは収集済み scalar から直接書き、送信時の WireValue / WireEvent / WireResult / WireMessage を生成しない。envelope と batch、非同期送信まで値を保持する snapshot は残る。

低頻度の受信・Hello は source-generated JSON または generated MessagePack resolver で復号し、検証した scalar を所有する内部要求として Pump へ渡す。MessagePack 属性は MagicOnion モジュールの wire-only DTO に限定する。通信失敗は DiagnosticTransportException として伝え、呼び出しのキャンセルは OperationCanceledException を使う。

Agent は IAsyncDisposable なので DI スコープを DisposeAsync で破棄する。エンジンの所有スレッドでの解除を先に済ませ、通信待ちの continuation からドメインにアクセスしない。[実行例](../../../samples/Lumyte.Diagnostics.Remote.Sample/README.md) を参照する。
