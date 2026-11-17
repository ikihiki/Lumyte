# Lumyte.UI.Application

CompositionのUIとサービスを登録して起動する標準ホストです。入力、layout、全体描画、Window/Popup、IME、終了順序を管理します。

```csharp
using Lumyte.UI.Application;
using Lumyte.UI.Application.Desktop;
using static Lumyte.UI.Compose;

var builder = UiApplication.CreateBuilder(args).UseDesktop();
builder.UseRoot(context => Button(width: 160, height: 48, onClick: context.RequestStop));
await using var app = builder.Build();
await app.RunAsync();
```

`Build`はサービス登録を固定しDIを検証します。OS/GPUやroot factoryの呼び出しは`RunAsync`で開始します。Browserでは`UseBrowser`を使いJS threadから起動します。HeadlessはGPU/OS入力を持ちません。

`Configure(new UiApplicationOptions { FrameInterval = TimeSpan.FromSeconds(1d / 120) })`で120Hzを要求できます。
標準のthread loopは固定の描画期限を保ち、継続処理や停止要求で待機を解除します。
Windows Desktopでは高精度waitable timerを使い、利用できない場合は通常の待機へ戻ります。
描画中のrootへ重ねて提出せず、GPU完了と提示資源の解放後に次のframeを進めます。
実際の達成率はUIの負荷・device・OSのスケジューリングに依存します。

複数Windowは`using static Lumyte.UI.Application.Compose;`の`Window(...)`を`UseWindows`から返します。同じkeyの内容と内部状態を保持し、閉じて再追加した場合は新しいIDを発行します。`isOpen`はモデルで管理し、`onCloseRequested`でモデルを変更して`UiApplicationContext.RequestRebuildWindows()`を呼びます。AutomaticはNativeが利用できれば独立表示、それ以外はHosted、Hostedは同じ提示root内、NativeRequiredは非対応時に失敗します。

構築関数の再評価は`context.View.CurrentSurface.Rebuild(newRoot)`で公開します。変更済みWidgetを使い回す必要はありません。DIによるページ登録、Signal、アニメーションの自動接続は後続packageで追加します。

Navigationは任意の[Application.Navigation](../Lumyte.UI.Application.Navigation/README.md) packageで提供します。標準Application、各driver、基本DiagnosticsはNavigationを参照しません。

TextBoxはstringとonTextChangedを受け取ります。`UseTextInput`へfont設定済みのIUiTextLayoutProviderを登録すると、hostが対応するWindows TSF/Browser textareaを接続します。OS serviceを各TextBoxへ渡しません。interface型providerのComposition引数は`layoutProvider: new(context.TextLayoutProvider)`と指定します。Linux/Headlessの編集backendは初期対応に含みません。候補表示の方針は`UiApplicationOptions.ImeCandidates`で指定します。

`context.Automation`は通常入力と同じ経路のUiAutomationPageへ渡せます。Popupは`context.PopupOwner`で開き、開いたIDから`GetPopupAutomation(id)`を取得します。Desktop/BrowserのPNG取得は要求時に実際の合成フレームをreadbackし、その対象の可視領域を切り出します。提示imageのCopySourceまたはRGBA8 sRGB中間画像のreadbackが利用できない場合はUnsupportedを返します。

Diagnosticsは別packageの`UseDiagnostics`で追加します。入力・テキスト入力・値の閲覧・画像取得の許可はそれぞれ既定falseです。通信は既存のAgent/transportを明示登録した場合だけ開始します。

独自contentは`IUiApplicationContent`で初期化・Advance・停止・Retire・Releaseを実装できます。GPU資源を借用する内容は`IUiApplicationGraphicsContent`で世代更新を確認します。Retire失敗は資源を保持するStopFailedとなり`RetryStopAsync`で再試行できます。最終Release/DI解放の失敗は再実行しません。

詳細と後続PRの範囲は[ADR UI-0016](../../../docs/adr/ui/UI-0016-ui-application-framework.md)を参照してください。

`UiApplicationOptions.Motion`でFull／Reduced／Disabled、同時演出数、snapshotのpixel／byte予算、準備期限と実時間の最大寿命を設定できます。通常の移動・拡大は保持したDrawListを再生します。半透明groupには更新可能なRGBA16Float中間bufferを使い、opacityを一度だけ適用します。内容変更で演出を中断せず、同じ寸法のbufferは再利用します。旧ページのexitや異なる見た目のcrossfadeに限って旧snapshotを保持します。byte予算はtexel payload（幅×高さ×8）で、backend内部のallocation paddingは含みません。layerの中間bufferとGPU退役待ち画像も予算に算入します。移動だけのlayerは画像予算を消費しません。

Hosted Popup／Windowの終了では先に入力・IMEを止め、内容のexit、最終提示、GPU drain、既存のWindow清掃と`onClosed`の順で進みます。停止・device loss・Native Window終了では演出を省略してdrainへ進みます。OS Windowの位置やopacityをアニメーションする機能は含みません。詳細は[UI-0021](../../../docs/adr/ui/UI-0021-connected-and-presence-transitions.md)を参照してください。

## 2D保持描画

Desktop／Browserの通常frameは、Presentationごとの`UiDrawingScene2D`を更新し、変更されていないローカル`DrawList2D`とGPU資産を再利用します。追加の設定は不要です。Hosted／Nativeの補助Windowにも同じ経路を使用します。配置・clip・DPIは内容とは別に合成し、毎frameの提示確認は省略しません。

Presence／ページ遷移などの特殊合成は既存の描画をopaqueな内容として毎frame再記録します。候補の検証も保持sceneのsnapshotを使用します。visual画像のcapture/updateは特殊合成用の不変fragmentを保持sceneとして準備し、通常frameと同じ保持frameの退役規約を使用します。独自の`OnRecord`は既定では毎frame実行し、明示的な`CacheRetainedDrawing`の契約だけを再利用します。

非同期処理へ渡すのは内容を共有する不変の配置snapshotで、UI／sceneのborrowはGPU準備前に解放済みです。GPU完了と提示image解放の確認後にcommand memory、保持frame、submissionの順で退役します。送信成否が不明な場合はdevice世代の退役確認まで保持し、device再生成時はCPU記録も作り直します。Headlessを含む全内部driverは保持snapshotだけを受け取り、旧フレーム全体のDrawList生成・キャッシュ・direct送信分岐はありません。

## キャプチャ診断（明示的な有効化）

既存provider `Lumyte-UI-Application-Profiling` のkeyword `0x10`に、event 12の`CaptureState(requestId, profileRunId, frameId, sourceId, phase, reason, processingCount, owned, drawingBusy)`を追加します。従来の`0xF` filterの意味は変更しません。IDは診断専用で、dequeue・再試行・重複除去・取消しには使いません。

- phase 1：dispatcherへ渡す前のhost要求。2：dispatcherへ到達。
- phase 3：driverのqueueへ到達。現在の描画keyとbusy状態を含みます。owned Windowは外部Window用fieldではなく、application Entryの描画task／keyを読みます。
- phase 4：送信queue待ち前に、処理対象frameと関連付けます。同じ要求が再処理された場合も`processingCount`が増えます。
- phase 5／6：crop＋PNG処理の開始／終了。7：処理拒否／失敗。8：host成功。9：取消し。10：hostエラー。終了時にframeが不明なら空／zero keyを維持し、時刻の近さではなく`requestId`で結合します。
- reason 0：未指定。1：hostへ渡されたtokenの取消し（上流automationのlinked tokenの場合もあり、caller／deadlineの区別を意味しません）。2：application停止。3：その他／不明。4：結果が既に完了。5：対象が非表示。6：readback非対応。例外本文は出力しません。

既存stage 14（submission待ち）、15（image解放）、29（readback）は変更しません。新たな内側stageは30（map待ち）、31（CPU copy／unmap／repack）、32（crop）、33（PNG encode）です。CPU側の経過scopeであり、GPU timestampではありません。stage keyword `0x2`とcontrol `0x1`が既存のframe／run／spanを結合します。Captureだけでもframe keyは生成しますが、全stageを出力するわけではありません。

Browserサンプルの共有EventListenerは`EnableCaptureDiagnostics(sink)`の明示呼出しでだけ接続します。`runMain`より前に有効化し、要求以前から開いているstageも有界に追跡します。queue／matched frame keyで関連付けたstageと要求の状態をmetadata専用の`Action<string>` callbackへ渡し、queue到達時には既に開いているstageを再掲します。callbackのprefixは`UI Capture trace:`と末尾の半角spaceで、その後ろは`{eventId,name,ticks,frequency,data:{payloadName:scalar}}`です。Guidは文字列として出力します。通常のbrowser consoleにはイベントを出しません。capture runnerは`Runtime.addBinding`で`__lumyteCaptureDiagnosticEvent`をnavigation前に登録し、`payload => globalThis.__lumyteCaptureDiagnosticEvent(payload)`というwrapperを渡して呼出しreceiverを維持します。Node側で有界に保持し、外側のcapture失敗時だけ診断を表示するため、browser側snapshot取得が停止しても既受信分を報告できます。callback省略時の共有observerはsnapshotのみを保持し、callback例外はapplication結果や終端記録を変えません。

`GetCaptureDiagnostics()`はJSONを返します。`enabled`、`dropped`、`streamed`、`events`（直近64件）、`requests`（最大16 IDの最終状態）、`relations`（処理中の要求とframeの組）、`lastStages`（4 frame）、`openStages`（最大4 frame・各8 span）を含みます。callback streamは2048件が上限ですが、要求の終了記録はこの上限を超えて出力し、ringとは別に`requests`へ保持します。`dropped`は有界状態／ringの退避やstream抑制の件数であり、EventPipeのlossではありません。空／zero IDは未関連のまま扱います。

hostの終了記録にframe zeroが入る場合でも、liveな関連を解放する前に`terminalFrames`へ`{requestId,profileRunId,frameId,observation:"atTerminal",openStages,lastStage}`を保存します。資源参照ではなく、終了を観測した時点のscalar event JSONです。内側の`openStages`は終了時点で開いていたstageであり、snapshot取得時にも開いているという意味ではありません。top-levelの`relations`と`openStages`はlive状態です。終了時にはpinを解除するので、古い要求が新frameの追跡を妨げません。archiveは16要求の退避寿命と16,384文字の別予算で制限し、退避を`dropped`へ数えます。最後の要求終了記録はarchive／ringの退避とは別に保持します。
