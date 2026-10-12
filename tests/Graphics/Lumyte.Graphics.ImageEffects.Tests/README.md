# ImageEffectsの検証

CPU側の共通定義の不変graph、数値検証、必要入力範囲、共有nodeの展開、失敗時の解放、拡張メソッドによるパス追加を検証する。すべての効果について、エフェクト自身の固定実装への配送・子snapshot・領域計算・0入力生成・複数入力・予算・出力検証・複数pass・失敗時解放を検証する。

```bash
source tools/setup/activate.sh
dotnet test tests/Graphics/Lumyte.Graphics.ImageEffects.Tests -c Release
```

Vulkanとwgpuの画像演算をreadbackで検証する。

```bash
LUMYTE_GRAPHICS_GPU_TESTS=1 VK_DRIVER_FILES="$LUMYTE_LAVAPIPE_ICD" \
  dotnet test tests/Graphics/Lumyte.Graphics.Tests -c Release --filter ImageEffectsTests
```

設計とAPIは [GRAPHICS-0017](../../../docs/adr/graphics/GRAPHICS-0017-image-effects.md) を参照する。
