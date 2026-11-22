# 固定長の仮想化一覧

10,000件のデータをコピーし、有限の表示範囲と前後2件だけを実体化するHeadlessアプリです。フォントとParley providerを明示し、UseDiagnosticsと通常の入力leaseによって画面外キーを選択します。件数は構造の検証であり性能の測定値ではありません。

```sh
dotnet run --project build -c Release -- --target Test --suite ui --feature virtualization
dotnet run --project build -c Release -- --target Test --suite ui --feature virtualization --aot
```

高さの自動測定、TreeView、DataGridは対象外です。

標準モードは画面外キーの表示と選択、eviction後の新しい接続ID、項目追加後のキーanchorと選択、通常wheelを確認します。schema 10のitem-keyとbring-itemはsource-generated JSONを実行時に往復し、同じ処理をNativeAOTでも実行します。

既存のHTTP Diagnosticsサービスへ接続するoperatorモードもあります。UseDiagnostics自体はsocketを開かないため、接続先transportは別途明示構成します。指定したsessionとtargetだけに制御leaseを取得します。

```sh
dotnet run --project samples/Lumyte.UI.Virtualization.Sample -- font.ttf --remote http://localhost:5000/ SESSION_ID TARGET_ID
```

このモードは指定先のlistというTestIdに対し、9999というモデルキーを表示・選択します。標準のHeadless実行だけをHTTP疎通の検証とは扱いません。
