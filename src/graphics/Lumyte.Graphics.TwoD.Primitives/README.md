# 2D の描画値

namespace は `Lumyte.Graphics.TwoD`。scene と準備済み glyph が同じ path、paint、画像参照を使うための CPU assembly で、Native／Portable には依存しない。

`PathBuilder` は Move、Line、Quadratic、Cubic、Close を保持した不変 `PathGeometry` を作る。`PolygonGeometry` は三角形列を所有する。GPU ごとの flatten、stroke 展開や packet 作成をこの assembly には置かない。

`Brush` は Solid、Linear／Radial／Sweep gradient、Image の意味を表す。gradient stop と dash は入力配列をコピーし、変更できない列として公開する。奇数個の dash は同じ列を繰り返して偶数周期にする。stroke は幅、join、cap、miter limit、dash offset を持つ。

`Color` は線形 straight RGBA。gradient の stop は premultiply してから RGBA を補間し、画像も encoding と alpha mode の変換後に線形 premultiplied の値を filter する。sweep の角度はラジアン、左上原点における正方向は時計回り。

`Draw2DImageSource` は `GpuImageUploadData` または logical texture から構築でき、どちらからも暗黙変換できる。file／URI／asset ID は受け取らない。GPU texture、sampler や descriptor は本体が管理する。layer mask、opacity、shadow と composite も物理 GPU object を含まない値で表す。

詳細は [ADR 0036](../../../docs/adr/0036-2d-render-passes.md) を参照。
