# Lumyte.Diagnostics.UI.Automation

既存DiagnosticsのOperationからUIを検索・操作する任意の接続です。Operationは受付結果をすぐ返し、実際の操作・待機はPump後のAdvanceと通常frameで進めます。UI本体・UI.Automationへ通信依存を追加せず、新しいsocketも開きません。

```csharp
services.AddLumyteDiagnostics(options => options.Enabled = true);
services.AddDiagnosticExecutionPoint<BeforeUiAutomation>("ui.before-automation");
services.AddUiAutomationDiagnostics<BeforeUiAutomation>();
services.AddDiagnosticAgent<BeforeUiAutomation>();
// 既存HTTP／MagicOnion transportをcomposition rootで選択する。
var runtime = scope.ServiceProvider.GetRequiredService<UiAutomationDiagnosticsRuntime>();
var registration = runtime.Register("settings", surface, host,
    new UiAutomationTargetOptions { AllowInput = true, AllowScreenshots = true });
```

BeforeUiAutomationはアプリのmarkerです。Register、Operation、Advance、登録解除は同じexecution ownerで行います。UIへ戻すdispatcherと新frameの進行はhostの責務です。通知だけを行うNotifySessionClosedはネットワークthreadから呼べます。診断無効時の登録は解放済みのno-opとなり、snapshot・Job・画像取得を始めません。

通常loopは入力取得→Diagnostics.Pump→runtime.Advance→hostのUIキュー→layout/描画→新frame完了通知の順です。Advanceは取消・期限、完了Taskの固定、新規Job開始を処理し、未完了Taskを待ちません。Task.RunからUIへ触れる方式は使いません。

`ui.automation`、schema version 1として、targets、control-acquire/get/renew/release、action-start/get/cancel/forget、observe-start/get/cancel/forget、artifact-readを公開します。既存のOperations画面も利用できます。Observeは検索・状態検証・許可された画像取得、OverrideInputは操作権とClick/Focus/Pressに必要です。ActorとSessionは信頼済みcontextから取得し、要求本文から受け取りません。別所有者のIDはnot-foundです。

入力・Screenshotは登録時の許可が既定falseです。公開範囲は登録surface全体であり、部分木の秘匿機能ではありません。名前は小文字ASCII識別子1〜128文字、一つのsurfaceを重複登録できません。名前の再登録でもTargetIdは変わり、hostがsurfaceを交換した場合も新しい登録が必要です。

controlはTargetごとに一つ、寿命1〜5秒です。同じ所有者の再取得は期限を延長しません。更新・解放はExpectedRevisionを比較します。各入力はNextSequenceと正規化payloadの指紋で識別し、同じSequenceの再配送は保持中の元Jobへ合流します。内容変更、Sequence飛ばし、Forget後の古いSequence、失効したcontrolを拒否します。新しい操作としての自動再送はしません。

JobはQueued→Running→Succeeded/Failed、またはCancelRequested→Canceled/TimedOutです。CancelRequestedは操作停止の完了ではありません。入力leaseの取得成功時にInputMayHaveBeenSentを保守的にtrueとし、後処理の完了後だけterminalへ進めます。Screenshot capabilityは元hostが対応する場合だけ転送します。Job期限は受付時から100〜10000msで、RPC取消・再配送・照会では延長しません。

snapshot、PNG、traceは固定byte列のartifactにし、Jobと所有者へ結び付けます。artifact-readは最大64KiBの生byteをbase64で返します。offsetはbyte位置、終端は0 byteです。再読は同じ内容で、SHA-256を含みます。詳細artifactにはObserveが必要です。JSONは上限付きbufferで符号化し、巨大な結果を切り詰めません。

既定上限はTarget 16、保持Job 64、実行中4、同じTargetの入力Job／Screenshotそれぞれ1、snapshot 10000ノード、artifact 8MiB、合計32MiBです。結果はterminalから120秒またはForgetまで保持し、読取では延命しません。容量不足はbusy／result-too-largeで拒否し、既存Jobを追い出しません。

登録解除・controlの解放・期限・Agent切断では取消後のhost清掃を待ちます。登録のIsReleasedまでUI/hostを維持してください。全体停止はBeginStop→frame/Advance継続→IsStopped→Pump停止・Agent停止・scope解放です。RuntimeはUI、Window、hostをDisposeしません。閉鎖通知はJob容量とは別の有界・重複統合経路で受け、上限超過時は新規操作を停止する方針です。

監査のLog categoryとActivitySourceは`Lumyte.Diagnostics.UI.Automation`です。既存DiagnosticOptionsの許可リストへ追加すると、操作・取消・完了・controlの変更を観測できます。tokenや要求本文を記録しません。

[設計](../../../docs/adr/ui/UI-0010-diagnostics-ui-automation.md)／[client](../Lumyte.Diagnostics.UI.Automation.Client/README.md)／[別プロセスの確認](../../../samples/Lumyte.Diagnostics.UI.Automation.Game.Sample/README.md)

PopupはRegisterPopup(parent, popupId, name, surface, routingHost, options)で独立targetとして登録します。親子はroot TargetIdをControlGroupIdとし、controlとSequenceを共有します。指紋には対象IDを含め、同Sequenceを別targetで再利用できません。子の許可は親とのANDです。子登録解除はそのtargetを、親解除・control失効はgroup全体を取消します。登録tokenのDisposeAsyncをPopupのBindLifetimeへ渡すと、清掃完了までsurfaceを維持できます。枠不足はroot targetsのAutomationCodeへautomation-unavailableを報告します。
