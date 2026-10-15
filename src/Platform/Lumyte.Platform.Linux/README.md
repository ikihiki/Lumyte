# Lumyte.Platform.Linux

64-bit Linux の X11 Window と論理キーボード・マウス取得を提供する初期バックエンドです。X11 display が必要です。Wayland 専用バックエンド、Clipboard selection、XIM 文字入力は未実装です。

LinuxPlatform が返す LinuxWindow は IWindow を直接実装し、`XDisplay` と `XWindow` を具象プロパティとして公開します。表示・非表示・リサイズ、所有者、override-redirect Popup、Native 保持と終了を提供します。Popup は PreserveFocus を指定します。

DPI は 1 として扱い、IsResizable による Window manager のリサイズ制限は未実装です。アンカー配置、明示活性化、前面化、通常以外の display mode は Unsupported を通知します。Window manager が要求を反映しなければ二秒後に Denied とします。Xlib のプロトコルエラーは scoped handler と XSync で回収しますが、X server の接続喪失からの回復は保証しません。

キー対応の既定は evdev ベースの XKB keycode です。異なる keymap は LinuxPlatform の keyMapping 引数で指定します。Raw Input、ゲームパッド、タッチ、ペンは未対応です。

作成スレッドで PumpEvents を呼び、任意の InputSystem / WindowSystem をその後に更新します。Input Source や Window は OS キューを再取得しません。Window と Source を解放してから Platform を解放します。
