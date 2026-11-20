# 有限領域を操作する SplitView

明示的なフォントと Parley を借用し、Headless の通常ウィンドウへ三領域と入れ子の SplitView を接続します。読み取り専用 DataGrid と TextBox は通常の子として配置し、境界だけが入力を所有します。

```sh
dotnet run --project samples/Lumyte.UI.SplitView.Sample -c Release -- tests/Graphics/Lumyte.Graphics.Text.Parley.Tests/Fonts/DejaVuSans.ttf
dotnet run --project build -c Release -- --target Test --suite ui --feature splitview
dotnet run --project build -c Release -- --target Test --suite ui --feature splitview --aot
```

実行には先頭の一つの引数でフォントファイルを渡します。数値の比率要求、矢印キー、ポインター取消しを通常入力 lease から実行し、編集内容と入れ子の領域の保持を確認します。宣言の寸法を書き換えて利用者操作を代用しません。

[契約](../../docs/adr/ui/UI-0041-split-view.md)と[基本 API](../../src/UI/Lumyte.UI/README.md)を参照してください。OS の IME、物理アクセシビリティ、Browser や GPU の実機動作を保証するサンプルではありません。
