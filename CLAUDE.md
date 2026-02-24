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
└── Wallpapers/                  # サンプル壁紙
    ├── package.json             # npm scripts (build / dev)
    ├── tsconfig.json            # TypeScript 設定
    ├── esbuild.config.mjs       # ビルド設定（壁紙の自動検出付き）
    ├── audio-visualizer/
    │   ├── index.html           # <script src="main.js"> で読み込み
    │   ├── src/main.ts          # ★ TypeScript ソース
    │   └── main.js              # ビルド成果物（git管理外）
    ├── particles/
    ├── gradient-wave/
    └── shader-demo/
```

## コアアーキテクチャ

壁紙の埋め込みは `WallpaperHelper.cs` が担当。Progman に `0x052C` メッセージを送信後、WorkerW を検出して `SetParent` で WPF ウィンドウを子にする。

### Windows 11 Build 26200+ の重要な注意

従来(Win10等)は WorkerW がトップレベルウィンドウとして存在したが、Windows 11 Build 26200+ では **Progman の子ウィンドウ** として存在する。`WallpaperHelper.ScanForTarget()` で両パターンに対応済み。

## 壁紙の TypeScript 開発

壁紙のスクリプトは TypeScript で書ける。esbuild でバンドル・コンパイルする。

### コマンド（`src/ugokuDeskTop/Wallpapers/` で実行）

```bash
npm run build          # 本番ビルド（minify あり）
npm run dev            # watch モード（ファイル変更で自動ビルド）
```

### Hot Reload 開発フロー

1. `dotnet run --project src/ugokuDeskTop/ugokuDeskTop.csproj` でアプリ起動
2. `Wallpapers/` ディレクトリで `npm run dev` を実行
3. `src/main.ts` を編集して保存 → esbuild が `main.js` を出力 → C# の `FileSystemWatcher` が検知 → WebView2 が自動リロード

### 壁紙の TypeScript 化

新しい壁紙を TypeScript 化するには `壁紙名/src/main.ts` を作るだけ。esbuild.config.mjs が自動検出する。

### 開発時のパス解決

`MainWindow.ResolveWallpaperPath()` は開発時にソースディレクトリ (`src/ugokuDeskTop/Wallpapers/`) を優先的に参照する。本番では `bin/` のビルド出力にフォールバック。

## WPF + WinForms 混在の注意

`Application`, `MessageBox` 等は WPF/WinForms 間で名前衝突する。完全修飾名または using alias を使うこと。
