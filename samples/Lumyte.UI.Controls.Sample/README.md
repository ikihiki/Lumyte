# Controls settings sample

Public Headless application with all five selection controls, explicit-font Media.Text labels and Signal models.

```sh
dotnet run --project samples/Lumyte.UI.Controls.Sample -- tests/Graphics/Lumyte.Graphics.Text.Parley.Tests/Fonts/DejaVuSans.ttf
```

The sample checks normal automation, idempotent desired states, model writeback, drawing and clean shutdown. Windows, Browser OS input and real GPU execution are not implied by this headless check.

Official gates are `Test --suite ui --feature controls` (after compiling the solution) and `Test --suite ui --feature controls --aot` (strict ILLink/Ilc warnings-as-errors, publish and execute). The launcher exposes `--sample ui-controls --backend headless -- <explicit-font.ttf>`.

## ノブのテンプレート

サンプルは Slider の Thumb Slot にラムダを指定し、受理値が50以上なら「◆」、未満なら「●」を実フォントで描画します。公開自動操作で値を75へ変更し、表示が追従することと、装飾が自動操作対象にならないことを確認します。

```csharp
Slider.Thumb()[
    state => Text(value: state.Value >= 50 ? "◆" : "●", layoutProvider: provider)
]
```
