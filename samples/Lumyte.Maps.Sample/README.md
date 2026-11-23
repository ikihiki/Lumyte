# ローカルの空間ストリーミングサンプル

既存のlauncherから実行します。

```sh
dotnet run --project samples/Lumyte.Samples -c Release -- --sample maps --backend headless --configuration Release
dotnet run --project samples/Lumyte.Samples -c Release -- --sample maps --backend desktop --configuration Release
```

Headlessは時間と処理を制限したCPU／描画記録のみのアプリ試験で、デコード済みタイルの実際の採用を待ちます。GPU画像を検証したものではありません。Desktopでは同じ公開`IUiApplicationContent`実装を画面へ表示します。ウィンドウのタイトルには、OSMデータを使わない手作り試験であることを示します。公開タイル配信先へは接続しません。一時的な小さなMVTを生成し、停止後にサンプルが所有する一時ディレクトリを削除します。

contentは`MapSession`を所有し、`ResourceManager`を借用します。Initializeで`MapView`を公開し、Advanceで上限内の完了採用を進め、BeginStopで処理を停止します。ホストのGPU使用完了後にRetireAsyncでsessionを返却し、ReleaseAsyncではサービスの所有権をアプリscopeに残します。CPUのみのcontentでも`IUiApplicationGraphicsContent`で復旧を明示的に確認し、旧GPU viewを保持しません。

試験形状には半透明の水域、穴付き建物、道路の縁取りと内側の別passを含みます。M4 の動的 session を使い、ドラッグによるパンとポインター基準のホイールズームに対応します。zoom 0～2 の全21 IDへ同じ小さな手作り MVT のローカルファイルを割り当てます。これは実都市の異なる解像度ではありません。常駐8 slot、先読み2候補で、不要 tile の LRU と Manager の未使用 generation 回収を進めます。HTTP は使いません。Native／Browserの検証範囲と現在の制約は[検証手順](../../tests/Maps/README.md)を参照してください。

Browser サンプルの検証スクリプトは CDP の実マウス入力を DOM へ配送し、40×20 論理 pixel のパンと +0.25 zoom のポインター基準を camera の公開観測値で確認します。これはブラウザーの実イベント経路の検証で、物理マウスや実画面での操作確認とは区別します。

Headless は 512×256 論理 pixel で zoom 2 の16中心を巡回して再訪する固定経路を実行します。各段の可視採用・配送／maintenance 終端を待ち、Manager の実解放を1回以上要求してから停止します。tile の保持 counter が減るだけの成功扱いにはしません。ローカル registration の21 ID／入力ファイルと host 記録の予算は session の CPU tile 予算とは別です。Browser サンプルは M3 の有限集合の互換確認を継続し、現在の Browser cleanup 例外を成功へ読み替えません。
