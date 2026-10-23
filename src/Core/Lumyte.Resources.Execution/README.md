# Lumyte.Resources.Execution

Resourcesの非同期Stageから、Executionの型付きCPUジョブを使用する接続です。
`ResourceJob<TCommand, TResult, THandler>`を一つの`IExecutionModule`として登録します。
TCommandはstruct、THandlerは`ICommandHandler<TCommand, TResult>`を実装するstateless structです。
`Execute(command, token)`は実際の結果を返し、ラムダ・Task.Run・ExecuteAsyncへの転送を追加しません。
入力の保持と出力の所有権はStageが管理し、ジョブ完了後に次のStageへ進みます。

I/OはSourceの非同期APIで待ち、デコードなど有限のCPU処理をジョブへ送ります。
ハンドラー内でawait・依存ロード・所有スレッド待機を行わないでください。
CPUジョブの同時実行数・容量・キャンセルはExecutionRuntimeが管理します。
開始後のジョブは中断して出力を捨てず、実際の完了をStageへ返します。
大きなデコードは有限のチャンクへ分割し、協調実行でホストを長時間占有しないようにします。

ThreadPoolモードではワーカーへ、CooperativeモードではホストのPumpJobsへ配置されます。
ブラウザでの同期WaitやResultによる待機は行わず、ホストのループを回したままawaitします。
Resources自身はスレッドを追加せず、ExecutionRuntimeはホストが起動・停止します。

[Runtime設計](../../../docs/adr/resources/RESOURCES-0009-runtime-execution.md)を参照してください。
Audio固有のStage・デコード・DI登録は独立したAudio機能で提供します。
