# 保持タブと折り畳み

明示したfont、Headless UiApplication、借用text backendを使う実行例。
TabView、TabItem、Expanderを生成named slotで組み立て、通常の排他的入力leaseで操作する。
実際のTextBoxとParley文書がタブ切替・折り畳み後も同一であり、preeditが終了することを確認する。
借用backendはOS IMEではない。Windows、Browser、物理表示の確認を代替しない。

```sh
dotnet run --project samples/Lumyte.Samples -c Release -- --sample ui-tabs --backend headless --configuration Release -- tests/Graphics/Lumyte.Graphics.Text.Parley.Tests/Fonts/DejaVuSans.ttf
dotnet run --project build -c Release -- --target TestUiTabs
dotnet run --project build -c Release -- --target TestUiTabsAot
```

HeaderとContentは各0〜1個。`TabItem(key: "a")[TabItem.Header()[header], TabItem.Content()[content]]`を使う。
配置は`with`引数で指定し、GridCellやExpandedの所有wrapperを追加しない。
