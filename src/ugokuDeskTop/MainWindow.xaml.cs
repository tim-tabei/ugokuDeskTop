using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using ugokuDeskTop.Models;
using ugokuDeskTop.Native;
using ugokuDeskTop.Services;
using WpfMessageBox = System.Windows.MessageBox;

namespace ugokuDeskTop;

public partial class MainWindow : Window
{
    private IntPtr _workerW;
    private IntPtr _windowHandle;
    private bool _isEmbedded;
    private bool _webViewReady;
    private readonly WallpaperConfig _config;
    private AudioCaptureService? _audioService;
    private DispatcherTimer? _audioTimer;
    private float[]? _latestFrequencyData;
    private readonly object _audioLock = new();
    private FileSystemWatcher? _fileWatcher;

    public MainWindow()
    {
        InitializeComponent();
        _config = WallpaperConfig.Load();
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _windowHandle = new WindowInteropHelper(this).Handle;

        var env = await CoreWebView2Environment.CreateAsync(
            userDataFolder: Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ugokuDeskTop", "WebView2Data"));
        await webView.EnsureCoreWebView2Async(env);

        webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        webView.CoreWebView2.Settings.IsZoomControlEnabled = false;
        webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
        webView.CoreWebView2.Settings.AreDevToolsEnabled = false;

        _webViewReady = true;

        // 保存された壁紙を読み込み
        if (!string.IsNullOrWhiteSpace(_config.CustomUrl))
            LoadUrl(_config.CustomUrl);
        else
            LoadWallpaper(_config.CurrentWallpaper);

        // 壁紙として埋め込み
        if (!EmbedAsWallpaper())
        {
            WpfMessageBox.Show(
                "WorkerW ウィンドウが見つかりませんでした。\nデスクトップへの埋め込みに失敗しました。",
                "ugokuDeskTop", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        // 音声キャプチャ開始
        StartAudioCapture();
    }

    private void StartAudioCapture()
    {
        _audioService = new AudioCaptureService();
        _audioService.FrequencyDataReady += data =>
        {
            lock (_audioLock)
            {
                _latestFrequencyData = (float[])data.Clone();
            }
        };
        _audioService.Start();

        // UIスレッドのタイマーで定期的に WebView2 へ送信（約60fps）
        _audioTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _audioTimer.Tick += SendAudioDataToWebView;
        _audioTimer.Start();
    }

    private async void SendAudioDataToWebView(object? sender, EventArgs e)
    {
        if (!_webViewReady) return;

        float[]? data;
        lock (_audioLock)
        {
            data = _latestFrequencyData;
            _latestFrequencyData = null;
        }

        if (data == null) return;

        try
        {
            string json = JsonSerializer.Serialize(data);
            await webView.CoreWebView2.ExecuteScriptAsync(
                $"if(window.onFrequencyData) window.onFrequencyData({json})");
        }
        catch
        {
            // ページ遷移中など
        }
    }

    public void StopAudioCapture()
    {
        _audioTimer?.Stop();
        _audioService?.Dispose();
        _audioService = null;
    }

    public void LoadWallpaper(string wallpaperName)
    {
        if (!_webViewReady) return;

        string wallpaperPath = ResolveWallpaperPath(wallpaperName);

        if (File.Exists(wallpaperPath))
        {
            webView.CoreWebView2.Navigate(new Uri(wallpaperPath).AbsoluteUri);
            _config.CurrentWallpaper = wallpaperName;
            _config.CustomUrl = null;
            _config.Save();
            StartFileWatcher(Path.GetDirectoryName(wallpaperPath)!);
        }
    }

    /// <summary>
    /// 壁紙の index.html パスを解決する。
    /// 開発時はソースディレクトリ (src/ugokuDeskTop/Wallpapers/) を優先し、
    /// esbuild の出力が直接反映されるようにする。
    /// </summary>
    private static string ResolveWallpaperPath(string wallpaperName)
    {
        // 開発時: プロジェクトのソースディレクトリを探す
        // bin/Debug/net10.0-windows/ → 3階層上が ugokuDeskTop プロジェクトルート
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var projectDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", ".."));
        var srcPath = Path.Combine(projectDir, "Wallpapers", wallpaperName, "index.html");

        if (File.Exists(srcPath))
            return srcPath;

        // フォールバック: ビルド出力ディレクトリ（本番用）
        return Path.Combine(baseDir, "Wallpapers", wallpaperName, "index.html");
    }

    public void LoadUrl(string url)
    {
        if (!_webViewReady) return;
        webView.CoreWebView2.Navigate(url);
        _config.CustomUrl = url;
        _config.Save();
    }

    public bool EmbedAsWallpaper()
    {
        if (_isEmbedded) return true;

        _workerW = WallpaperHelper.FindWallpaperParent();
        if (_workerW == IntPtr.Zero) return false;

        WallpaperHelper.EmbedAsWallpaper(_windowHandle, _workerW);
        _isEmbedded = true;
        return true;
    }

    public void DetachWallpaper()
    {
        if (!_isEmbedded) return;
        WallpaperHelper.DetachWallpaper(_windowHandle);
        _isEmbedded = false;
    }

    // --- Hot Reload: 壁紙ディレクトリの .js/.html/.css を監視 ---
    private void StartFileWatcher(string directory)
    {
        StopFileWatcher();

        _fileWatcher = new FileSystemWatcher(directory)
        {
            NotifyFilter = NotifyFilters.LastWrite,
            IncludeSubdirectories = false,
            EnableRaisingEvents = true
        };
        _fileWatcher.Filters.Add("*.js");
        _fileWatcher.Filters.Add("*.html");
        _fileWatcher.Filters.Add("*.css");

        // esbuild は短時間に複数回書き込むので、デバウンスする
        DateTime lastReload = DateTime.MinValue;
        _fileWatcher.Changed += (_, args) =>
        {
            var now = DateTime.Now;
            if ((now - lastReload).TotalMilliseconds < 500) return;
            lastReload = now;

            Dispatcher.InvokeAsync(() =>
            {
                if (_webViewReady)
                    webView.CoreWebView2.Reload();
            });
        };
    }

    private void StopFileWatcher()
    {
        _fileWatcher?.Dispose();
        _fileWatcher = null;
    }
}
