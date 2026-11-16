# 地図機能の検証

すべての試験データは手作りで、ネットワークや再配布されたOSMデータを使いません。

- `Lumyte.Maps.Tests`：座標正規化、レイヤーごとのextent、輪郭・穴・複数外周、不正／途中終了のprotobufとcommand、独立した容量上限、ローカルsource／stageの同一性、観測revision、Managerによる共有を検証します。
- `Lumyte.Maps.Session.Tests`：容量制限付き受付、queue満杯時の再試行、0サイズviewport、停止時の実処理完了待ち、cache hitの計上、内容世代のPinを検証します。明示的に有効化した場合は実Vulkanでの解析的な期待画素と、直接描画／MapViewの保持型描画の出力一致も確認します。既定ではGPU試験を明示的にskipします。
- `Lumyte.Maps.UI.Tests`：記録のみの保持型描画再利用、無効化、交換、mount／unmountを検証します。

リポジトリ共通のSDK・環境とSlangのPATHを使用し、各projectを`dotnet test -c Release`で実行します。実GPUの読戻しには既存CIの`LUMYTE_GRAPHICS_GPU_TESTS=1`（`LUMYTE_MAPS_GPU_TESTS=1`も別名として利用可能）と、実際のdeviceを確認した`VK_DRIVER_FILES`のmanifestを指定してください。PNG成果物は`/tmp/lumyte-map-gpu-rgba.bin`の読戻し結果を変換したもので、デスクトップのスクリーンショットではありません。

警告をエラーとして扱うNativeAOTとtrim付きpublishには、既存CIと同じ設定を使います。

```sh
dotnet publish samples/Lumyte.Maps.Sample -c Release -p:PublishAot=true -p:ILLinkTreatWarningsAsErrors=true -p:IlcTreatWarningsAsErrors=true -o /tmp/lumyte-maps-aot
dotnet publish samples/Lumyte.Maps.Sample -c Release -r linux-x64 --self-contained true -p:PublishTrimmed=true -p:ILLinkTreatWarningsAsErrors=true -o /tmp/lumyte-maps-trim
```

publishした各`Lumyte.Maps.Sample`を`headless`引数で実行します。この動作確認は実際のタイル採用を待ってから正常終了します。launcherへの登録は`--sample maps --backend headless --configuration Release`で確認します。

Browser実行は、容量制限付きの不変メモリ入力を使う`Lumyte.Maps.Browser.Sample`で別途検証します。trim付きBrowserの動作確認は、採用、mount済みUIの記録、保持型描画の再利用、後始末を検証します。BrowserのGPU画素を検証したものではなく、追加の画素キャプチャはdevice復旧のtimeoutで失敗した状態です。HTTP と空間優先 queue／LRU は対象外です。M3 は明示した有限候補の可視選択、world wrap、連続 zoom と整数取得 zoom、primary pointer capture とホイール入力を検証します。採用済み CPU tile は停止まで保持し、無制限の空間ストリーミングではありません。最終の手作り実GPU試験は、親clip、overlay、extent 1024／8192にまたがるalpha 0.5の所有境界、整数／半pixel位置、DPI 1／1.5／2を含みます。汎用試験では平行移動・反転も検証し、回転・せん断を明示的に拒否します。この有限の試験から、実都市データや一般的な無継ぎ目を保証しません。

## 既存CIでの検出

Mapsの試験projectは`Lumyte.slnx`へ登録しています。`build/Build.cs`のTestは正本solution（またはホスト用に派生した`BuildSolution`）に対して`-m:1`付きの`dotnet test`を実行します。Build.Ciのホスト向け選別でも、これらの移植可能なprojectは残ります。既存LinuxのNative GPU CIは`LUMYTE_GRAPHICS_GPU_TESTS=1`を設定済みで、`MapGpuFact`もその標準設定に従うため、新たなCI環境変数は不要です。

追加の実GPU試験は論理DPI 1／2、隣接するextent 1024／8192、半透明でタイル余白を持つポリゴン、道路の縁取りと内側、UI overlay、実際のUI親領域からはみ出してclipされる子を描画します。直接描画と保持型描画の全読戻し画素を比較し、解析的な期待色も独立に確認します。試験成果物はGit外へ保存します。結果は[実装の検証記録](IMPLEMENTATION-EVIDENCE.md)を参照してください。

M3 の固定移動試験は public `UiSurface.Dispatch`／layout／直接と保持型記録を使います。capture loss・focus loss、overlay Button 優先、viewport 変更、正規化 ID の反復world共有と未知領域の背景を検証します。実 GPU の追加試験は 0.5 pixel パン→pointer 基準 +0.4 zoom→(-20,3) pixel パンの決定的な経路で、alpha 0.5 の解析的な期待値と直接／保持型の全画素一致を要求します。
