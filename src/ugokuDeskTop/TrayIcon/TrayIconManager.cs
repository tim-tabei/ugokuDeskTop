using System.Drawing;
using System.Windows.Forms;
using ugokuDeskTop.Services;

namespace ugokuDeskTop.TrayIcon;

internal class TrayIconManager : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly MainWindow _mainWindow;
    private readonly Action _shutdownAction;

    public TrayIconManager(MainWindow mainWindow, Action shutdownAction)
    {
        _mainWindow = mainWindow;
        _shutdownAction = shutdownAction;

        _notifyIcon = new NotifyIcon
        {
            Text = "ugokuDeskTop",
            Visible = true,
            Icon = SystemIcons.Application
        };

        _notifyIcon.ContextMenuStrip = CreateContextMenu();
    }

    private ContextMenuStrip CreateContextMenu()
    {
        var menu = new ContextMenuStrip();

        // 壁紙選択サブメニュー（Wallpapers ディレクトリから自動検出）
        var wallpaperMenu = new ToolStripMenuItem("壁紙を選択");
        var wallpapers = WallpaperDiscoveryService.Discover();
        bool separatorAdded = false;

        foreach (var wp in wallpapers)
        {
            if (wp.IsSpecial && !separatorAdded)
            {
                wallpaperMenu.DropDownItems.Add(new ToolStripSeparator());
                separatorAdded = true;
            }

            wallpaperMenu.DropDownItems.Add(wp.DisplayName, null,
                (s, e) => _mainWindow.Dispatcher.Invoke(
                    () => _mainWindow.LoadWallpaper(wp.DirectoryName)));
        }

        menu.Items.Add(wallpaperMenu);

        // Equalizer APO 連携トグル
        var apoMenuItem = new ToolStripMenuItem("Equalizer APO 連携")
        {
            CheckOnClick = true,
            Checked = _mainWindow.IsApoEnabled,
            Enabled = _mainWindow.IsApoAvailable
        };
        if (!_mainWindow.IsApoAvailable)
            apoMenuItem.ToolTipText = "Equalizer APO がインストールされていません";
        apoMenuItem.CheckedChanged += (s, e) =>
        {
            _mainWindow.Dispatcher.Invoke(() => _mainWindow.SetApoEnabled(apoMenuItem.Checked));
        };
        menu.Items.Add(apoMenuItem);

        // フィルターモード選択サブメニュー
        var modeMenu = new ToolStripMenuItem("フィルターモード")
        {
            Enabled = _mainWindow.IsApoAvailable
        };

        var modeLabels = new Dictionary<string, string>
        {
            ["LP/HP"] = "ローパス / ハイパス",
            ["BP"] = "バンドパス（帯域通過）",
            ["NO"] = "ノッチ（帯域除去）",
            ["PK"] = "ピーキングEQ（ブースト/カット）",
            ["LSC"] = "ローシェルフ（低域増減）",
            ["HSC"] = "ハイシェルフ（高域増減）",
        };

        var currentMode = _mainWindow.ApoFilterMode;
        foreach (var mode in EqualizerApoService.AvailableModes)
        {
            var label = modeLabels.TryGetValue(mode, out var l) ? l : mode;
            var item = new ToolStripMenuItem(label)
            {
                Tag = mode,
                Checked = mode == currentMode
            };
            item.Click += (s, e) =>
            {
                var clicked = (ToolStripMenuItem)s!;
                var selectedMode = (string)clicked.Tag!;
                _mainWindow.Dispatcher.Invoke(() => _mainWindow.SetApoFilterMode(selectedMode));
                // チェック状態を更新
                foreach (ToolStripItem child in modeMenu.DropDownItems)
                {
                    if (child is ToolStripMenuItem mi)
                        mi.Checked = (string)mi.Tag! == selectedMode;
                }
            };
            modeMenu.DropDownItems.Add(item);
        }
        menu.Items.Add(modeMenu);

        menu.Items.Add(new ToolStripSeparator());

        // URL から読み込み
        menu.Items.Add("URLから読み込み...", null, (s, e) =>
        {
            using var dialog = new UrlInputDialog();
            if (dialog.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.Url))
            {
                _mainWindow.Dispatcher.Invoke(() => _mainWindow.LoadUrl(dialog.Url));
            }
        });

        menu.Items.Add(new ToolStripSeparator());

        // 終了
        menu.Items.Add("終了", null, (s, e) => _shutdownAction());

        return menu;
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}

/// <summary>
/// URL入力用の簡易ダイアログ
/// </summary>
internal class UrlInputDialog : Form
{
    private readonly TextBox _textBox;
    public string Url => _textBox.Text;

    public UrlInputDialog()
    {
        Text = "ugokuDeskTop - URL入力";
        Width = 500;
        Height = 150;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;

        var label = new Label
        {
            Text = "壁紙のURLを入力してください:",
            Left = 10,
            Top = 15,
            Width = 460
        };

        _textBox = new TextBox
        {
            Left = 10,
            Top = 40,
            Width = 460
        };

        var okButton = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Left = 310,
            Top = 70,
            Width = 75
        };

        var cancelButton = new Button
        {
            Text = "キャンセル",
            DialogResult = DialogResult.Cancel,
            Left = 395,
            Top = 70,
            Width = 75
        };

        AcceptButton = okButton;
        CancelButton = cancelButton;

        Controls.AddRange([label, _textBox, okButton, cancelButton]);
    }
}
