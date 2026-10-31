# Lumyte.UI.TextEditing.Parley

借用`ParleyTextEngine2D`から`ParleyUiTextLayoutProvider`を作ります。ホストがfontを登録し、OS font探索はしません。ParleyのCursor/SelectionからUTF-8境界・visual順・bidi選択矩形をコピーし、managed側でUTF-16/graphemeへ対応付けます。glyph幅の割り算でcaret位置を推定しません。

新しいediting metadataにはnative ABI 2が必要です。managedとnative binaryを同じpackage版で配布してください。旧ABIはengine構築時に拒否します。
