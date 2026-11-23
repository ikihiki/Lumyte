# スクロール画面

Headless host上でScrollView、借用ScrollController、実体を持つScrollBarをGrid.Cellで配置する。
入れ子wheel残余、touch pan、BringIntoViewと実際のTextBoxを検証する。ScrollBarのthumbドラッグ、trackのページ移動、PageDownを通常の排他的入力leaseへ送り、controllerとcallbackを確認する。themeは不要。
文字layoutには明示的に借用するフォントファイルを渡す。

```sh
dotnet run --project samples/Lumyte.Samples -- --sample ui-scroll --backend headless -- tests/Graphics/Lumyte.Graphics.Text.Parley.Tests/Fonts/DejaVuSans.ttf
dotnet run --project build -- Test --suite ui --feature scroll
dotnet run --project build -- Test --suite ui --feature scroll --aot
```
