# Lumyte.Execution

coreに置く、BCLだけに依存した実行基盤。モジュールが同期的な仕事を宣言し、利用側が型付きcommandを送信する。Host／専用スレッドの所有制約と、Cooperative／ThreadPoolのCPUジョブを同じRuntimeで扱う。

## 登録と起動

Microsoft.Extensions.DependencyInjectionを使うアプリでは、factoryを用意せず登録できる。

```csharp
services.AddSingleton<IExecutionModule, AudioModule>();
services.AddSingleton<ExecutionRuntime>();
var runtime = provider.GetRequiredService<ExecutionRuntime>();
runtime.PrepareOnCurrentHost(OwnerPlacement.Dedicated,
    CpuExecutionMode.ThreadPool, 4, new ExecutionOptions());
// 固定Hostのevent loopへPumpを接続してから呼ぶ。
Task startup = runtime.StartAsync().AsTask();
```

Runtimeの唯一の公開constructorは `ExecutionRuntime(IEnumerable<IExecutionModule>)`。一覧を一度だけ収集し、名前・実装型の重複と依存を検証する。`Declare` は仕事、owner、FIFO group、gateを登録し、`Configure` はowner配置と更新駆動を登録する。どちらもdeviceやthreadを作らない。DIなしでも `new ExecutionRuntime([new AudioModule()])` から同じ経路を使う。

Prepareは固定Hostで一度だけ行い、全endpointを検証して接続する。Startで専用threadを起動し、依存順にmoduleを初期化する。Hostの各turnでは `PumpHostLifecycle` と `PumpJobs` を呼び、Host配置の通常仕事と更新には `RunHostPhase` も呼ぶ。起動・停止の未完了TaskをHost上で同期waitしない。

Browserでは `OwnerPlacement.Host, CpuExecutionMode.Cooperative, 1` を明示的に選ぶ。HostOnlyは常にHostに置く。所有者と呼び出し側が同じ物理threadでもcommandは必ずenqueueする。WebAudioサンプルをChromiumで検証した。

## 型付き送信と所有権

[Generator](../Lumyte.Execution.Generators/README.md)がメソッドからcommand、handler、登録API、senderを生成する。[Audioサンプル](../../../samples/Lumyte.Execution.Sample/README.md)のAudioVoice自体が元インスタンス参照一本のreadonly structで、`new AudioVoice(original)`から作り、`voice.Play()` が固定のstatic endpointへ送信する。

単発はtyped payloadと結果を保持する一つの非pool `IValueTaskSource<TResult>` を使い、Task／batch／完了wrapperを作らない。生成senderは受付とValueTask構築を本体に展開し、coreのExecuteAsyncを中継しない。ValueTaskは一度だけ観測する。AsTaskへ変換した場合はそのTaskを観測する。

Ownerの異型commandは同じgroupのFIFOで処理する。queue nodeはcommand自身が持ち、groupごとの拡張配列を残さない。CPU仕事は順序を指定せず、ThreadPoolの同時数を制限する。専用threadは到着通知と更新周期で起床し、外部から更新commandを繰り返し送る必要はない。

`CommandBatch` は同じowner/group、またはCPUだけの仕事を全件受理する。未受理ならstagingを維持し、完了後は `run.Dispose()` → `batch.Reset()` の順に再利用する。成功済みの個別結果は、他commandが失敗しても取得できる。

`JobGraph<TContext>` は不変で、Unit結果の仕事と範囲を依存辺でつなぐ。Rangeの長さは受付前に一度評価し、同時数分以下のworkerがchunkをclaimする。owner待ちはCPU枠を使わない。将来の配送枠、node・edge・worker管理領域も受付時に予約し、依存伝播と取消は有界のstepで進める。

## 容量・取消・停止

通常枠と停止時cleanup枠は独立する。MaxInFlightRuns、MaxCommands、MaxGraphNodes、MaxPayloadBytesは完了後の未回収分にも適用する。typed input／resultと保守的なcommand管理費を課金し、参照先buffer・native資源は統合層が管理する。MaxGraphNodesにはnode・edge・range workerを含める。stagingと不変graphは利用側の所有物である。

取消がhandler開始より先なら副作用を抑止し、開始後なら実際の結果を返す。handler自身のOperationCanceledExceptionはfaultとして扱う。キャンセル可能なgate待ちCPU仕事とgraphには、必要時だけtokenの通知登録を使う。通常の取消なし送信には登録しない。元の例外を層ごとに包み直さず、StackTraceHiddenも使用しない。

handler内の投入、Pump、runtime停止は拒否する。handlerは有限時間で返し、I/Oや非同期操作の実完了待ちは外側workflowで行う。更新・時間予算はOSの実時間保証ではなく、既に始めたhandlerは中断しない。

停止は受付・更新の閉鎖、開始済み仕事の静止、逆依存順のcleanup、thread終了の順。停止が非同期初期化と重なっても、起動中のcontextで実operationの終了処理を送り、その静止を待つ。Quiescing中のlifecycle Cleanupも予約枠を使う。Stop成功後にDI providerを破棄する。cleanup失敗ではownerとstateを保持し、同じRuntimeのStopを再試行する。静的endpointは停止後も再接続できず、過去のhandler・Runtime参照を外す。

[設計ADR](../../../docs/adr/core/CORE-0003-execution-and-jobs.md)には、Audio／Resources／ECSとの責務分担と他ライブラリの調査を記載している。
