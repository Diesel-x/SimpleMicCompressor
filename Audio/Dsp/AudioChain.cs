using MicCompressor.Models;

namespace MicCompressor.Audio.Dsp;

/// <summary>
/// Orchestrates the DSP signal processing pipeline for microphone audio.
/// Process order: High-Pass (Low-Cut) -> Noise Gate -> Compressor -> Limiter.
/// </summary>
public sealed class AudioChain
{
    private sealed record DspParameters(
        float SampleRate,
        float HighPassCutoffHz,
        float LimiterCeilingDb,
        float LimiterReleaseMs,
        float GateThresholdDb,
        float GateAttackMs,
        float GateReleaseMs,
        float GateHoldMs,
        float CompressorThresholdDb,
        float CompressorRatio,
        float CompressorAttackMs,
        float CompressorReleaseMs,
        float CompressorMakeupGainDb,
        float CompressorKneeDb
    );

    private BiquadHighPassFilter[] _hpFilters = [new(), new(), new(), new(), new(), new(), new(), new()];
    private FastLimiter[] _limiters = [new(), new(), new(), new(), new(), new(), new(), new()];
    private readonly NoiseGate _gate = new();
    private readonly DynamicCompressor _compressor = new();
    private readonly AudioMetrics _metrics = new();

    private float _sampleRate = 48000f;
    private float _highPassCutoffHz = 80f;
    private float _limiterCeilingDb = -1f;
    private float _limiterReleaseMs = 40f;
    private AudioSettings _settings;
    private DspParameters? _pendingParameters;

    // Smoothed meter decay rates
    private float _inputPeakTracker = 0f;
    private float _outputPeakTracker = 0f;

    public AudioMetrics Metrics => _metrics;

    public AudioChain(AudioSettings settings, float sampleRate = 48000f)
    {
        _settings = settings;
        _sampleRate = sampleRate;
        ApplyParameters(CaptureParameters(settings, sampleRate));
    }

    public void UpdateSettings(AudioSettings settings, float sampleRate)
    {
        Volatile.Write(ref _settings, settings);
        Interlocked.Exchange(ref _pendingParameters, CaptureParameters(settings, sampleRate));
    }

    private static DspParameters CaptureParameters(AudioSettings settings, float sampleRate) => new(
        sampleRate,
        settings.HighPassCutoffHz,
        settings.LimiterCeilingDb,
        settings.LimiterReleaseMs,
        settings.GateThresholdDb,
        settings.GateAttackMs,
        settings.GateReleaseMs,
        settings.GateHoldMs,
        settings.CompressorThresholdDb,
        settings.CompressorRatio,
        settings.CompressorAttackMs,
        settings.CompressorReleaseMs,
        settings.CompressorMakeupGainDb,
        settings.CompressorKneeDb
    );

    private void ApplyPendingParameters()
    {
        var pending = Interlocked.Exchange(ref _pendingParameters, null);
        if (pending != null)
        {
            ApplyParameters(pending);
        }
    }

    private void ApplyParameters(DspParameters p)
    {
        _sampleRate = p.SampleRate;
        _highPassCutoffHz = p.HighPassCutoffHz;
        _limiterCeilingDb = p.LimiterCeilingDb;
        _limiterReleaseMs = p.LimiterReleaseMs;

        for (int i = 0; i < _hpFilters.Length; i++)
        {
            _hpFilters[i].Configure(p.HighPassCutoffHz, p.SampleRate);
            _limiters[i].Configure(p.LimiterCeilingDb, p.LimiterReleaseMs, p.SampleRate);
        }

        _gate.Configure(p.GateThresholdDb, p.GateAttackMs, p.GateReleaseMs, p.GateHoldMs, p.SampleRate);
        _compressor.Configure(
            p.CompressorThresholdDb,
            p.CompressorRatio,
            p.CompressorAttackMs,
            p.CompressorReleaseMs,
            p.CompressorMakeupGainDb,
            p.CompressorKneeDb,
            p.SampleRate
        );
    }

    public void Reset()
    {
        ApplyPendingParameters();
        foreach (var hp in _hpFilters) hp.Reset();
        foreach (var lim in _limiters) lim.Reset();
        _gate.Reset();
        _compressor.Reset();
        _metrics.Reset();
        _inputPeakTracker = 0f;
        _outputPeakTracker = 0f;
    }

    /// <summary>
    /// Processes a block of interleaved float samples in-place.
    /// Supports mono or multi-channel.
    /// </summary>
    public void ProcessInterleaved(float[] buffer, int offset, int sampleCount, int channels)
    {
        if (sampleCount <= 0 || channels <= 0) return;

        ApplyPendingParameters();

        if (_hpFilters.Length < channels)
        {
            int oldLen = _hpFilters.Length;
            Array.Resize(ref _hpFilters, channels);
            Array.Resize(ref _limiters, channels);
            for (int i = oldLen; i < channels; i++)
            {
                _hpFilters[i] = new BiquadHighPassFilter();
                _hpFilters[i].Configure(_highPassCutoffHz, _sampleRate);
                _limiters[i] = new FastLimiter();
                _limiters[i].Configure(_limiterCeilingDb, _limiterReleaseMs, _sampleRate);
            }
        }

        AudioSettings settings = Volatile.Read(ref _settings);
        bool isMuted = settings.IsMuted;
        bool bypass = settings.BypassAll;
        bool memeMode = settings.DestroyedMicMemeMode;
        bool hpEnabled = settings.HighPassEnabled;
        bool gateEnabled = settings.GateEnabled;
        bool compEnabled = settings.CompressorEnabled;
        bool limEnabled = settings.LimiterEnabled;

        float blockInputPeak = 0f;
        float blockOutputPeak = 0f;

        int frameCount = sampleCount / channels;

        for (int frame = 0; frame < frameCount; frame++)
        {
            int baseIdx = offset + frame * channels;

            // 1. Calculate raw input peak for meters
            float rawMono = 0f;
            for (int ch = 0; ch < channels; ch++)
            {
                rawMono += buffer[baseIdx + ch];
            }
            rawMono /= channels;

            float absRaw = MathF.Abs(rawMono);
            if (absRaw > blockInputPeak) blockInputPeak = absRaw;

            if (isMuted)
            {
                for (int ch = 0; ch < channels; ch++)
                {
                    buffer[baseIdx + ch] = 0f;
                }
                continue;
            }

            if (bypass)
            {
                if (absRaw > blockOutputPeak) blockOutputPeak = absRaw;
                continue;
            }

            // 2. FUN MEME MODE ("Уничтоженный микрофон")
            if (memeMode)
            {
                // The buggy HPF phase-shifted division ratio that crushed audio into funny square waves
                float processed = _hpFilters[0].Process(rawMono);
                if (gateEnabled) processed = _gate.Process(processed);
                if (compEnabled) processed = _compressor.Process(processed);
                if (limEnabled) processed = _limiters[0].Process(processed);

                float ratio = MathF.Abs(rawMono) > 1e-6f ? (processed / rawMono) : 0f;
                if (float.IsNaN(ratio) || float.IsInfinity(ratio)) ratio = 1f;

                for (int ch = 0; ch < channels; ch++)
                {
                    float chSample = buffer[baseIdx + ch];
                    if (hpEnabled) chSample = _hpFilters[ch].Process(chSample);
                    float outSample = Math.Clamp(chSample * ratio, -1.0f, 1.0f);
                    buffer[baseIdx + ch] = outSample;

                    float absOut = MathF.Abs(outSample);
                    if (absOut > blockOutputPeak) blockOutputPeak = absOut;
                }
                continue;
            }

            // 3. CLEAN STUDIO MODE (Pristine High-Pass + Clean Gate + Smooth Compression)
            // Apply Low-Cut filter to each channel cleanly with its own filter instance
            if (hpEnabled)
            {
                for (int ch = 0; ch < channels; ch++)
                {
                    buffer[baseIdx + ch] = _hpFilters[ch].Process(buffer[baseIdx + ch]);
                }
            }

            // Measure filtered mono signal for detector envelope analysis
            float filteredMono = 0f;
            for (int ch = 0; ch < channels; ch++)
            {
                filteredMono += buffer[baseIdx + ch];
            }
            filteredMono /= channels;

            // Calculate smooth DSP gain multipliers
            float gateGain = gateEnabled ? _gate.ComputeGain(filteredMono) : 1.0f;
            float compGain = compEnabled ? _compressor.ComputeGain(filteredMono) : 1.0f;
            float totalGain = gateGain * compGain;

            // Apply gain and limiter to each channel cleanly
            for (int ch = 0; ch < channels; ch++)
            {
                float sample = buffer[baseIdx + ch] * totalGain;

                if (limEnabled)
                {
                    sample = _limiters[ch].Process(sample);
                }

                sample = Math.Clamp(sample, -1.0f, 1.0f);
                buffer[baseIdx + ch] = sample;

                float absSample = MathF.Abs(sample);
                if (absSample > blockOutputPeak) blockOutputPeak = absSample;
            }
        }

        // Time-consistent smooth meter ballistics (decay based on block duration)
        float blockDurationSec = (float)frameCount / _sampleRate;
        float decay = MathF.Exp(-blockDurationSec / 0.150f);
        _inputPeakTracker = MathF.Max(blockInputPeak, _inputPeakTracker * decay);
        _outputPeakTracker = MathF.Max(blockOutputPeak, _outputPeakTracker * decay);

        float inDb = _inputPeakTracker > 1e-5f ? 20.0f * MathF.Log10(_inputPeakTracker) : -60.0f;
        float outDb = _outputPeakTracker > 1e-5f ? 20.0f * MathF.Log10(_outputPeakTracker) : -60.0f;

        _metrics.InputPeakDb = Math.Clamp(inDb, -60.0f, 3.0f);
        _metrics.OutputPeakDb = Math.Clamp(outDb, -60.0f, 3.0f);
        _metrics.GainReductionDb = compEnabled && !bypass ? _compressor.CurrentGainReductionDb : 0.0f;
        _metrics.IsGateOpen = !gateEnabled || bypass || _gate.IsOpen;
    }
}
