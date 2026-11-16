# HTTP transportのtrim / NativeAOT consumer

synthetic MVT道路レイヤーを127.0.0.1の一時portだけで配信し、owned native HttpMvtSource、
BoundedMvtRegistration、実ResourceManagerとMvtResourceStageを通す。内容SHA256とlayerを検査する。
外部データ、公開サーバー、M4 session API、UI/GPU、性能測定は使わない。

    dotnet run --project tests/Maps/Lumyte.Maps.Http.Smoke -c Release
    dotnet publish tests/Maps/Lumyte.Maps.Http.Smoke -c Release -r linux-x64 -p:PublishAot=true -o /tmp/maps-m5-aot
    /tmp/maps-m5-aot/Lumyte.Maps.Http.Smoke
    dotnet publish tests/Maps/Lumyte.Maps.Http.Smoke -c Release -r linux-x64 --self-contained true -p:PublishTrimmed=true -o /tmp/maps-m5-trim
    /tmp/maps-m5-trim/Lumyte.Maps.Http.Smoke

SDKとSlangの環境設定は既存tools/setup/environment.shを使用し、SlangCompilerPathは2026.19の
実行ファイルの絶対pathを指定する。Linux x64以外やBrowser HTTPの実証ではない。
帰属はsynthetic fixture。実OSMデータを使うアプリは別途帰属・配信条件を確認する。
