using MicCompressor.Models;

namespace MicCompressor.Audio.Dsp;

/// <summary>
/// Orchestrates the DSP signal processing pipeline for microphone audio.
/// Process order: High-Pass (Low-Cut) -> Noise Gate -> Compressor -> Limiter.
/// </summary>
public sealed class AudioChain
{
    private readonly BiquadHighPassFilter _highPass = new();
    private readonly NoiseGate _gate = new();
    private readonly DynamicCompressor _compressor = new();
    private readonly FastLimiter _limiter = new();
    private readonly AudioMetrics _metrics = new();

    private float _sampleRate = 48000f;
    private AudioSettings _settings;

    // Smoothed meter decay rates
    private float _inputPeakTracker = 0f;
    private float _outputPeakTracker = 0f;

    public AudioMetrics Metrics => _metrics;

    public AudioChain(AudioSettings settings, float sampleRate = 48000f)
    {
        _settings = settings;
        _sampleRate = sampleRate;
        UpdateSettings(settings, sampleRate);
    }

    public void UpdateSettings(AudioSettings settings, float sampleRate)
    {
        _settings = settings;
        _sampleRate = sampleRate;

        _highPass.Configure(settings.HighPassCutoffHz, sampleRate);
        _gate.Configure(settings.GateThresholdDb, settings.GateAttackMs, settings.GateReleaseMs, settings.GateHoldMs, sampleRate);
        _compressor.Configure(
            settings.CompressorThresholdDb,
            settings.CompressorRatio,
            settings.CompressorAttackMs,
            settings.CompressorReleaseMs,
            settings.CompressorMakeupGainDb,
            settings.CompressorKneeDb,
            sampleRate
        );
        _limiter.Configure(settings.LimiterCeilingDb, settings.LimiterReleaseMs, sampleRate);
    }

    public void Reset()
    {
        _highPass.Reset();
        _gate.Reset();
        _compressor.Reset();
        _limiter.Reset();
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

        bool bypass = _settings.BypassAll;
        bool hpEnabled = _settings.HighPassEnabled;
        bool gateEnabled = _settings.GateEnabled;
        bool compEnabled = _settings.CompressorEnabled;
        bool limEnabled = _settings.LimiterEnabled;

        float blockInputPeak = 0f;
        float blockOutputPeak = 0f;

        int frameCount = sampleCount / channels;

        for (int frame = 0; frame < frameCount; frame++)
        {
            int baseIdx = offset + frame * channels;

            // 1. Calculate combined/mono representation for detector analysis
            float monoIn = 0f;
            for (int ch = 0; ch < channels; ch++)
            {
                monoIn += buffer[baseIdx + ch];
            }
            monoIn /= channels;

            float absMonoIn = MathF.Abs(monoIn);
            if (absMonoIn > blockInputPeak) blockInputPeak = absMonoIn;

            if (bypass)
            {
                if (absMonoIn > blockOutputPeak) blockOutputPeak = absMonoIn;
                continue;
            }

            // 2. High-pass filter (per-channel or mono)
            float processedMono = monoIn;
            if (hpEnabled)
            {
                processedMono = _highPass.Process(processedMono);
            }

            // 3. Noise Gate
            if (gateEnabled)
            {
                processedMono = _gate.Process(processedMono);
            }

            // 4. Dynamic Compressor
            if (compEnabled)
            {
                processedMono = _compressor.Process(processedMono);
            }

            // 5. Fast Limiter
            if (limEnabled)
            {
                processedMono = _limiter.Process(processedMono);
            }

            float absMonoOut = MathF.Abs(processedMono);
            if (absMonoOut > blockOutputPeak) blockOutputPeak = absMonoOut;

            // Apply processed factor back to all channels
            float ratio = MathF.Abs(monoIn) > 1e-6f ? (processedMono / monoIn) : (gateEnabled && !_gate.IsOpen ? 0f : 1f);
            
            // Limit ratio to sane values to prevent explosive arithmetic
            if (float.IsNaN(ratio) || float.IsInfinity(ratio)) ratio = 1f;

            for (int ch = 0; ch < channels; ch++)
            {
                float chSample = buffer[baseIdx + ch];
                if (hpEnabled) chSample = _highPass.Process(chSample);
                buffer[baseIdx + ch] = Math.Clamp(chSample * ratio, -1.0f, 1.0f);
            }
        }

        // Smooth meter ballistics (decay)
        const float decay = 0.85f;
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
