# ローカルの固定地図サンプル

既存のlauncherから実行します。

```sh
dotnet run --project samples/Lumyte.Samples -c Release -- --sample maps --backend headless --configuration Release
dotnet run --project samples/Lumyte.Samples -c Release -- --sample maps --backend desktop --configuration Release
```

Headlessは時間と処理を制限したCPU／描画記録のみのアプリ試験で、デコード済みタイルの実際の採用を待ちます。GPU画像を検証したものではありません。Desktopでは同じ公開`IUiApplicationContent`実装を画面へ表示します。ウィンドウのタイトルには、OSMデータを使わない手作り試験であることを示します。公開タイル配信先へは接続しません。一時的な小さなMVTを生成し、停止後にサンプルが所有する一時ディレクトリを削除します。

contentは`MapSession`を所有し、`ResourceManager`を借用します。Initializeで`MapView`を公開し、Advanceで上限内の完了採用を進め、BeginStopで処理を停止します。ホストのGPU使用完了後にRetireAsyncでsessionを返却し、ReleaseAsyncではサービスの所有権をアプリscopeに残します。CPUのみのcontentでも`IUiApplicationGraphicsContent`で復旧を明示的に確認し、旧GPU viewを保持しません。

試験形状には半透明の水域、穴付き建物、道路の縁取りと内側の別passを含みます。固定のローカル集合であり、パン・ズーム入力や空間ストリーミングではありません。Native／Browserの検証範囲と現在の制約は[検証手順](../../tests/Maps/README.md)を参照してください。
