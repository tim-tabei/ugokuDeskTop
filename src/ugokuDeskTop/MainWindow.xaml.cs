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

        string wallpaperPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Wallpapers", wallpaperName, "index.html");

        if (File.Exists(wallpaperPath))
        {
            webView.CoreWebView2.Navigate(new Uri(wallpaperPath).AbsoluteUri);
            _config.CurrentWallpaper = wallpaperName;
            _config.CustomUrl = null;
            _config.Save();
        }
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
}
