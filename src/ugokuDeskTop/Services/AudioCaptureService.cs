using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace ugokuDeskTop.Services;

internal class AudioCaptureService : IDisposable
{
    private WasapiLoopbackCapture? _capture;
    private const int DftSize = 512;
    private const int BandCount = 64;
    private readonly float[] _sampleBuffer = new float[2048];
    private int _sampleBufferPos;
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

            _sampleBuffer[_sampleBufferPos] = sample;
            _sampleBufferPos++;

            // 512サンプル溜まったら DFT 実行
            if (_sampleBufferPos >= DftSize)
            {
                ComputeDft(_sampleBuffer, _sampleBufferPos);
                _sampleBufferPos = 0;
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

        // DFT で各バンドの振幅を直接計算（線形ビンマッピング、上限 18kHz）
        int halfN = N / 2;
        float freqPerBin = (float)_sampleRate / N;

        int maxBin = (int)(18000.0f / freqPerBin);
        if (maxBin >= halfN) maxBin = halfN - 1;
        if (maxBin < 1) maxBin = 1;

        float[] magnitudes = new float[BandCount];

        for (int band = 0; band < BandCount; band++)
        {
            // 線形マッピング: バンド 0 → ビン 1, バンド 63 → maxBin
            int k = 1 + band * (maxBin - 1) / BandCount;
            if (k >= halfN) k = halfN - 1;

            float real = 0.0f;
            float imag = 0.0f;
            for (int n = 0; n < N; n++)
            {
                float angle = 2.0f * MathF.PI * k * n / N;
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

                // スムージング: 前フレーム 55% + 今フレーム 45%
                _bands[i] = _bands[i] * 0.55f + val * 0.45f;
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
