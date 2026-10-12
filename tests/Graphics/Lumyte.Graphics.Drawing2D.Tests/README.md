# Drawing2Dの検証

CPU側の不変snapshot、入力検証、Canvasの状態とlayer境界、DI登録、エフェクトシステムへの接続を検証する。共通エフェクト自体は `Lumyte.Graphics.ImageEffects.Tests` で独立して検証する。

```bash
source tools/setup/activate.sh
dotnet test tests/Graphics/Lumyte.Graphics.Drawing2D.Tests -c Release
```

GPU描画は `Lumyte.Graphics.Tests/Drawing2DTests.cs` でVulkanとwgpuのreadbackを比較する。ソフトウェアVulkan driverでも実行できる。

```bash
LUMYTE_GRAPHICS_GPU_TESTS=1 VK_DRIVER_FILES="$LUMYTE_LAVAPIPE_ICD" \
  dotnet test tests/Graphics/Lumyte.Graphics.Tests -c Release --filter Drawing2DTests
```
