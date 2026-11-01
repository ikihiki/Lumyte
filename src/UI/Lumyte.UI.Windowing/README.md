# Lumyte.UI.Windowing

通常画面を借用登録し、独立したUiSurfaceのPopupを注入managerで表示します。WindowSystem、Platform、通信への依存はありません。内容は既存のComposition静的関数で構築し、callbackは要求パラメーターに渡します。

```csharp
var windows = new HostedUiWindowManager();
using var root = windows.Register(source); // IUiWindowSourceをホストが実装する
// 非同期処理中も通常frameでwindows.Advance()を続ける。
await using var drawing = await windows.BindPresentationAsync(root.Window, surface);
var popups = new UiPopupManager(windows);
using var owner = popups.RegisterOwner(root.Window, surface);
UiPopupHandle menu = popups.RequestOpen(owner, new UiPopupRequest(
    anchor, new Vector2(200, 120),
    createContent: () => Stack()[Button(onClick: Save)],
    onClosed: result => Report(result)));
```

`using static Lumyte.UI.Compose;`で構築関数を使います。示したusing宣言だけで終了処理は完了しません。以下のdrain順を守ります。

frame順はOS/Input取得→window manager.Advance→UiPopupManager.Dispatch/Advance→通常layout/描画→提示済みrevision通知です。既存の取得時InputWindowIdをUiWindowInputEnvelopeへ渡し、別Windowの終端にも同じpointer identityを使います。取得履歴の重複・欠落・device取消の変換はホストの責務です。Originの推定・broadcastは行いません。

IUiWindowSourceは既存のNative client／canvasとInputOriginを明示して借用登録します。PreparePresentationAsyncは非表示の描画登録を返し、Updateで位置・clip・DPI・IsVisibleを反映します。実際に提示したGeometryRevisionだけをPresentedGeometryRevisionへ返してください。Hostedのroot、Popup、候補層を同じ描画先へ順序通り合成します。位置・clipは提示root論理座標、surface内の入力はlocal論理座標です。

Hostedはroot寸法へclampし、希望側→反対側→root内clampで配置します。submenuは小さい親menuのclipを継承せずroot直下へ表示します。Passiveは入力透過、Candidateはkeyboard focusを変更しない予約済みpointer層です。候補controllerは後続IME PRで追加します。

`FallbackUiWindowManager(native, hosted)`は両managerを借用し、専用利用します。通常rootの描画は一回だけ登録します。PreferNativeは表示前の失敗だけを、元方式の準備・描画・GPU・Native Close完了後にHostedへ切り替えます。同じsurfaceと公開IDを維持し、内容factoryは一度です。Show発行後は自動移送しません。RequireNativeは失敗を報告します。

PopupはPending→Opening→Open→Closing→Closed、開けなかった場合は清掃後Failedになります。factoryと開閉callbackはそれぞれ一度だけです。callbackのopen/closeは次のdrainで処理します。callback例外でも他の通知を処理してからAggregateExceptionで報告します。再入Advance/Dispatchを避け、全処理をUIホストの一つの実行系列で直列化してください。UIのthread IDやlayout phase検査は追加しません。Native接続には既存backendのスレッド条件も適用されます。

Menuは最深surfaceへkeyboardを配送し、Widgetが処理したキーを重ねて認識しません。未処理の上下/Home/Endでfocus移動、Escape/Leftで最深menuを閉じます。Enter/Spaceは通常のButton入力です。外側Downは背面へ渡さず系列終端まで消費し、親menu内へのDownは子だけ閉じて一度配送します。開く原因のUpを新menuへ移しません。focus bookmarkはsurfaceと接続IDを保持し、OSをActivateしません。

終了はpopupの入力停止→BindLifetime参加者のDisposeAsync完了→描画bindingのGPU退役→surface破棄→生成Window Close確認→Releaseです。終了参加者とpresentationは、失敗を報告する場合も借用資源の使用停止・退役を終えてから完了する必要があります。ネイティブCloseが失敗して実Windowが残る場合は終了待ちを継続します。元のsource/Windowを先に破棄しないでください。

アプリ全体はpopups.BeginStop→frame/Advance継続→popups.IsStopped→popups.Dispose→root drawingのDisposeAsync/drain→root.Dispose/IsReleased→windows.BeginStop/Advance/IsStopped/Disposeの順です。managerは借用UI/source/inner managerをDisposeしません。Dispose前のdrain不足はInvalidOperationExceptionです。Closed/Failedの生成IDはReleaseまで観測でき、再利用しません。

[ADR UI-0011](../../../docs/adr/ui/UI-0011-popup-windowing-and-fallback.md)／[manager契約](../../../docs/adr/ui/UI-0014-injectable-window-manager.md)／[実行サンプル](../../../samples/Lumyte.UI.Popup.Sample/README.md)

ホストのUI/Game所有表は、DownでUIが消費した系列だけをUIへ終端まで戻し、UIが辞退した系列をGame側に維持します。通常画面で辞退したDownをmanagerがUI所有として保持することはありません。Closing中のMenu scopeは清掃完了まで新規入力を消費し、背面へ漏らしません。

IUiWindowSourceAdapterはfallbackの描画ID/revision変換を維持する透過境界です。Native実装はInnerSourceを辿って実backend sourceを調べ、新しい物理sourceにもAdaptSourceを適用します。source/presentationへ公開WindowIdと単調なGeometryRevisionを渡し、提示ackとHitTestも同じrevisionで照合できます。

通常Hosted画面をpointerで選ぶ場合は論理keyboard targetを更新し、Popup/Passive/Candidateの表示では変更しない。借用rootは最小化・非表示・0寸法になっても登録と描画bindingを維持し、Popupだけを閉じる。復帰後は同じroot IDで再び開ける。
