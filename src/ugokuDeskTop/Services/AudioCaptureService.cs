using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace ugokuDeskTop.Services;

internal class AudioCaptureService : IDisposable
{
    private WasapiLoopbackCapture? _capture;
    private const int DftSize = 4096;   // 分解能 11.7Hz/bin（低音域の分離を改善）
    private const int HopSize = 512;   // DFT 更新間隔（~10.7ms @ 48kHz）
    private const int BandCount = 64;
    private readonly float[] _ringBuffer = new float[DftSize];
    private int _ringPos;
    private int _totalSamples;
    private int _samplesSinceLastDft;
    private readonly float[] _bands = new float[BandCount];
    private readonly object _lock = new();
    private bool _running;
    private int _sampleRate = 48000;

    public event Action<float[]>? FrequencyDataReady;

    public void Start()
    {
        if (_running) return;

        try
        {
            _capture = new WasapiLoopbackCapture();
            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;
            _capture.StartRecording();
            _sampleRate = _capture.WaveFormat.SampleRate;
            _running = true;
            Debug.WriteLine($"[AudioCapture] Started (sampleRate={_sampleRate})");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AudioCapture] Failed to start: {ex.Message}");
        }
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;

        try
        {
            _capture?.StopRecording();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AudioCapture] Error stopping: {ex.Message}");
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (!_running || e.BytesRecorded == 0) return;

        var waveFormat = _capture!.WaveFormat;
        int bytesPerSample = waveFormat.BitsPerSample / 8;
        int channels = waveFormat.Channels;
        int sampleCount = e.BytesRecorded / (bytesPerSample * channels);

        for (int i = 0; i < sampleCount; i++)
        {
            float sample = 0;
            for (int ch = 0; ch < channels; ch++)
            {
                int offset = (i * channels + ch) * bytesPerSample;
                if (offset + bytesPerSample <= e.BytesRecorded)
                {
                    sample += BitConverter.ToSingle(e.Buffer, offset);
                }
            }
            sample /= channels;

            // リングバッファに書き込み
            _ringBuffer[_ringPos] = sample;
            _ringPos = (_ringPos + 1) % DftSize;
            _totalSamples++;
            _samplesSinceLastDft++;

            // HopSize (512) サンプルごとに DFT 実行（窓をスライド）
            // 2048 サンプル窓を保ちつつ ~10.7ms 間隔で更新
            if (_samplesSinceLastDft >= HopSize && _totalSamples >= DftSize)
            {
                // リングバッファから最新 DftSize サンプルを時系列順に取り出す
                float[] samples = new float[DftSize];
                for (int j = 0; j < DftSize; j++)
                {
                    samples[j] = _ringBuffer[(_ringPos + j) % DftSize];
                }
                ComputeDft(samples, DftSize);
                _samplesSinceLastDft = 0;
            }
        }
    }

    /// <summary>
    /// C++ の audio_capture.cpp の ComputeFFT + SimpleDFT を移植。
    /// Hann 窓 → DFT で 64 バンドの振幅 → dB 変換 → 正規化 → スムージング。
    /// </summary>
    private void ComputeDft(float[] samples, int count)
    {
        int N = Math.Min(count, DftSize);

        // Hann 窓適用
        float[] windowed = new float[N];
        for (int i = 0; i < N; i++)
        {
            float window = 0.5f * (1.0f - MathF.Cos(2.0f * MathF.PI * i / (N - 1)));
            windowed[i] = samples[i] * window;
        }

        // 3帯域分割マッピング（各帯域内は対数、浮動小数点ビン）
        //   Low  bands  0-11 (12本):   30 -  200 Hz  キック・ベース
        //   Mid  bands 12-53 (42本):  200 - 4000 Hz  ボーカル・スネア・ギター
        //   High bands 54-63 (10本): 4000 -12000 Hz  ハイハット・シンバル
        int halfN = N / 2;
        float freqPerBin = (float)_sampleRate / N;

        const int LowEnd = 12;   // bands 0-11
        const int MidEnd = 54;   // bands 12-53

        float[] magnitudes = new float[BandCount];

        for (int band = 0; band < BandCount; band++)
        {
            float centerFreq;
            if (band < LowEnd)
            {
                // Low: 30-200 Hz (12 bands)
                float t = (band + 0.5f) / LowEnd;
                centerFreq = 30.0f * MathF.Pow(200.0f / 30.0f, t);
            }
            else if (band < MidEnd)
            {
                // Mid: 200-4000 Hz (42 bands)
                float t = (band - LowEnd + 0.5f) / (MidEnd - LowEnd);
                centerFreq = 200.0f * MathF.Pow(4000.0f / 200.0f, t);
            }
            else
            {
                // High: 4000-12000 Hz (10 bands)
                float t = (band - MidEnd + 0.5f) / (BandCount - MidEnd);
                centerFreq = 4000.0f * MathF.Pow(12000.0f / 4000.0f, t);
            }

            float kf = centerFreq / freqPerBin;
            kf = Math.Clamp(kf, 1.0f, halfN - 1.0f);

            float real = 0.0f;
            float imag = 0.0f;
            for (int n = 0; n < N; n++)
            {
                float angle = 2.0f * MathF.PI * kf * n / N;
                real += windowed[n] * MathF.Cos(angle);
                imag -= windowed[n] * MathF.Sin(angle);
            }

            magnitudes[band] = MathF.Sqrt(real * real + imag * imag) / N;
        }

        // dB スケール変換 + スムージング
        const float DB_MIN = -80.0f;
        const float DB_MAX = -20.0f;

        lock (_lock)
        {
            for (int i = 0; i < BandCount; i++)
            {
                float raw = magnitudes[i];

                // dB 変換
                float dB;
                if (raw < 1e-10f)
                    dB = -100.0f;
                else
                    dB = 20.0f * MathF.Log10(raw);

                // -80dB～-20dB を 0.0～1.0 に正規化
                float val = (dB - DB_MIN) / (DB_MAX - DB_MIN);
                val = Math.Clamp(val, 0.0f, 1.0f);

                // スムージング: 前フレーム 30% + 今フレーム 70%（低レイテンシ）
                _bands[i] = _bands[i] * 0.3f + val * 0.7f;
            }
        }

        FrequencyDataReady?.Invoke(_bands);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        Debug.WriteLine($"[AudioCapture] Stopped: {e.Exception?.Message ?? "OK"}");
    }

    public void Dispose()
    {
        Stop();
        _capture?.Dispose();
        _capture = null;
    }
}
