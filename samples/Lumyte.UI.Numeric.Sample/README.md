# 数値・範囲・実editorのHeadless sample

通常のUiApplication、Gridのwith配置、明示的なフォントを借用するParley providerでSlider・RangeSlider・NumericBoxを構築する。

```sh
dotnet run --project samples/Lumyte.UI.Numeric.Sample -c Release -- tests/Graphics/Lumyte.Graphics.Text.Parley.Tests/Fonts/DejaVuSans.ttf
```

通常のapplication入力leaseから、scalar/rangeの閉じた操作、pointer dragとkeyboard、NumericBoxのFillを実行する。編集途中の符号、fr-FRの小数、値の受理とsnapshotを確認する。backendはsampleが明示的に所有するTextInputSessionBase実装であり、TextDocumentと既存のconnectionをそのまま使う。

GPU描画、物理入力、Windows TSF、BrowserのOS IMEは検証しない。詳細な判断は[UI-0031](../../docs/adr/ui/UI-0031-numeric-input-and-accepted-values.md)を参照する。
