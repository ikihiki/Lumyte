# HTTP + spatial MapSessionのtrim / NativeAOT consumer

127.0.0.1の一時portに明示登録した二つのXYZ endpointのみを持つsynthetic MVT道路fixture。
実際のM4 spatial MapSession（4引数constructor）からcamera/viewportとAdvanceでA→B→Aを要求し、
owned native HttpMvtSource、実ResourceManager、既存MvtResourceStage、revision検証、採用、
Canvas2DへのRecord、退役・manager maintenance・停止/Disposeまで通す。
有限fixedTiles overload、fake decoded stage、別parserは使わない。

- zoom 2、各tile中央の100×100 viewport。prefetch/fallbackを0にして要求を決定的にする。
- MaxResidentTiles / MaxInFlightTiles / MaxCompletedTiles / MaxPendingTilesを1に固定。
  各Advanceで実行・採用・退役の合計slot数とresident CPU byte予算を検査する。
- MapTileSet.GetKeyはHttpMvtRegistration.GetKeyへ委譲し、manifestの期待SHA256を採用時にも検査する。
  二つの受信bodyは異なるrevisionを持ち、実MVT decodeはA/B/Aの3回。
- 一つしか常駐できないため、各移動で旧Resources generationを実際にrelease/maintenanceする。
  一方HTTPの同期fresh cacheは二つのendpointだけを保持し、各1回、合計2回のdownloadで済む。
  sourceのrevision照会でも追加requestがないことを確認する。
- 各採用後に背景だけでなく実道路geometryをCanvas.Recordする（3回、draw records合計6以上）。
  これはrecord-only検証であり、UI mount、GPU描画、画素一致、性能測定の実証ではない。
- BeginStop後は公開revisionを増やさず、最後のgenerationもreleaseする。IsStopped、未処理/退役/常駐0、
  maintenance完了、追加Trimのrelease数0、実HTTP接続0を確認し、manager/sourceを解放する。
- 30秒はconsumerの機能的完了期限であり性能しきい値ではない。HTTP sourceの既存wire/expanded/deadline/
  entry/resident上限を利用し、source本体や304/retry/認証の契約を変更しない。

    dotnet run --project tests/Maps/Lumyte.Maps.Http.Smoke -c Release
    dotnet publish tests/Maps/Lumyte.Maps.Http.Smoke -c Release -r linux-x64 -p:PublishAot=true -o /tmp/maps-m5-spatial-aot
    /tmp/maps-m5-spatial-aot/Lumyte.Maps.Http.Smoke
    dotnet publish tests/Maps/Lumyte.Maps.Http.Smoke -c Release -r linux-x64 --self-contained true -p:PublishTrimmed=true -o /tmp/maps-m5-spatial-trim
    /tmp/maps-m5-spatial-trim/Lumyte.Maps.Http.Smoke

SDKとSlangの環境設定は既存tools/setup/environment.shを使用し、SlangCompilerPathは2026.19の
実行ファイルの絶対pathを指定する。Linux x64以外の実証ではない。
strict Browser HTTPは明示的に未対応のまま。公開外部request、外部データ、料金、disk cacheは使わない。
帰属はsynthetic fixture。実OSMデータを使うアプリは別途帰属・配信条件を確認する。
