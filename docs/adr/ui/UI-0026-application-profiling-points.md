# ADR-UI-0026: アプリケーションのフレーム所有権と待機を計測する

- 状態: 採用
- 日付: 2026-11-11
- 基準コミット: `8ea844540cd98d2beb70eebb132e6086be66e662`

## 背景

UI ホストの開発者は、遅いフレームが準備、描画面取得、投入待ち、完了待ちのどこで滞留したかを区別したい。現在の `UiHostPresenter.DrawAsync` は描画投入後も資源を保持し、投入完了と表示面解放を別々に待つため、メソッドの総時間だけでは原因と所有権を説明できない。

`UiApplication.AdvanceRendering` は回復中やフレーム間隔未到達では戻り、各ウィンドウの実行中描画も飛ばす。`UiFrameSubmissionQueue` は投入用ゲートと処理中フレーム数の枠を別々に解放する。この既存の順序を変えず、ホストが所有する境界を観測する。

## 決定

内部 EventSource `Lumyte-UI-Application-Profiling` に、フレームの識別、工程、集計カウンターを追加する。EventPipe で収集し dotnet-trace で取得する方式とし、既存の非同期処理と解放条件をそのまま観測できる位置に挿入する。

### 利用者の変更前・変更後

アプリケーションの構築、`RunAsync`、`StopAsync` の呼び出しコードは変わらない。変更前はフレーム全体の遅延から待機原因を推測する。変更後は対象プロバイダーを Level 4、キーワード `0x7` で取得し、同じフレームの投入、完了、解放、抑止理由を時系列で比較できる。ここで記載するイベントは追加提案である。

### イベントとフレームの識別

- ロガー型は internal とし、入れ子の `public static class Keywords` に `public const EventKeywords Control = 1, Stages = 2, Counters = 4, GPU = 8` 相当の各定数を置く。全イベントは Informational（Level 4）とする。このプロバイダーは GPU 時刻を発行しない。
- イベント ID は `1 StageBegin`、`2 StageEnd`、`3 StageCounter`。工程は下表の固定数値、終了結果は `0 成功 / 1 取消 / 2 失敗 / 3 スキップ` とする。開始・終了は Stages、集計値は Counters を用いる。
- 工程イベントは `ProfileRunId: Guid`、`FrameId: long`、`SpanId: long`、`ParentSpanId: long`、工程 ID を持つ。SpanId はプロバイダー内で一意、親は同じプロバイダーの区間だけとし、不在は 0。終了に結果と数値理由、カウンターに固定カウンター ID と long 値を付ける。
- ホストは Control（0x1）の購読中だけ、描画対象を受理した時点で正の FrameId を実行内単調増加で採番する。`AdvanceRendering` では `RecordRetainedFrame` の直前とし、例外で記録に失敗した試行も同じ ID で閉じる。別ウィンドウ、次の試行には別 ID を割り当てる。
- ProfileRunId はホスト実行を識別する Guid とし、Control の初回有効化時に一度だけ生成し、無効化・再有効化後も再利用する。最初から無効なら Guid を生成しない。採番時にフレーム単位の内部 Activity を開始し、custom property `Lumyte.Profiling.RunId`（Guid）と `Lumyte.Profiling.FrameId`（long）を設定する。工程ごとの Activity は作らない。
- 下流は `Activity.Current` から Parent チェーンを辿り、同じ Activity 上に正しい型の両プロパティを持つ最も近い組を読む。該当しなければ ProfileRunId = Guid.Empty、FrameId = 0 とする。ホストは await 前にキーを内部の描画状態、`UiHostPresenter.Frame`、queue の Flight に保存し、継続、再試行、解放時には保存値を使う。
- 呼出元 Activity は変更しない。各描画の ExecutionContext で独立したフレーム Activity を Current にし、描画を開始する同期側は直前の Current を finally で復元する。非同期継続は捕捉した文脈を使う。終了・遅延した停止でも、そのフレームの文脈内でのみ Activity を停止し、他の重複フレームの Current を上書きしない。記録失敗時も同様に復元する。
- `Frame.Owner` の Guid は既存の `window.Source.SourceId` であり、ProfileRunId とは別物。制御イベントの SourceId に載せる。コンテンツ名、入力文字、例外本文、画像データは送らない。

制御イベントは Control を用い、次の ID に固定する。フレームを持たない owner 操作は FrameId = 0、工程外の SpanId / ParentSpanId = 0 とする。

| ID / 名称 | 発行点と追加情報 |
| --- | --- |
| 4 FrameAccepted | 記録直前。SourceId、GraphicsGeneration を記録する |
| 5 FrameFinished | `DrawFrameAsync` の scene 解放までの終了、または RecordSnapshot 失敗時。結果、presented、SourceId を一度記録する |
| 6 FrameSuppressed | `AdvanceRendering` の早期 return と対象ごとの continue。理由、対象数を 1 回の呼出しで理由別集計する |
| 7 SurfaceResult | Acquire と Present の直後。区分と実際の SurfaceStatus、presenter 試行番号を記録する |
| 8 DeviceRetired | `Frame.ConfirmDeviceRetirement` の初回状態変更。保持中フレームのキーで記録する |
| 9 FrameResourcesReleased | Frame が正常に Dispose を終えた最初の 1 回。通常完了か確認済み退役かを記録する |
| 10 OwnerRelation | OwnerDrain 開始直後に `rootSpanId: long` と `owner: Guid`（SourceId）を対応付ける。Control のみで工程購読がなければ rootSpanId は 0 |
| 11 RunMetadata | `profileRunId: Guid` と runtime / operatingSystem / processArchitecture / backend / assemblyVersion / gpu / buildHash / seed / scenario / warmup の string。Control 初回有効フレームと、その後の受理フレーム境界で最大 1 Hz。即取得可能な実行時・OS・プロセス architecture・driver 型・assembly informational version 以外は `unknown`。ファイル I/O や GPU query は行わない |

`SurfaceResult.kind` は `1 Acquire / 2 Present / 3 サイズ不一致で合成した Outdated`、`attempt` は同じホスト描画内の Presenter 呼出し試行番号（初回 1、capture 非対応後の再描画 2）とする。

RunMetadata は同時収集セッションの途中接続にも最大 1 秒間隔で同じ実行を再通知する。ウォームアップ・シナリオ・期待フレーム数の測定開始終了 marker は既存ベンチマークの責務であり、本体は未取得値を推測しない。

抑止理由は `1 回復中 / 2 間隔未到達 / 3 Drawing あり / 4 entry 停止中 / 5 StopFailed` に固定する。複数条件はこの順で一つに分類する。抑止は受理前なので FrameId = 0 とし、架空のフレーム開始・終了を作らない。

### 工程と正確な挿入境界

表のメソッドは、特記しない限り `src/UI/Lumyte.UI.Application/` 配下にある。外側区間は内側区間を含む経過時間であり、足し合わせて CPU 時間にしない。

| 工程 ID / 意味 | 開始と終了の挿入点 |
| --- | --- |
| 1 OwnerTurn | `UiApplication.Turn` の SynchronizationContext 設定前から、既存 finally での復元後まで |
| 2 OwnerWait | `RunThreadLoop` の `Driver.WaitForWork` 呼出し直前から復帰まで。`FrameWait()` の計算は外側 |
| 3 BrowserTurnWait | `RunBrowserLoopAsync` の `Driver.NextTurnAsync` 直前から await 終了まで。例外後の Task.Delay は含めない |
| 4 AdvanceRendering | `AdvanceRendering` 入口から全 return を含む出口まで |
| 5 RecordSnapshot | 対象受理後、`entry.Root.Source.RecordRetainedFrame(GraphicsGeneration)` の呼出しだけ |
| 6 HostDraw | `UiApplication.DrawFrameAsync` 入口から scene の using 解放終了まで。Driver の待機と後処理を含む |
| 7 ObserveCompletion | `CompleteFrames` 入口から `Driver.CompleteFrames` 復帰まで。Task の観測であり GPU 待機ではない |
| 8 PresenterDraw | `UiHostPresenter.DrawAsync` 入口から既存 finally の `Finished.TrySetResult` まで |
| 9 Prepare | Presenter の intermediate Rent 直前から PrepareAsync、argument table、semaphore 作成終了まで。Acquire の直前で終える |
| 10 Acquire | `swapchain.AcquireNextFrameAsync(acquired)` の直前から await 復帰まで |
| 11 Record | 有効な image とサイズ一致を確認後、display view 作成直前から `commands.Finish()` 復帰まで |
| 12 Submit | `SubmissionAttempted = true` の直前から `Queue.Submit` の復帰または throw まで。AttachSubmission は含めない |
| 13 Present | `image.Present([rendered])` 呼出しだけ。ステータス補正や完了待ちは含めない |
| 14 SubmissionWait | `Frame.DrainAsync` で実際に行う `submission.WaitAsync(_retirement.Token)` の直前から await 終了まで |
| 15 ImageReleaseWait | 同メソッドの `image.WaitForReleaseAsync(_retirement.Token)` の直前から await 終了まで |
| 16 FrameDispose | `Frame.Dispose` の入口から出口まで。再試行ごとに別 SpanId とし、成功済みの解放を二重集計しない |
| 17 QueueSlotWait | `UiFrameSubmissionQueue.EnterAsync` の `_slots.WaitAsync()` だけ |
| 18 QueueGateWait | 同メソッドの `submissionGate.WaitAsync()` だけ |
| 19 QueueGateResume | `Flight.ResumeAsync` の実際の `gate.WaitAsync()` だけ。既に保持中なら区間を作らない |
| 20 QueueFlight | EnterAsync で slot 取得直後から Flight.Dispose による slot 解放まで。ゲート取得失敗時は slot を返す catch で終了 |
| 21 OwnerDrain | `UiHostPresenter.DrainAsync(Guid owner)` 入口から出口まで。FrameId = 0、SourceId = owner とし、Finished 待ちと残存フレーム処理を含む |
| 22 OwnerFinishedWait | OwnerDrain と DeviceRetirementRelease の各 `await frame.Finished.Task` だけ。対象 Frame の保存キーを使う |
| 23 DeviceRetirementRelease | `ReleaseAfterDeviceRetirementAsync` 全体。StopRenting、確認済み退役、Finished 待ち、解放を含む |
| 24 PresenterDispose | `UiHostPresenter.Dispose` 全体。未 drain の例外も記録する |
| 25 Stop | `UiApplication.StopCoreAsync` 入口から終了まで。失敗後の Stop 再試行は別区間 |
| 26 CloseDrawWait | `CloseEntryAsync` の `await drawing` だけ。対象描画のキーを保存して使う |
| 27 WindowDrain | `UiPlatformApplicationDriver.DrainWindowAsync` 全体。通常の owner drain と退役待ちを区別する |
| 28 CandidatePrepare | `UiHostPresenter.PrepareCandidateAsync` 全体。target / view / prepared の using 解放を含む |
| 29 CaptureReadback | Presenter の readback.MapAsync 直前から、Unmap、行詰め替え、色順変換、capture 呼出し終了まで |

HostDraw は最初の記録に対応する 1 区間、PresenterDraw は実際の呼出しごとに 1 区間とする。`UiPlatformApplicationDriver.DrawAsync` のスクリーンショット非対応時の再描画は同じ FrameId、異なる SpanId / 試行番号となる。
同一プロバイダーの直近の外側区間を親とするが、非同期処理が別の Turn へ継続しても親を書き換えない。OwnerDrain の呼出元とフレーム区間の関係はキーで結び、親 ID を他フレームから流用しない。

### スキップ、例外、寿命

工程の開始を記録した場合は finally 相当で終了を一度だけ記録する。実際に実行しない工程は開始しない。開始時に有効性を保存し、途中の無効化後も有効時の対を閉じるよう試みるが、EventPipe の途中終了やイベント欠落では対が欠け得る。
Acquire で Frame がない場合やサイズ不一致では PresenterDraw をスキップで終了し、Record / Submit / Present を生成しない。サイズ不一致で合成する Outdated も SurfaceResult に区分を付けて記録する。Timeout / Outdated / Lost / DeviceLost は SurfaceResult に残し、正常な表示成功と区別する。Success / Suboptimal は表示を受理した結果であり、画面走査完了の保証ではない。
伝播する OperationCanceledException は取消、それ以外の例外は失敗とする。確認済み退役で吸収する待機取消も取消として閉じ、DeviceRetired と対応させる。通常の待機失敗を完了成功に読み替えない。

`Frame.DrainAsync` は成功済みの `_submitted` / `_released` の待機を繰り返さない。失敗後の再試行は新しい SpanId とし、両境界の確認まで資源を保持する。Submit が throw し Submission がない場合も、`SubmissionAttempted` による隔離を保持し、解放イベントは確認済み退役後にのみ出す。
HostDraw の正常な false 戻りはスキップ、true は成功とする。AdvanceRendering の早期 return もスキップとする。FrameFinished はホスト描画 Task の終端であり、資源解放の証明ではない。失敗した Frame が `_frames` に残る場合は FrameResourcesReleased が後から届く。QueueFlight の slot 占有も独立で、`Submitted()` はゲートだけを解放するため完了イベントにしない。
Stop は entry の新規描画停止、描画 Task 待ち、WindowDrain、退役・資源解放の既存順序を観測する。StopFailed で保持した所有権を計測都合で捨てず、再試行と確認済み退役を別の終了経路として残す。

### 固定カウンター

値は該当工程の終了時にまとめて StageCounter で出す。単位は下表どおりで全て実数え・実測値とし、項目ごとのイベントや GPU メモリー推定値は出さない。失敗時は到達した操作だけ数え、未取得値を 0 と偽らず省略する。

| ID / 値 | 取得元、単位、集計区間 |
| --- | --- |
| 1 AcceptedWindows / 2 BusyWindows / 3 StoppingWindows / 4 StopFailedWindows | AdvanceRendering の各分岐を通過した対象数、件。Driver.AdvanceFrames の bool は件数に足さない |
| 5 DriverAdvanced | Driver.AdvanceFrames の戻り値、0 / 1、AdvanceRendering |
| 6 CompletedDraws / 7 PresentedDraws | CompleteFrames で観測した Task 数と presented が true の数、件 |
| 8 WidthPixels / 9 HeightPixels | Presenter の中間 target.Width / Height、pixel、Prepare |
| 10 RetainedVisuals | Presenter に渡された visuals.Count、件、Prepare。画像バイト数ではない |
| 11 ReadbackBytes | 作成した readback.Count = pitch × target.Height、byte、Prepare。padding を含む |
| 12 SubmittedCommandBuffers | Queue.Submit が戻った回数 × この呼出しの CommandBuffers 1 本、件、Submit。throw 時は成功数 0 |
| 13 PresentDrawCalls | ホストの合成 render.Draw(3) が戻った回数、件、Record。Drawing2D 内部の draw は含めない |
| 14 CaptureOutputBytes | callback に渡す pixels.Length、byte、CaptureReadback。readback がない場合は省略 |
| 15 PresenterFrames | `_frames.Count` の操作後スナップショット、件、PresenterDraw / OwnerDrain / DeviceRetirementRelease 終了時 |
| 16 QueueSlotsHeld / 17 QueueGateHeld | slot 取得・返却、gate 取得・Submitted / Dispose の成功で内部計数した占有数、件、QueueFlight / QueueGateWait / QueueGateResume 終了時 |
| 18 WaitTimeoutTicks | OwnerWait に渡した timeout.Ticks、100 ns。実際の待機時間はイベント時刻差で求める |

queue の計数は全 Flight（visual 更新を含む）を対象とし、未相関の処理は FrameId = 0 を使う。SemaphoreSlim.CurrentCount から処理中数を推測しない。値はその観測点のスナップショットであり、複数工程の値を累積加算しない。

### 公開 API への影響

公開 API、既存の内部メソッドの呼出し契約、friend assembly を変更しない。識別子と工程状態は同じアセンブリの内部フィールドで保持し、他アセンブリへの相関は Activity の custom property だけを用いる。UI の Measure と Drawing2D 内部処理の工程・件数は本判断の対象外とする。

## 検討した代替案

- DrawAsync 全体だけを囲む: 小さい変更で済むが、投入ゲート、GPU 完了、表示面解放、後処理を区別できない。実在する所有境界を分ける。
- Submitted でフレームを完了扱いにする: 短い区間になるが、slot と GPU 資源を保持する期間を失う。ホスト Task、Flight、資源解放の終端を分ける。
- 独自収集セッションや工程ごとの Activity: 任意の保存ができるが、EventPipe と重複し、非同期相関とホットパス負担が増える。標準収集とフレーム単位の相関を選ぶ。

## 結果と影響

開発者は UI ホストの待機と処理中資源を同じ FrameId で追跡できる。時間差は壁時計上の経過時間であり、GPU 実行時間や CPU 専有時間を保証しない。完全なフレーム分析にはホストの Control（0x1）購読が必要となる。購読なしでは新しい相関キーや Activity を作らず、下流プロバイダーだけの取得は未相関となる。有効時に開始済みのフレームだけは保存キーで終了処理し、無効化後の新規フレームへ流用しない。工程状態、カウンター集計、イベント生成は IsEnabled を先に判定して省き、無効時コストを実測する。

検証では、通常の複数ウィンドウ、間隔未到達、回復、停止、描画中の抑止を分け、受理フレームだけに ID が付き、Activity 値が復元されることを確認する。途中有効化、再有効化、下流のみの購読、重複する非同期フレームと遅延停止でも相関の有無と Current の分離を確認する。FrameQueueTests の gate と slot の独立性、FramePacingTests の待機からの起床と位相維持、RecordingHostTests の連続フレーム投入を保持する。
FrameTests の投入待ち失敗、表示面待ち失敗、再試行、Submission 不明、退役による取消、解放失敗再試行で、工程の結果、資源保持、解放イベントの一回性を確認する。Acquire 失敗、サイズ不一致、capture 再描画、StopFailed 後の再試行、収集中断・イベント欠落も確認し、欠落対を成功と補完しない。

根拠は基準コミットの `src/UI/Lumyte.UI.Application/UiApplication.cs`、`UiApplication.Frames.cs`、`UiApplication.Resources.cs`、`UiApplication.Stopping.cs`、`UiPlatformApplicationDriver.cs`、`UiHostPresenter.cs`、`UiFrameSubmissionQueue.cs` と、`tests/UI/Lumyte.UI.Application.Tests/` の `FrameTests.cs`、`FrameQueueTests.cs`、`FramePacingTests.cs`、`RecordingHostTests.cs` とする。
