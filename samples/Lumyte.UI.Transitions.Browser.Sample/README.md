# Browser UI transitions sample

`dotnet publish samples/Lumyte.UI.Transitions.Browser.Sample -c Release -o artifacts/browser/ui-transitions`で出力し、`wwwroot`をHTTP配信します。GPU、capture、frame更新と終了処理は標準Applicationが管理します。

Chromiumを用意し、`node samples/Lumyte.UI.Transitions.Browser.Sample/verify-browser.mjs artifacts/browser/ui-transitions/wwwroot`で検証します。重なった子のgroup alpha、Hosted Popupの退出とonClosed、共有要素の位置と半透明crossfade、背景混入と二重描画の防止、演出中の入力gate、旧DI scopeの保持、Back/ForwardとSkip、URL/historyの維持、終了を通常のPNGと入力経路で確認します。

Browser probeは検証専用です。clockだけを動かし、Surfaceの更新や描画を直接呼び出しません。PNGは通常のAutomation screenshotを使います。実装と登録は[共通scene](../Lumyte.UI.Transitions.Sample/README.md)を参照してください。

`--sequential`を検証scriptの末尾に付けると、同じsceneをExitThenEnterで実行し、旧ページのfade後に新ページがfadeする画素も検査します。ヘッドレスChromiumにはフォーカスを明示し、注入したHosted managerのWindow準備時計だけを固定します。これはSwiftShaderの初回描画が既存の2秒期限へ影響しないための検証設定で、アニメーションの準備・最大寿命は通常の実時間で測ります。

画素検証のviewportは240×180です。汎用2D shaderによる全画面画像の合成はSwiftShaderでは重いため、実GPUでのframe性能測定とは分けています。完全透明なlayerは描画commandとGPU借用を生成しません。

`--same-content`ではsnapshot予算を一pixel・8 bytesに制限し、同じ見た目の共有要素が画像確保なしで移動することを検証します。通常/`--sequential`ではfade途中の色Signal変更と、入力gate中の新ページの色変更を確認します。保持layerの内容更新で演出を終了・再開始しません。

追加の内容更新を含むManualClock検証では、ソフトウェアGPUの処理時間と演出の進行を分けるため、sampleの最大実時間寿命を180秒にしています。通常アプリの既定値は2秒のままです。
