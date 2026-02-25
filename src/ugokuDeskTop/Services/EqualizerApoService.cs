using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace ugokuDeskTop.Services;

/// <summary>
/// Equalizer APO の設定ファイルを動的に書き換え、
/// マウス位置に応じたリアルタイム音声フィルターを適用する。
/// </summary>
internal class EqualizerApoService : IDisposable
{
    private const string APO_REGISTRY_KEY = @"SOFTWARE\EqualizerAPO";
    private const string APO_DEFAULT_PATH = @"C:\Program Files\EqualizerAPO";
    private const string MAIN_CONFIG = "config.txt";
    private const string UGOKU_CONFIG = "ugokuDeskTop.txt";
    private const string INCLUDE_DIRECTIVE = "Include: ugokuDeskTop.txt";

    // スロットリング
    private DateTime _lastWriteTime = DateTime.MinValue;
    private const int ThrottleMs = 50;

    // 前回値キャッシュ（不要な書き込み防止）
    private double _lastFreqHz = -1;
    private double _lastQ = -1;
    private double _lastGainDb = double.MinValue;
    private string _lastFilterType = "";

    // 状態
    private string? _apoConfigDir;
    private bool _isAvailable;
    private bool _isActive;
    private bool _enabled;
    private string _filterMode = "LP/HP";

    // フィルターパラメータ範囲
    private const double MinFreqHz = 80;
    private const double MaxFreqHz = 16000;
    private const double MinQ = 0.5;
    private const double MaxQ = 5.0;
    private const double MinGainDb = -15;
    private const double MaxGainDb = 15;

    // 中央のデッドゾーン幅（左右各5%）— LP/HP モードのみ
    private const double DeadZone = 0.05;

    // 有効なフィルターモード一覧
    public static readonly string[] AvailableModes = ["LP/HP", "BP", "NO", "PK", "LSC", "HSC"];

    public bool IsAvailable => _isAvailable;
    public bool IsActive => _isActive;
    public string CurrentFilterType { get; private set; } = "OFF";
    public double CurrentFrequencyHz { get; private set; }
    public double CurrentQ { get; private set; }
    public double CurrentGainDb { get; private set; }

    public string FilterMode
    {
        get => _filterMode;
        set
        {
            if (_filterMode == value) return;
            _filterMode = value;
            // モード変更時にキャッシュをリセットして即時反映
            _lastFreqHz = -1;
            _lastQ = -1;
            _lastFilterType = "";
            _lastGainDb = double.MinValue;
        }
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            if (!_enabled && _isActive)
                DisableFilter();
        }
    }

    public bool Initialize()
    {
        if (!DetectApo())
            return false;

        if (!EnsureIncludeDirective())
        {
            _isAvailable = false;
            return false;
        }

        // 起動時はフィルター無効状態で開始
        DisableFilter();
        return true;
    }

    public void UpdateFilter(double mouseX, double mouseY)
    {
        if (!_isAvailable || !_enabled) return;

        // スロットリング
        var now = DateTime.UtcNow;
        if ((now - _lastWriteTime).TotalMilliseconds < ThrottleMs) return;

        var result = CalculateFilter(mouseX, mouseY, _filterMode);

        // 変化検出
        double freqChange = Math.Abs(result.freqHz - _lastFreqHz) / Math.Max(_lastFreqHz, 1);
        double qChange = Math.Abs(result.q - _lastQ);
        double gainChange = Math.Abs(result.gainDb - _lastGainDb);
        if (result.filterType == _lastFilterType && freqChange < 0.05 && qChange < 0.1 && gainChange < 0.5)
            return;

        string content;
        if (result.filterType == "OFF")
        {
            content = "# ugokuDeskTop auto-generated filter\n# Filter disabled (center)";
        }
        else if (result.filterType is "PK")
        {
            // ピーキングEQ: ゲインとQ両方
            content = $"# ugokuDeskTop auto-generated filter\nFilter: ON {result.filterType} Fc {result.freqHz:F0} Hz Gain {result.gainDb:F1} dB Q {result.q:F2}";
        }
        else if (result.filterType is "LSC" or "HSC")
        {
            // シェルフ: ゲインのみ（Qは省略 → デフォルト）
            content = $"# ugokuDeskTop auto-generated filter\nFilter: ON {result.filterType} Fc {result.freqHz:F0} Hz Gain {result.gainDb:F1} dB";
        }
        else
        {
            // LP, HP, BP, NO: 周波数とQ
            content = $"# ugokuDeskTop auto-generated filter\nFilter: ON {result.filterType} Fc {result.freqHz:F0} Hz Q {result.q:F2}";
        }

        if (WriteFilterConfig(content))
        {
            _lastWriteTime = now;
            _lastFreqHz = result.freqHz;
            _lastQ = result.q;
            _lastGainDb = result.gainDb;
            _lastFilterType = result.filterType;
            _isActive = result.filterType != "OFF";
            CurrentFilterType = result.filterType;
            CurrentFrequencyHz = result.freqHz;
            CurrentQ = result.q;
            CurrentGainDb = result.gainDb;
        }
    }

    public void DisableFilter()
    {
        if (WriteFilterConfig("# ugokuDeskTop auto-generated filter\n# Filter disabled"))
        {
            _isActive = false;
            _lastFreqHz = -1;
            _lastQ = -1;
            _lastGainDb = double.MinValue;
            _lastFilterType = "";
            CurrentFilterType = "OFF";
            CurrentGainDb = 0;
        }
    }

    public void Dispose()
    {
        if (_isActive)
            DisableFilter();
    }

    private bool DetectApo()
    {
        // 1. レジストリからインストールパスを取得
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(APO_REGISTRY_KEY);
            if (key != null)
            {
                var installPath = key.GetValue("InstallPath") as string;
                if (!string.IsNullOrEmpty(installPath))
                {
                    var configDir = Path.Combine(installPath, "config");
                    if (Directory.Exists(configDir))
                    {
                        _apoConfigDir = configDir;
                        _isAvailable = true;
                        Debug.WriteLine($"[EqualizerAPO] Detected via registry: {configDir}");
                        return true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[EqualizerAPO] Registry check failed: {ex.Message}");
        }

        // 2. デフォルトパスにフォールバック
        var defaultConfigDir = Path.Combine(APO_DEFAULT_PATH, "config");
        if (Directory.Exists(defaultConfigDir) &&
            File.Exists(Path.Combine(defaultConfigDir, MAIN_CONFIG)))
        {
            _apoConfigDir = defaultConfigDir;
            _isAvailable = true;
            Debug.WriteLine($"[EqualizerAPO] Detected at default path: {defaultConfigDir}");
            return true;
        }

        Debug.WriteLine("[EqualizerAPO] Not installed");
        _isAvailable = false;
        return false;
    }

    private bool EnsureIncludeDirective()
    {
        if (_apoConfigDir == null) return false;

        var configPath = Path.Combine(_apoConfigDir, MAIN_CONFIG);
        try
        {
            var lines = File.ReadAllLines(configPath).ToList();

            // 既に Include 行がある場合は何もしない
            if (lines.Any(l => l.Trim() == INCLUDE_DIRECTIVE))
                return true;

            lines.Add("");
            lines.Add("# Added by ugokuDeskTop");
            lines.Add(INCLUDE_DIRECTIVE);

            File.WriteAllLines(configPath, lines);
            Debug.WriteLine("[EqualizerAPO] Include directive added to config.txt");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[EqualizerAPO] Failed to add Include: {ex.Message}");
            return false;
        }
    }

    private bool WriteFilterConfig(string content)
    {
        if (_apoConfigDir == null) return false;

        var path = Path.Combine(_apoConfigDir, UGOKU_CONFIG);
        try
        {
            File.WriteAllText(path, content);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[EqualizerAPO] Write failed: {ex.Message}");
            return false;
        }
    }

    private static (string filterType, double freqHz, double q, double gainDb) CalculateFilter(
        double mouseX, double mouseY, string mode)
    {
        switch (mode)
        {
            case "LP/HP":
                return CalculateLpHp(mouseX, mouseY);
            case "BP":
                return CalculateFreqQ(mouseX, mouseY, "BP");
            case "NO":
                return CalculateFreqQ(mouseX, mouseY, "NO");
            case "PK":
                return CalculatePeaking(mouseX, mouseY);
            case "LSC":
                return CalculateShelf(mouseX, mouseY, "LSC");
            case "HSC":
                return CalculateShelf(mouseX, mouseY, "HSC");
            default:
                return ("OFF", 0, 1, 0);
        }
    }

    /// <summary>LP/HP モード: 左=ローパス、中央=OFF、右=ハイパス</summary>
    private static (string, double, double, double) CalculateLpHp(double mouseX, double mouseY)
    {
        double q = MaxQ - (MaxQ - MinQ) * mouseY;
        double center = 0.5;

        if (mouseX < center - DeadZone)
        {
            double t = mouseX / (center - DeadZone);
            double freqHz = MinFreqHz * Math.Pow(MaxFreqHz / MinFreqHz, t);
            return ("LP", freqHz, q, 0);
        }
        else if (mouseX > center + DeadZone)
        {
            double t = (mouseX - center - DeadZone) / (center - DeadZone);
            double freqHz = MinFreqHz * Math.Pow(MaxFreqHz / MinFreqHz, t);
            return ("HP", freqHz, q, 0);
        }
        else
        {
            return ("OFF", 0, q, 0);
        }
    }

    /// <summary>BP/NO モード: X=中心周波数、Y=Q値</summary>
    private static (string, double, double, double) CalculateFreqQ(double mouseX, double mouseY, string type)
    {
        double freqHz = MinFreqHz * Math.Pow(MaxFreqHz / MinFreqHz, mouseX);
        double q = MaxQ - (MaxQ - MinQ) * mouseY;
        return (type, freqHz, q, 0);
    }

    /// <summary>PK モード: X=中心周波数、Y=ゲイン（上=ブースト、下=カット）</summary>
    private static (string, double, double, double) CalculatePeaking(double mouseX, double mouseY)
    {
        double freqHz = MinFreqHz * Math.Pow(MaxFreqHz / MinFreqHz, mouseX);
        // Y=0(上) → +15dB、Y=0.5(中央) → 0dB、Y=1(下) → -15dB
        double gainDb = MaxGainDb - (MaxGainDb - MinGainDb) * mouseY;
        double q = 2.0; // 固定Q
        return ("PK", freqHz, q, gainDb);
    }

    /// <summary>LSC/HSC モード: X=コーナー周波数、Y=ゲイン</summary>
    private static (string, double, double, double) CalculateShelf(double mouseX, double mouseY, string type)
    {
        double freqHz = MinFreqHz * Math.Pow(MaxFreqHz / MinFreqHz, mouseX);
        double gainDb = MaxGainDb - (MaxGainDb - MinGainDb) * mouseY;
        return (type, freqHz, 0.7, gainDb); // Q=0.7（デフォルトスロープ）
    }
}
