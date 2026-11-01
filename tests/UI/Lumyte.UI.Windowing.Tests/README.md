# UI Windowingの検証

```sh
dotnet test tests/UI/Lumyte.UI.Windowing.Tests -c Release
```

独立Hosted manager、Fallback wrapper、実WindowSystemを使うNative fixture、focus bookmark、Popup/submenu、outside系列、keyboard navigation、遅延GPU退役、終了参加者、自動操作のroutingを検証します。

Native fixtureはOS/backendの入力取得を代用するため、Windows/Linuxの実Window操作の成功とは扱いません。描画のfixtureと、[GPUサンプル](../../../samples/Lumyte.UI.Popup.Sample/README.md)の実PNG検証、[実Browser](../../../samples/Lumyte.UI.Browser.Sample/README.md)の入力・CPU合成検証を区別します。
