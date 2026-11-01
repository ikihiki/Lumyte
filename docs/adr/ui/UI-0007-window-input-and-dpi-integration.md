# ADR-UI-0007: Windowを明示する入力配送とDPI・Hosted接続

- 状態: 採用
- 日付: 2026-10-27
- 対応: 実装PR 6、前提: PR 3・5、同時実装: INPUT-0007

## 背景

[UI-0004](UI-0004-drawing-and-clipping.md)の描画と[UI-0006](UI-0006-focus-and-keyboard.md)までの入力を実Windowへ接続する。従来のInputRecordはNative Windowの出所を保持せず、入力履歴を後から現在のactive Windowへ割り当てるとイベント時点の配送先が変わる。[INPUT-0007](../input/INPUT-0007-window-associated-input.md)で追加する取得元Window・系列owner・取得順序をUI配送へ引き継ぐ。

またWindowState.ScaleはVector2、Drawing2DのPixelScaleはfloatなので、両方へDPIを設定すると二重変換になり、非一様scaleも表せない。Hostedの位置、祖先clip、captureをsurface間で一貫して扱う必要がある。

## 決定

### 実装範囲と配置

UI本体への公開API追加はない。Nativeの`samples/Lumyte.UI.Sample`を実入力対応にし、Browserは`samples/Lumyte.UI.Browser.Sample`へ置く。プラットフォーム非依存のサンプル補助を`samples/Lumyte.UI.Shared`に置き、UI/Input/Windowingの接続と検証を共有する。これらはNuGetパッケージにせず、Platform・InputSystem・Windowingへの参照をUI本体へ追加しない。Platformの取得側capture・位置記録と、INPUT-0007のInput/Processing/Actions/Diagnostics・標準Platform移行もこのPRへ含める。

テストは`tests/UI/Lumyte.UI.IntegrationTests`で共有補助を参照する。native実入力が利用できないCIでも、window状態と出所付きInputRecordのfixtureで配送規則を検証する。API diffの比較元は[UI-0001](UI-0001-composition-based-ui-system.md)と同じで、以下はサンプル内部の主要契約である。

```diff
+namespace Lumyte.UI.SampleSupport
+{
+    internal sealed class UiHostInputRouter
+    {
+        // 借用する加工後sourceと取得元map。同じStreamIdでなければArgumentException。
+        internal UiHostInputRouter(IOrderedInputSource source, IWindowInputSource windowMap);
+        // 全て借用。登録の重複・別presentation rootとの不整合はArgumentException。
+        internal void Attach(IWindow window, UiSurface surface);
+        internal void Attach(WindowNode node, UiSurface surface);
+        // 変更前に旧surfaceを取消。WindowやsurfaceをDisposeしない。
+        internal void Detach(UiSurface surface);
+        // 一つのInputSystemの記録順。重複は無視、逆行はInvalidOperationException。
+        internal bool Dispatch(InputRecord input);
+        // 当該originの所有状態を取消し、キー/位置のcacheも消す。他Windowは維持。
+        internal void Reset(InputWindowId origin);
+    }
+    internal static class UiHostDrawing
+    {
+        // Begin済みCanvas。scaleとHosted原点・祖先clipを一度だけ合成する。
+        internal static void Record(DrawingCanvas2D canvas, IWindow window, UiSurface surface);
+        internal static void Record(DrawingCanvas2D canvas, WindowNode node, UiSurface surface);
+    }
+}
```

### 出所の制約と入力変換

標準PlatformのIWindowInputSourceから取得したInputWindowIdをOriginとする。Attachは取得元mapのTryGetInputWindowIdでNative/canvasの生存中IDを解決し、InputWindowIdと提示rootの対応を保持する。Hostedは祖先の提示rootへ結び付け、別の取得IDを作らない。物理deviceの複製や、InputRecord.WindowIdの後付けはしない。加工wrapperのIOrderedInputSourceと生成元mapを別途借用し、StreamIdの一致を検査する。

入力はInputSystem.Recordedの記録順を保って一度配送する。同じsourceのkeyboard/pointerをdevice別に並べ直さず、Captureをsort keyにしない。Recordedの収集中はInputRecordをqueueへ保存し、InputSystem.Updateが戻った安全な境界でUIへ渡す。後続の文字取得より前にfocus同期が必要な場合は、取得列のprefixだけをその境界まで進めて配送する。履歴を再読する場合は前回Sequenceからの非破壊読取を使い、retentionのHasGapまたはHasAcquisitionGapがあれば影響するsourceの全OriginをResetする。

sourceの能力不足、null/未知/退役済みOriginは新しいUI操作として配送せず、broadcastや現在focusによる補完をしない。既存ownerの終端に出所不明・退役済みの記録が来た場合は、そのownerをCancelして残りを消費する。Windowの終了後はmapを除き、同じOS handleの再利用を別InputWindowIdとして扱う。

| InputData | 変換・保持する状態 |
| --- | --- |
| MouseMoveData | 取得Origin・DeviceId別の最後の論理位置を更新し、系列ownerへMoveを配送 |
| MouseButtonData | LeftのみDown/Upへ変換。取得元が直前に記録した同時点のMouseMoveDataを位置に使う |
| MouseButtonCanceledData | Leftの既存系列へCancelを配送。位置を参照せず、古いinside座標でUpへ変換しない |
| TouchData | Began/Moved/Ended/CanceledをDown/Move/Up/Cancelへ変換 |
| PenData | contactのDown/Upを使用し、接触中MoveとCanceled/Leftを追跡。hover移動は操作を開始しない |
| KeyData | 左右の修飾キーのDown/Upを追跡しUiKeyInputへ変換。Unknownは配送しない |
| FocusData(false) / WindowInputClosedData | StateWindowIdのscopeをResetし、対応surfaceへFocusLost。他Windowを維持 |
| DeviceDisconnectedData | 当該deviceが所有する系列を全Originで取消。他deviceの系列を維持 |

pointerIdは`(DeviceId, 種別, contactId)`へ割り当てる非負longの連番とし、数値のhashで混合しない。取得Originが途中で変わっても同じ物理pointerであり、開始時のownerを別に保持する。mouseはdevice単位で維持し、全button終端まで一つのownerを共有する。touch/penは終了・取消後にmappingを解放する。IDはrouter寿命内で再利用せず、overflowは状態変更前に失敗する。UIの初期対象外のwheel、pressure、controllerはfalseとする。

### 取得側captureとmouse取消

surface/router内のcaptureは配送先を固定するものであり、Window/canvas外のOS入力を取得する機能とは分ける。単一Nativeでも取得側captureが必要である。Platform sourceは最初の物理mouse button Downの取得中にcaptureを確立し、成功後にそのDownを公開する。UIのframe配送や入力消費結果を待って開始しない。mouseの複数buttonを追跡し、最後のbuttonがUp/取消になるまで同じ取得先を保持する。正常な最終Upでは先に内部押下・capture所有状態を終了させてからcaptureを解放し、それに伴う喪失通知で二重取消しない。

| 取得元 | 開始・位置・終了の契約 |
| --- | --- |
| Windows | DownのWindowへSetCaptureし、GetCaptureで取得成功を確認する。領域外のWM_MOUSEMOVEとbutton Upも取得元Windowのclient論理座標へ変換する。WM_CAPTURECHANGED/WM_CANCELMODE、Window終了・focus喪失で未完了buttonを取消し、自身のcaptureだけをReleaseCaptureする |
| Linux/X11 | 最初のDownでXGrabPointerの成功を確認し、取得元Windowへのmotion/button releaseを要求する。X11のイベント座標を同じclient論理空間へ変換し、最終button終了でXUngrabPointerする。Windowの非表示・終了、focus喪失・接続喪失で未完了buttonを取消す |
| Browser | mouseはPointer Eventsへ統一してpointerdown中にcanvas.setPointerCaptureを行う。取得失敗・lostpointercapture・pointercancelを追跡する。buttonsの差分で複数buttonのDown/Upを復元し、互換mouseイベントと二重記録しない。領域外のclientX/Yもcanvasの現在の矩形から論理座標へ変換し、最終button終了で解放する |

Down/Upにはそのイベント自身の座標を使い、sourceの取得列へMouseMoveData→MouseButtonDataを同じWindowId/CaptureSequenceで連続して記録する。位置に変化がなくても先行位置記録を省略しない。領域外座標をviewport内へclampせず、位置なしのUpを最後のcanvas内Moveと組み合わせない。capture中も正確な位置を取得できなくなったらUpを推測せず取消す。独自source/fixtureにもこの位置契約を要求し、位置が不明なDownは開始せず、既存系列の位置が不明なUpはCancelとして消費する。

取得失敗時にまだ公開していないDownは公開せず、既に公開したbuttonがあれば取消データを一度記録する。取得側は失敗・喪失後の物理状態を追跡し、その系列の残りのDown/UpをUI/ゲームへ公開しない。全buttonの解放を観測して中立を確認するまで新しい系列を開始しない。実際のfocus喪失は既存FocusData(false)も記録するが、captureだけの喪失でkeyboard focusまで失わせない。Browserのページ非表示・canvas削除も取消とcapture清掃を行う。

### 取消データと入力基盤への反映

既存MouseButtonData(false)は正常なUpなのでClick/Release認識を起こし得る。FocusData(false)はWindow scopeのfocusを失わせるため、button単位のcapture取消には使わない。Input.AbstractionsのMouseButtonCanceledDataは[INPUT-0007](../input/INPUT-0007-window-associated-input.md#id取得列の公開境界)で定義する。位置不要・解放済みなら状態変更なし・他button/key維持とし、不正enumはInputSystemへの取り込み前に拒否する。

InputSystemは取消を記録して当該buttonの論理押下を解除し、Actionsは関連するrecognizerを発火なしで中断する。取消はInputRecord.Dataの型を保ち、Recorded通知とGetRecords/ReadRecordsの履歴取得で通常Upへ変換しない。Input.DiagnosticsのInputInspectionSession.CreateSnapshotには、取消後のdevice状態とlatestSequenceを反映する。routerは当該pointerの`UiPointerInput(pointerId, UiPointerPhase.Cancel, Vector2.Zero)`へ変換し、[UI-0005のlayout不要の取消経路](UI-0005-pointer-dispatch-and-capture.md#dispatchと取消)を使う。これらの対応をPR 6の受け入れ条件とする。

取得側のOS/DOM操作と所有表はPlatform内部へ置き、UI向けのpublic OS capture APIは追加しない。複数Nativeの出所はINPUT-0007で保証する。跨Windowの系列は同じpointer ownerへ戻すが、座標は取得Originの空間として扱う。このサンプルrouterは異なるNative間の座標変換を提供せず、別OriginのMove/Upを受けた既存系列はCancelして終端まで消費する。取得元がcapture先のclient空間で届ける通常の領域外操作は同じOriginのまま継続する。

### Hosted・surface間の配送

capture開始前のpointer Downではpresentation rootのHitTestへNativeクライアント論理座標を渡す。選ばれたWindowNodeにsurfaceがなければ、背面のsurfaceへ透過せず配送しない。選択先の祖先Boundsを合成した原点を引いてsurfaceのローカル位置へ変換する。

DownがUIに消費された場合、routerは物理pointer系列を当該surfaceへ固定し、Move/Up/Cancelでは再hit testしない。UIが途中で押下を取り消しても、routerは物理Up/Cancelまで系列の消費を維持し、ゲームへUpだけを流さない。対象surfaceがDetach・非表示・終了した場合はUIへFocusLostを渡し、その系列の後続を終端まで破棄する。surfaceから別surfaceへドラッグを移す機能は対象外。

hostは初回・表示更新後にUpdateLayoutを成功させてから通常入力を配送する。Dispatchはlayoutや入力情報を更新しない。UpdateLayout失敗時は描画・通常入力を停止し、消費済み系列を再送・ゲームへ転送しない。入力全体を停止する場合はResetからlayout不要のFocusLostを配送する。取消・終端の清掃はfinallyで行い、callback例外でrouter側の所有状態を残さない。

capture中もHostedの祖先clipは判定する。祖先clip外だが対象surfaceの矩形内にある位置では、Moveを必ずviewport外となる(-1,-1)へ置き換え、UpはCancelへ変換する。これで見えていない領域のUpによるClickを防ぎ、Moveで領域内へ戻れば通常位置の配送へ戻せる。元の位置はhostのcacheへ保持し、UI向けの補正位置で書き換えない。初期UIには一般のpointer座標イベントがないため、この補正を外部イベントの座標として公開することはない。

keyboardの新しいDownは取得Originの提示rootを選び、そのroot内の論理keyboard対象へ配送する。先行pointerを処理した結果のHosted対象を使い、pump終了後のactive Nativeや別rootのWindowSystem.KeyboardTargetで取得Originを置換しない。論理対象の切替時は旧surfaceへFocusLostを渡す。Downを消費したキーは対応Upまでその配送先へ固定し、別OriginのUpもStateWindowIdと既存ownerで解決する。途中で配送先を失った場合はUpまで破棄する。WindowのActivate/BringToFrontはホストが要求し、UI focusの変更から暗黙に呼ばない。

Popupは[UI-0011](UI-0011-popup-windowing-and-fallback.md)と[UI-0014](UI-0014-injectable-window-manager.md)の実装PR 10で、注入したIUiWindowManagerへ接続する。managerは取得InputWindowIdから提示rootを解決し、このPRの系列・取消規則を引き継ぐ。Menuが開いている間だけ論理keyboard scopeを優先し、PreserveFocusのWindowSystem.KeyboardTargetは変更しない。Native popupは取得元map・領域外入力・座標変換等の実能力が揃う場合に使用し、Browserまたは能力不足ならHostedへフォールバックする。

### frame順とDPI

NativeはPumpEvents→WindowSystem.Update（使用時）→InputSystem.Update・収集→入力配送→UpdateLayout/Record→GPU実行・提示を基本順とする。pump中にfocusを変え得る入力を取得した場合は、次の文字取得より前に取得済みprefixのInputSystem更新・配送・focus同期を完了する。そこからPumpEventsを再入しない。BrowserもDOM callbackの取得順と同じ境界を保ち、requestAnimationFrameで残りの更新・描画を行う。UI処理を別threadへ移さない。

`surface.SetViewport(window.State.ClientSize)`とし、描画はCanvasへ`local → Native論理座標 → Vector2 Scale`の変換を一度適用する。Drawing2Dのtarget/bindingはPixelScale=1を明示する。非一様scaleもCanvas行列だけで表す。入力は既に論理座標なのでDPIで再除算しない。physical pixel入力を持つ独自ホストだけが取得record作成前に一度変換する。

viewport変更時のrootへのtight制約、無効化、Size/Boundsの確定は[UI-0003](UI-0003-layout-and-invalidation.md)に従う。ホストはlayout後の子サイズを直接変更せず、論理サイズの変更をSetViewportへ渡す。DPIだけの変更では同じ論理制約を再layoutする必要はない。

HostedはrootのNative論理空間で祖先Boundsの原点とclipを合成する。root内容を先に描き、GetHostedOrderの背面→前面順で各surfaceを記録する。Hosted祖先を辿ってもNative境界は越えない。canvasのSave→DPI変換→Native論理clip→Hosted原点→surface.Record→Restoreという順を保証する。

PixelSizeが0、非表示、Closing/ClosedのWindowは描画・新規押下を止める。Scaleがfiniteかつ正でない観測値も描画を止めて報告し、推測値で描かない。resize/DPI変更は新しいtargetとviewportを次frameで適用する。終了は入力停止・Reset→GPU退役確認→graphicsの保持解放→surface.Dispose→Window終了・Platform破棄の順で進める。

## 検討した代替案

DeviceIdやactive Windowから配送先を推定する案はイベント時点の出所を復元できない。UIとrenderer双方へのDPI設定は二重変換になる。初期は出所を明示し、DPI変換をホストのCanvas行列だけへ集約する。

## 結果と影響

Native/HostedとUIの境界を検証でき、UI本体の依存を維持できる。複数Nativeでは取得元・状態owner・UI対象を区別し、他Windowの入力を壊さず配送できる。一方、legacy sourceはWindow情報を推測して接続できず、標準Platformの移行と実機確認が必要になる。サンプルの対応範囲とfixtureで確認した規則をREADMEで区別する。

## 検証方針

- 二つのOriginを同じ物理DeviceIdが跨ぐfixture、位置未取得、Sequence重複・欠落、出所不明、退役ID・OS handle再利用、Detach後のUpを確認する。Aのfocus喪失・終了がBの押下を解除せず、別Originの終端が元のownerだけを終了することを確認する。
- 同一pumpのkeyboard→pointer→keyboardがdevice登録順によらず取得順で配送され、加工失敗中の別deviceが追い越さないこと、focus同期のprefix flushが再入・二重配送を起こさないことを確認する。
- 単一Native/BrowserでButtonを押してWindow/canvas外へ移動・解放し、Clickが発生せず押下が残らないこと、外から戻って解放すれば一度だけClickすることを確認する。canvas端を覆うButtonで外側Upの古いinside座標を再利用しないことも確認する。
- 取得側captureの失敗・喪失、複数buttonの途中Up、正常解放に伴う喪失通知、Window終了・canvas削除で、取消が一度だけ記録され正常Up認識へ変わらないことを確認する。位置の先行記録、Input履歴・Recorded通知での取消型の保持、Actionsの発火なし取消、Diagnostics snapshotの状態・sequenceの一致、dirtyなlayout失敗後のrouter清掃も確認する。
- 入れ子の原点・clip、z順、背景への透過拒否、領域外capture、keyboard target切替を確認する。
- (1,1)、(1.5,1.5)、(2,1.25)、resize、0pixelで描画境界と入力座標が一致することを確認する。
- Windows/Linuxの複数NativeとBrowserで実操作・focus喪失・終了順を確認する。Browserの複数surfaceは同じcanvasのOriginを使い、非活性Native popupのpointerをkeyboard focus不足だけで捨てないことを確認する。利用できない環境は未検証として記載する。
- Browser publish/trimmingと利用可能なNative AOT構成を既存ビルドrunnerへ登録し、公開型の静的構築とGeneratorの成果物が実行できることを確認する。

## 実装と検証の記録（2026-10-29）

Native・Browserサンプルと非配布のShared補助を追加した。`UiSurface`の公開API・依存は変更していない。描画先のBGRA/UNORM形式はDrawing2Dの直接描画対象ではないため、サンプルの`UiHostPresenter`がRGBA16Floatの中間画像からUNORM swapchainへsRGBを一度encodeする。targetはPixelScale=1とし、Windowの非一様scaleはCanvasだけへ適用する。このpresenterは各frameでGPU完了を待つ検証用の実装であり、frame並列化・画像再利用は提供しない。

統合fixtureで取得列、scope状態、ackと再試行、系列取消、Hosted clip、DPI行列を検証した。実BrowserではDOM Pointer Eventsのcapture、canvas外Up、Hosted対象の選択、キー操作、DPI×2と終了を、GPUを使わないCPU記録モードで検証した。NativeのWindows/X11実機操作とBrowserのGPU提示は別の確認項目である。この環境では最小のJavaScriptだけでもWebGPUの`mapAsync`が`A valid external Instance reference no longer exists`で失敗するため、GPU提示の成功は未確認とする。手順と検証の区別は[Nativeサンプル](../../../samples/Lumyte.UI.Sample/README.md)と[Browserサンプル](../../../samples/Lumyte.UI.Browser.Sample/README.md)に記録する。

PR 6はkeyboard/mouseの標準取得とtouch/penのfixture配送を含み、OSの文字取得・IMEを含まない。文字取得前のprefix flushを実プラットフォームへ接続する処理は、文字サービスを導入するPR 11で実装する。現在のsampleはPump/Updateが戻ってからRecordedのqueueを配送する。

検証結果は、Release 578件（UI 178、UI統合25、Input 20、加工・Actions 109、Platform 16、Diagnostics統合131、Drawing2D 99）、DebugのUI 178件とUI統合25件、Browser取得のNodeテスト8件。Platformの実機6件は環境不足でskipした。8パッケージのpackとProjectReferenceを持たないNuGet consumerも確認した。未確認のGPU提示・実機・AOTをこれらの結果へ含めない。

## Element基盤への接続記録

2026-10-31、#82のWindow取得情報・順序・DPIの契約を維持し、押下・focus・配置の
参照先をElementへ変更した。UI本体からWindow管理やInputSystemへの依存は追加していない。
統合28件、BrowserのWindowing JS 8件が成功し、Browser sampleのRelease buildと
Native sampleのrecord-only実行を確認した。OS上の入力検証やGPU提示の完了を意味しない。
