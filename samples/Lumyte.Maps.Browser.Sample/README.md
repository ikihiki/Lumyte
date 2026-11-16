# 容量制限付きMVTのBrowser採用サンプル

Nativeサンプルから手作り試験データをリンクして使用します。OSMのダウンロード、ローカルファイルからのタイル取得、HTTPタイルsource、M3のストリーミングは使いません。Browserホストはアプリ資産をlocalhost経由で配信しますが、地図タイルの配信ではありません。

contentは公開`IUiApplicationContent`のhookで`MapSession`を所有し、Resourcesがデコード済みCPU世代を所有します。専用の`InMemoryMvtRegistration`は入力・予約制限を検査してから各タイルをコピーし、SHA256 revisionを固定します。sourceは非公開の不変領域を読取り専用snapshot間で共有します。登録が先行読込みしたタイル領域はアプリ所有であり、sessionごとのdecoder／常駐予算とは別に必要です。profileはprocess全体の上限ではありません。

既存の.NET 10 SDK、wasm-tools、slangc、Chromiumが利用可能な環境で、リポジトリのルートから実行します。サンプルによるツールのインストールは行いません。

```sh
dotnet publish samples/Lumyte.Maps.Browser.Sample -c Release -p:PublishTrimmed=true
node samples/Lumyte.Maps.Browser.Sample/verify-browser.mjs
```

既定の動作確認は実際のタイル採用を要求し、mount済み`MapView`を`UiSurface.Record`と`UiDrawingScene2D`で記録して保持型描画の再利用を検査した後、所有アプリを停止して処理完了を待ちます。ホストGPUのPNGと地図の画素色も必須にする場合は`LUMYTE_MAPS_GPU_CAPTURE=1`を指定します。chromiumがPATHにない場合は`CHROME`に既存のBrowser実行ファイルを指定してください。これは一つの固定試験データでの動作確認であり、一般のMVT／Browser／AOT互換性、性能、無継ぎ目の証拠ではありません。
