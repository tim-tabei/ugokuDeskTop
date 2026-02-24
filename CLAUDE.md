# ugokuDeskTop

Windows 11 用ライブ壁紙アプリケーション。WebView2 でHTML/CSS/JS/WebGLコンテンツをデスクトップ背景に表示する。

## 技術スタック

- C# WPF + WebView2 (.NET 10.0)
- Win32 API P/Invoke (SetParent, FindWindow, SendMessageTimeout)
- WinForms 混在 (NotifyIcon によるシステムトレイ)

## ビルド・実行

```bash
dotnet build src/ugokuDeskTop/ugokuDeskTop.csproj
dotnet run --project src/ugokuDeskTop/ugokuDeskTop.csproj
```

## プロジェクト構造

```
src/ugokuDeskTop/
├── App.xaml.cs                  # エントリポイント
├── MainWindow.xaml.cs           # WebView2ホスト、壁紙埋め込み
├── Native/
│   ├── Win32Api.cs              # P/Invoke 宣言
│   └── WallpaperHelper.cs      # WorkerW 検出・埋め込みロジック
├── TrayIcon/
│   └── TrayIconManager.cs      # システムトレイ + コンテキストメニュー
├── Models/
│   └── WallpaperConfig.cs      # 設定 JSON 永続化
└── Wallpapers/                  # サンプル壁紙 (HTML/CSS/JS)
    ├── particles/
    ├── gradient-wave/
    └── shader-demo/
```

## コアアーキテクチャ

壁紙の埋め込みは `WallpaperHelper.cs` が担当。Progman に `0x052C` メッセージを送信後、WorkerW を検出して `SetParent` で WPF ウィンドウを子にする。

### Windows 11 Build 26200+ の重要な注意

従来(Win10等)は WorkerW がトップレベルウィンドウとして存在したが、Windows 11 Build 26200+ では **Progman の子ウィンドウ** として存在する。`WallpaperHelper.ScanForTarget()` で両パターンに対応済み。

## WPF + WinForms 混在の注意

`Application`, `MessageBox` 等は WPF/WinForms 間で名前衝突する。完全修飾名または using alias を使うこと。
