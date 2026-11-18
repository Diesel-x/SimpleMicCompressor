namespace MicCompressor.Audio.Dsp;

/// <summary>
/// Second-order Butterworth high-pass filter (biquad).
/// Cuts sub-bass rumble, microphone thumps, and fan humming.
/// </summary>
public sealed class BiquadHighPassFilter
{
    private float _b0, _b1, _b2, _a1, _a2;
    private float _x1, _x2, _y1, _y2;
    private float _sampleRate = 48000f;
    private float _cutoffFrequency = 80f;

    public BiquadHighPassFilter(float cutoffFrequency = 80f, float sampleRate = 48000f)
    {
        Configure(cutoffFrequency, sampleRate);
    }

    public void Configure(float cutoffFrequency, float sampleRate)
    {
        _cutoffFrequency = Math.Clamp(cutoffFrequency, 20f, 500f);
        _sampleRate = Math.Max(sampleRate, 8000f);

        // Standard Butterworth 2nd order HPF
        float w0 = 2.0f * MathF.PI * _cutoffFrequency / _sampleRate;
        float cosW0 = MathF.Cos(w0);
        float sinW0 = MathF.Sin(w0);
        float alpha = sinW0 / (2.0f * 0.70710678f); // Q = 1/sqrt(2) = 0.7071

        float a0 = 1.0f + alpha;
        _b0 = ((1.0f + cosW0) / 2.0f) / a0;
        _b1 = (-(1.0f + cosW0)) / a0;
        _b2 = ((1.0f + cosW0) / 2.0f) / a0;
        _a1 = (-2.0f * cosW0) / a0;
        _a2 = (1.0f - alpha) / a0;
    }

    public void Reset()
    {
        _x1 = 0f;
        _x2 = 0f;
        _y1 = 0f;
        _y2 = 0f;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public float Process(float input)
    {
        if (float.IsNaN(input) || float.IsInfinity(input)) input = 0f;

        // Direct Form I biquad difference equation
        float output = _b0 * input + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;

        if (float.IsNaN(output) || float.IsInfinity(output))
        {
            output = 0f;
            _x1 = 0f; _x2 = 0f; _y1 = 0f; _y2 = 0f;
        }
        else
        {
            _x2 = _x1;
            _x1 = input;
            _y2 = _y1;
            _y1 = output;
        }

        return output;
    }
}
