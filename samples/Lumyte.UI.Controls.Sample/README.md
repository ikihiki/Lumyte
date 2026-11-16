# Controls settings sample

Public Headless application with all five selection controls, explicit-font Media.Text labels and Signal models.

```sh
dotnet run --project samples/Lumyte.UI.Controls.Sample -- tests/Graphics/Lumyte.Graphics.Text.Parley.Tests/Fonts/DejaVuSans.ttf
```

The sample checks normal automation, idempotent desired states, model writeback, drawing and clean shutdown. Windows, Browser OS input and real GPU execution are not implied by this headless check.

Official gates are `TestUiControls` (after compiling the solution) and `TestUiControlsAot` (strict ILLink/Ilc warnings-as-errors, publish and execute). The launcher exposes `--sample ui-controls --backend headless -- <explicit-font.ttf>`.
