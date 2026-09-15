# CPU 2D scene

この assembly は図形、画像、準備済み文字、clip と layer の意味を不変 scene として保持する。GPU device、renderer、shader、atlas は所有しない。描画は `Lumyte.Graphics.Passes` の `Add2DPass` へ scene を渡し、登録された Native／Portable の本体が行う。

```csharp
using System.Numerics;
using Lumyte.Graphics.TwoD;

using var draw = new Draw2DSceneBuilder();
draw.FillRoundedRectangle(new(8, 8, 160, 48), 6,
    Brush.Solid(Color.FromSrgb(0.2f, 0.3f, 0.6f)));
using (draw.BeginClip(new Rect(12, 12, 152, 40)))
{
    draw.FillEllipse(new(20, 16, 32, 32), Brush.Solid(Color.White));
}

var store = new Draw2DSceneStore();
var node = store.CreateNode(draw.Finish());
Draw2DScene first = store.Snapshot();
store.SetTransform(node, Matrix3x2.CreateTranslation(12, 0));
Draw2DScene second = store.Snapshot();
```

`first` は編集前の内容を保つ。store は不変 AVL 部分木を共有し、編集した node の祖先だけを作り直す。変更のない `Snapshot()` は同じ object を返す。content は `DrawScene` でも再利用できる。子 scene の `DeviceScale` は 1 とし、最も外側の scene だけで出力倍率を指定する。

`BeginState`／`BeginClip`／`BeginLayer` の scope は作成と逆の順で閉じる。clip は追加した時点の transform を保持する。layer の Content はローカル座標で記録し、親の transform と clip は layer command が保持する。親 clip は blur や shadow の準備後の合成に適用する。

`Commands` は GPU packet ではなく意味上の描画内容である。`OwnUploads`／`OwnTextures` と `ChildScenes` は input contract が構造共有した所有情報を保持するための公開契約であり、GPU 使用後の回収は各 provider に任せる。

描画値は [TwoD.Primitives](../Lumyte.Graphics.TwoD.Primitives/README.md)、配置済み文字は [Text](../Lumyte.Graphics.Text/README.md)、全体の API と現在の最適化範囲は [ADR 0036](../../../docs/adr/0036-2d-render-passes.md) を参照。
