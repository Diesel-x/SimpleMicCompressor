namespace MicCompressor.Audio.Dsp;

/// <summary>
/// Smooth noise gate with hysteresis and hold time.
/// Eliminates background keyboard clicking, breathing, and PC fan noise.
/// </summary>
public sealed class NoiseGate
{
    private float _sampleRate = 48000f;
    private float _thresholdDb = -42f;
    private float _hysteresisDb = 2.5f;
    private float _attackTimeMs = 4f;
    private float _releaseTimeMs = 120f;
    private float _holdTimeMs = 60f;

    private float _attackCoeff;
    private float _releaseCoeff;
    private float _detectorAlpha = 0.05f;
    private int _holdSamples;
    private int _holdCounter;

    private float _currentGain = 0f;
    private float _envelope = 0f;
    private bool _isOpen = false;

    public bool IsOpen => _isOpen;
    public float CurrentGain => _currentGain;

    public NoiseGate(float thresholdDb = -42f, float attackMs = 4f, float releaseMs = 120f, float holdMs = 60f, float sampleRate = 48000f)
    {
        Configure(thresholdDb, attackMs, releaseMs, holdMs, sampleRate);
    }

    public void Configure(float thresholdDb, float attackMs, float releaseMs, float holdMs, float sampleRate)
    {
        _thresholdDb = Math.Clamp(thresholdDb, -80f, -10f);
        _attackTimeMs = Math.Max(0.5f, attackMs);
        _releaseTimeMs = Math.Max(10f, releaseMs);
        _holdTimeMs = Math.Max(0f, holdMs);
        _sampleRate = Math.Max(8000f, sampleRate);

        _attackCoeff = MathF.Exp(-1.0f / (_attackTimeMs * 0.001f * _sampleRate));
        _releaseCoeff = MathF.Exp(-1.0f / (_releaseTimeMs * 0.001f * _sampleRate));
        _detectorAlpha = 1.0f - MathF.Exp(-1.0f / (1.5f * 0.001f * _sampleRate));
        _holdSamples = (int)(_holdTimeMs * 0.001f * _sampleRate);
    }

    public void Reset()
    {
        _currentGain = 0f;
        _envelope = 0f;
        _holdCounter = 0;
        _isOpen = false;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public float ComputeGain(float detectorSample)
    {
        if (float.IsNaN(detectorSample) || float.IsInfinity(detectorSample)) detectorSample = 0f;
        float absIn = MathF.Abs(detectorSample);

        // Fast RMS / peak tracker for detection, sample-rate scaled
        _envelope = (1.0f - _detectorAlpha) * _envelope + _detectorAlpha * absIn;
        if (float.IsNaN(_envelope)) _envelope = 0f;

        float envDb = 8.6858896f * MathF.Log(MathF.Max(_envelope, 1e-6f));
        float closeThresholdDb = _thresholdDb - _hysteresisDb;

        if (envDb >= _thresholdDb)
        {
            _isOpen = true;
            _holdCounter = _holdSamples;
        }
        else
        {
            if (_isOpen)
            {
                if (_holdCounter > 0)
                {
                    _holdCounter--;
                }
                else if (envDb < closeThresholdDb)
                {
                    _isOpen = false;
                }
            }
        }

        // Smooth gain transition
        float targetGain = _isOpen ? 1.0f : 0.0f;
        if (targetGain > _currentGain)
        {
            _currentGain = _attackCoeff * _currentGain + (1.0f - _attackCoeff) * targetGain;
        }
        else
        {
            _currentGain = _releaseCoeff * _currentGain + (1.0f - _releaseCoeff) * targetGain;
        }

        if (_currentGain < 0.0001f) _currentGain = 0f;
        if (_currentGain > 0.9999f) _currentGain = 1f;

        return _currentGain;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public float Process(float input)
    {
        return input * ComputeGain(input);
    }
}
