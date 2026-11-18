namespace MicCompressor.Audio.Dsp;

/// <summary>
/// Fast transparent peak limiter and soft-clipper.
/// Prevents harsh digital clipping and audio distortion when shouting or speaking loudly.
/// </summary>
public sealed class FastLimiter
{
    private float _sampleRate = 48000f;
    private float _ceilingDb = -1.0f;
    private float _ceilingLinear = 0.89125f;
    private float _releaseTimeMs = 40f;
    private float _releaseCoeff;
    private float _gainEnvelope = 1.0f;

    public FastLimiter(float ceilingDb = -1.0f, float releaseMs = 40f, float sampleRate = 48000f)
    {
        Configure(ceilingDb, releaseMs, sampleRate);
    }

    public void Configure(float ceilingDb, float releaseMs, float sampleRate)
    {
        _ceilingDb = Math.Clamp(ceilingDb, -6.0f, 0.0f);
        _ceilingLinear = MathF.Pow(10.0f, _ceilingDb / 20.0f);
        _releaseTimeMs = Math.Max(5f, releaseMs);
        _sampleRate = Math.Max(8000f, sampleRate);
        _releaseCoeff = MathF.Exp(-1.0f / (_releaseTimeMs * 0.001f * _sampleRate));
    }

    public void Reset()
    {
        _gainEnvelope = 1.0f;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public float Process(float input)
    {
        if (float.IsNaN(input) || float.IsInfinity(input)) input = 0f;
        float absIn = MathF.Abs(input);

        // Peak gain reduction with smooth release
        if (absIn > _ceilingLinear)
        {
            float targetGain = _ceilingLinear / absIn;
            if (targetGain < _gainEnvelope)
            {
                _gainEnvelope = targetGain;
            }
            else
            {
                _gainEnvelope = _releaseCoeff * _gainEnvelope + (1.0f - _releaseCoeff) * targetGain;
            }
        }
        else
        {
            // Smooth release back to unity gain
            _gainEnvelope = _releaseCoeff * _gainEnvelope + (1.0f - _releaseCoeff) * 1.0f;
        }

        float limited = input * _gainEnvelope;

        // Transparent soft knee saturation above 95% of ceiling for extra safety against inter-sample peaks
        float softThreshold = _ceilingLinear * 0.95f;
        if (MathF.Abs(limited) > softThreshold)
        {
            float sign = MathF.Sign(limited);
            float excess = (MathF.Abs(limited) - softThreshold) / (_ceilingLinear - softThreshold);
            excess = MathF.Tanh(excess);
            limited = sign * (softThreshold + excess * (_ceilingLinear - softThreshold));
        }

        return Math.Clamp(limited, -1.0f, 1.0f);
    }
}
