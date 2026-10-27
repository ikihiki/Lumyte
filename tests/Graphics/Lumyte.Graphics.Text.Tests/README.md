# 共通文字描画テスト

文字組みエンジンに依存しない`Lumyte.Graphics.Text`のsnapshotと距離場を検証します。
フォントライブラリ、nativeフォント描画、OSにインストールされたフォントは使用しません。

```sh
dotnet test tests/Graphics/Lumyte.Graphics.Text.Tests
```

`TextLayoutTests`はsnapshotの独立性、通常輪郭とカラーpaintの描画順、
Canvasの変換・clipの保持、不正な入力後も記録を続けられる契約を確認します。
`DistanceFieldGenerationTests`はMSDFの独立したedge channel、MTSDFのtrue distance alpha、
輪郭の穴・向き、鋭い角、二次・三次曲線、生成上限を確認します。

実フォントの文字組み、COLR v1、OpenType variation、fallbackの検証と
フォント資産の出所・ライセンスは[Parleyテスト](../Lumyte.Graphics.Text.Parley.Tests/README.md)で管理します。
