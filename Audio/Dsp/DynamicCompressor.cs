namespace MicCompressor.Audio.Dsp;

/// <summary>
/// Broadcast-grade dynamic range compressor with soft knee, peak/RMS detection,
/// ballistics smoothing, makeup gain, and gain reduction metering.
/// </summary>
public sealed class DynamicCompressor
{
    private float _sampleRate = 48000f;
    private float _thresholdDb = -20f;
    private float _ratio = 4f;
    private float _kneeDb = 4f;
    private float _attackMs = 10f;
    private float _releaseMs = 150f;
    private float _makeupGainDb = 5f;

    private float _attackCoeff;
    private float _releaseCoeff;
    private float _makeupFactor = 1f;

    private float _detectorEnvelope = 0f;
    private float _gainReductionEnvelope = 0f; // in dB

    /// <summary>
    /// Current gain reduction in dB (e.g., -6.0 dB when compressing).
    /// </summary>
    public float CurrentGainReductionDb => -_gainReductionEnvelope;

    public DynamicCompressor(
        float thresholdDb = -20f,
        float ratio = 4f,
        float attackMs = 10f,
        float releaseMs = 150f,
        float makeupGainDb = 5f,
        float kneeDb = 4f,
        float sampleRate = 48000f)
    {
        Configure(thresholdDb, ratio, attackMs, releaseMs, makeupGainDb, kneeDb, sampleRate);
    }

    public void Configure(
        float thresholdDb,
        float ratio,
        float attackMs,
        float releaseMs,
        float makeupGainDb,
        float kneeDb,
        float sampleRate)
    {
        _thresholdDb = Math.Clamp(thresholdDb, -60f, 0f);
        _ratio = Math.Max(1.0f, ratio);
        _kneeDb = Math.Max(0.1f, kneeDb);
        _attackMs = Math.Max(0.5f, attackMs);
        _releaseMs = Math.Max(5f, releaseMs);
        _makeupGainDb = Math.Clamp(makeupGainDb, 0f, 24f);
        _sampleRate = Math.Max(8000f, sampleRate);

        _attackCoeff = MathF.Exp(-1.0f / (_attackMs * 0.001f * _sampleRate));
        _releaseCoeff = MathF.Exp(-1.0f / (_releaseMs * 0.001f * _sampleRate));
        _makeupFactor = MathF.Pow(10.0f, _makeupGainDb / 20.0f);
    }

    public void Reset()
    {
        _detectorEnvelope = 0f;
        _gainReductionEnvelope = 0f;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public float Process(float input)
    {
        float absIn = MathF.Abs(input);

        // Fast RMS/Peak hybrid detector (5 ms averaging)
        const float detectorAlpha = 0.04f;
        _detectorEnvelope = (1.0f - detectorAlpha) * _detectorEnvelope + detectorAlpha * absIn;
        float inputDb = 20.0f * MathF.Log10(MathF.Max(_detectorEnvelope, 1e-6f));

        // Soft-knee compression curve
        float halfKnee = _kneeDb * 0.5f;
        float grDb = 0f;

        if (inputDb <= _thresholdDb - halfKnee)
        {
            // Below knee, no reduction
            grDb = 0f;
        }
        else if (inputDb > _thresholdDb + halfKnee)
        {
            // Above knee: full linear compression
            grDb = (inputDb - _thresholdDb) * (1.0f - 1.0f / _ratio);
        }
        else
        {
            // In knee region: quadratic interpolation
            float delta = inputDb - _thresholdDb + halfKnee;
            grDb = ((delta * delta) / (2.0f * _kneeDb)) * (1.0f - 1.0f / _ratio);
        }

        // Apply attack and release ballistics to the gain reduction
        if (grDb > _gainReductionEnvelope)
        {
            // Attack (reducing volume)
            _gainReductionEnvelope = _attackCoeff * _gainReductionEnvelope + (1.0f - _attackCoeff) * grDb;
        }
        else
        {
            // Release (recovering volume)
            _gainReductionEnvelope = _releaseCoeff * _gainReductionEnvelope + (1.0f - _releaseCoeff) * grDb;
        }

        // Convert gain reduction dB to linear multiplier + makeup gain
        float linearGain = MathF.Pow(10.0f, -_gainReductionEnvelope / 20.0f) * _makeupFactor;

        return input * linearGain;
    }
}
