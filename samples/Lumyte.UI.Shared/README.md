# Lumyte.UI.Shared

UIサンプル専用の非配布補助です。UI本体の依存を増やさず、Input・Platform・Windowing・Drawing2Dを接続します。

- `UiHostInputRouter`は、加工後のordered sourceと生成元のWindow mapを借用します。両者のStreamIdは一致させます。Window／surfaceの生成・破棄はホストが管理します。
- `Recorded`で記録をqueueへ保存し、InputSystem.Updateが成功して戻った後に取得順でDispatchします。履歴から再開する場合はReadRecordsのHasGap／HasAcquisitionGapで影響するoriginをResetします。
- Attach後、初回と表示更新後のUpdateLayoutを成功させてから通常入力を渡します。layout・入力callbackの例外時は通常入力と描画を停止し、Reset／Detachで所有状態を清掃します。surfaceのDisposeより先にDetachします。
- `UiHostDrawing`は、Hosted原点と祖先clipをNative論理空間で合成します。rootを先に描き、GetHostedOrderの背面から前面へ各surfaceを記録します。入力にDPIを再適用しません。
- `UiHostPresenter`はサンプルのGPU提示を担当します。RGBA16FloatからUNORM表示先へ変換し、毎frameの完了を待ちます。

標準Platformの取得対象はkeyboard/mouseです。touch/penのrouter変換は統合fixtureで検証し、OSの取得能力とは分けて扱います。文字・IME、Popup manager、dragのsurface間移動は後続PRの対象です。

## 親RenderGraphとResources

`UiGraphFrame.AppendAsync`は呼び出し元のgraphへ2D passを追加します。UI用の内側graphは作りません。呼び出し元がtargetを一度だけimportし、同じgraphの`DrawingGraphBindings2D`、`PixelScale=1`のtarget、任意の`ResourceReference<Renderer2DResource>`を渡します。通常の`UiHostDrawing.Record`は`RenderGraphCanvas2D`にも利用できます。

frame ownerはpreparedとrendererだけを所有します。GPU完了後、command、compiled plan、frame owner、Reference、DI所有者の順に返却します。preparedの解放に失敗した場合はrendererを保持し、再度Disposeして解放を試せます。Append失敗時は親graph全体を破棄し、以前に成功したframeは別に返却します。

`UiSampleResources`は既存のAddGraphics2Dと公開pipeline keyを使うDI設定例です。Manager構築前にpipelineを設定し、host所有のdeviceを借用します。Browserではcooperative CPU実行を選び、初期化・取得・終了中のpumpを補助内で処理します。renderer作成時に既存の世代leaseを取得し、frame中の参照を差し替えません。独自のUI resource providerやUI本体の依存は追加しません。

サンプルは毎frame、取消tokenを渡さずにGPU完了を待ちます。並行frameや取消可能なホストでは、Waitの取消を完了と扱わず、実際の完了またはdevice退役までframe・plan・command・外部資源を保持してください。device復旧は既存のIGraphicsDeviceServiceとRebuildRuntimeAsyncを使い、新しいtarget/bindings/rendererを作ります。CPUのUiSurfaceは維持できます。自動復旧を行う常駐coordinatorはこのサンプルに含めません。

[設計と所有権](../../docs/adr/ui/UI-0008-render-graph-and-resource-lifetime.md)／[実行と画素比較](../Lumyte.UI.Sample/README.md)／[Browserの選択方法](../Lumyte.UI.Browser.Sample/README.md)
