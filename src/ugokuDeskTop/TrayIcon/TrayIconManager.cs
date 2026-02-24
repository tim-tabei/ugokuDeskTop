using System.Drawing;
using System.Windows.Forms;

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

        // 壁紙選択サブメニュー
        var wallpaperMenu = new ToolStripMenuItem("壁紙を選択");
        wallpaperMenu.DropDownItems.Add("Particles", null,
            (s, e) => _mainWindow.Dispatcher.Invoke(() => _mainWindow.LoadWallpaper("particles")));
        wallpaperMenu.DropDownItems.Add("Gradient Wave", null,
            (s, e) => _mainWindow.Dispatcher.Invoke(() => _mainWindow.LoadWallpaper("gradient-wave")));
        wallpaperMenu.DropDownItems.Add("Shader Demo", null,
            (s, e) => _mainWindow.Dispatcher.Invoke(() => _mainWindow.LoadWallpaper("shader-demo")));
        wallpaperMenu.DropDownItems.Add(new ToolStripSeparator());
        wallpaperMenu.DropDownItems.Add("Audio Visualizer", null,
            (s, e) => _mainWindow.Dispatcher.Invoke(() => _mainWindow.LoadWallpaper("audio-visualizer")));
        menu.Items.Add(wallpaperMenu);

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
