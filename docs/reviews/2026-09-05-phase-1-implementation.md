# E1: 正しさと寿命の実装

対象は [review and roadmap](2026-09-05-review-and-roadmap.md) の E1。R01/R02/R03/R04/R13 を実装した。E2 の共通上限・shader ABI変更、E4 のdescriptor cache最適化は含まない。

## 変更

- **R01**: HotReloadのStart・通知登録・停止開始を同じlockで同期する。通知処理はlockの外で実行し、debounceで置き換えたworkも別のactive集合でdrainする。複数のDisposeAsyncは同じ終了Taskを待つ。通知先の例外は終了時に報告する。
- **R02**: Vulkanは64 descriptorのlayoutに対して、1ページ16 setを上限として容量を追跡する。read-only/writable bufferは同じstorage-buffer予算を消費する。容量超過・native pool不足・fragmentationではページを増設し、完了またはAbortでsetを返却する。空ページをresetして再利用する。ページ数はピーク使用量まで保持する。
- **R03**: GpuCommandBufferにRecording/Finished/Submitted/AbortedとDispose/Abortを追加した。提出配列の全要素について所属・状態・重複を検証してからEndを呼ぶ。native実行前に追跡先を確保し、未提出のrecordingは中断時に解放する。RenderGraphと2D/Textの記録経路に例外時の解放を接続した。
- **R03のstate管理**: Vulkanのimage layoutとDirectX12のresource stateは記録ごとに保持し、提出時に反映する。記録後に前提stateが変わったbufferは、native送信前に拒否する。その場合は現在のstateで記録し直す。複数bufferの提出では配列順にstateを検証する。
- **R03の提出後エラー**: RenderGraph/retirement queueは、提出済みのworkでエラーが起きても資源を即時解放しない。GpuSubmissionException.Completionで追跡し、完了後にexportを含めて回収する。同期Executeも同じ寿命管理を利用する。DirectX12のSignal失敗は待機時に再試行できる。Vulkan/DirectX12の待機でdevice lostを検出した場合はnative recordingを解放し、retirementを回収してGpuDeviceLostExceptionを報告する。
- **R04**: backendが発行する公開resource/view/sampler/pipeline/allocation IDを、共通のプロセス内一意な64-bit IDに統一した。native handleやdevice-local counterを公開IDとして再利用しない。slot再利用・generation導入は行っていない。
- **R13**: 希望hostをconnectへ明示して渡す。各await後とrefresh反映前に接続世代を検証する。eventは現在host・subscriptionに限定し、置き換えたWebSocketのmessage/closeが現在のrequestへ作用しないようにした。

## 利用上の契約

コマンドの所有者は、記録開始後すぐにusingで寿命を確保する。fluent chainの途中で例外が起きても確実にDisposeされるよう、初期化と記録を分ける。

```csharp
using GpuCommandBuffer commands = backend.MainQueue.StartCommandRecording();
commands.Barrier(GpuStage.None, GpuStage.All);
using GpuSemaphore completion = backend.MainQueue.CreateSemaphore();
backend.MainQueue.Submit([commands], completion, 1);
backend.MainQueue.Wait(completion, 1);
```

SubmittedのDisposeは何もしない。native recordingの解放はqueue/semaphoreがGPU完了後に行う。device・参照したresourceはその完了まで生存させる。低レベルAPIの借用resourceを自動pinする変更はE4の範囲である。device/queue/command操作は同じ所有threadで直列化する。

RenderGraphからGpuSubmissionExceptionが返った場合は、backendを生存させたまま例外のCompletionを待つか、例外をDisposeする。同期Executeが内部で作ったqueueも例外のDisposeで回収する。待機が再び失敗した場合、device lost以外では未完了資源の所有権を保持する。外部のretirement queueは通常どおりアプリケーションがDisposeする。

## 回帰試験

- Resources: fake sourceと明示的な同期による、置き換え済み通知のdrain・複数disposer・停止後の取得済みcallback。FileSystemWatcherを使う試験にはIntegration traitを付与。
- Graphics unit: Dispose/Abortの一回性、Submittedの所有権、後続要素の所属違い、重複、Endの失敗、提出後失敗時の資源保持、device lost時の回収。
- 3 backend共通conformance: 二重提出、2件目が別deviceまたは提出済み、2device間のtexture/sampler取違え、記録途中の例外とその後の描画。
- Vulkan conformance: 16/17/64回のread-only/writable buffer table bind、Abort後と複数frameの完了後のページ再利用、compute出力。
- Frontend: 2host切替時の選択・購読・表示、遅い旧refresh、同じhostへの再接続後の旧subscription、旧WebSocketのclose/message。

GPU強制切断・driverのSignal失敗・pool fragmentationの実機故障注入、validation layerを強制した試験、長時間・性能測定は未実施。device lostの共通回収経路はfake queueで検証する。1k/10k batchの性能・ページ予算とcacheはE4で測定する。

## 最終検証結果

- `dotnet test Lumyte.slnx --nologo --verbosity minimal`: 終了コード0。24プロジェクト986件成功、失敗0、スキップ0。Resources 64件、Graphics unit 122件、Vulkan 153件、DirectX12 152件、WebGPU Native 149件を含む。
- Frontend: Vitest 5ファイル14件成功。ESLint、TypeScript project build、Vite production buildも成功。Viteの500 kB chunk警告は残る。
- この環境はnpmがPATHにないため、同梱Nodeから既存のTypeScript/Viteを直接実行してfrontend成果物を更新した後、全体テストを実行した。Vitest/Viteはsandboxのchild process制約を受けるため制限外で実行した。
- `git diff --check`: 成功。
