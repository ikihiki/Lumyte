# 容量制限付きMVTのBrowser採用サンプル

Nativeサンプルから手作り試験データをリンクして使用します。OSMのダウンロード、ローカルファイルからのタイル取得、HTTPタイルsource、無制限の空間ストリーミングは使いません。有限の6候補を可視集合と整数取得zoomで選び、ドラッグとポインター基準のホイールズームを利用できます。Browserホストはアプリ資産をlocalhost経由で配信しますが、地図タイルの配信ではありません。

contentは公開`IUiApplicationContent`のhookで`MapSession`を所有し、Resourcesがデコード済みCPU世代を所有します。専用の`InMemoryMvtRegistration`は入力・予約制限を検査してから各タイルをコピーし、SHA256 revisionを固定します。sourceは非公開の不変領域を読取り専用snapshot間で共有します。登録が先行読込みしたタイル領域はアプリ所有であり、sessionごとのdecoder／常駐予算とは別に必要です。profileはprocess全体の上限ではありません。

既存の.NET 10 SDK、wasm-tools、slangc、Chromiumが利用可能な環境で、リポジトリのルートから実行します。サンプルによるツールのインストールは行いません。

```sh
dotnet publish samples/Lumyte.Maps.Browser.Sample -c Release -p:PublishTrimmed=true
node samples/Lumyte.Maps.Browser.Sample/verify-browser.mjs
```

既定の動作確認は実際のタイル採用を要求し、mount済み`MapView`を`UiSurface.Record`と`UiDrawingScene2D`で記録して保持型描画の再利用を検査した後、所有アプリを停止して処理完了を待ちます。ホストGPUのPNGと地図の画素色も必須にする場合は`LUMYTE_MAPS_GPU_CAPTURE=1`を指定します。chromiumがPATHにない場合は`CHROME`に既存のBrowser実行ファイルを指定してください。これは一つの固定試験データでの動作確認であり、一般のMVT／Browser／AOT互換性、性能、無継ぎ目の証拠ではありません。

このサンプルは `UseBrowser(new UiBrowserApplicationOptions { OwnWheelDefaults = true })` でホイール既定動作の所有を明示します。既定は `false` で、一般の埋込みページのスクロールや Ctrl+wheel によるブラウザーズームを変更しません。opt-in 時だけ、表示中・接続中の Maps canvas が入力を受け付ける間、その canvas 上の wheel（Ctrl+wheel を含む）の既定動作を抑止し、地図カメラへ配送します。入力停止・非表示・切離し後や canvas 外の wheel は所有せず、window 全体の `preventDefault` は行いません。Platform を直接使う場合も `BrowserPlatform.CreateAsync(canvas, ownWheelDefaults: true)` で明示できます。

検証スクリプトは CDP の実マウス入力を DOM へ配送し、40×20 論理 pixel のパンと +0.25 zoom のポインター基準を camera の公開観測値で確認します。スクロール可能な埋込みfixtureで通常wheelとCtrl+wheelのカメラ変化、`window.scrollY`・canvas境界・viewport scaleの不変も要求します。これはブラウザーの実イベント経路の検証で、物理マウスや実画面での操作確認とは区別します。

現在の検証では、提示済み入力幾何の公開 hit-test を待ってから CDP 入力を配送します。パン・wheel anchor・取得 zoom 0→1 の追加4枚の採用／再記録は成功しましたが、終了時に `A valid external Instance reference no longer exists.` が報告される未解決条件があります。Browser 全体を合格とは扱いません。raw log と制約は [検証記録](../../tests/Maps/IMPLEMENTATION-EVIDENCE.md) を参照してください。
