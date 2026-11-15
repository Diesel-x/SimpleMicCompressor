namespace MicCompressor.Audio.Dsp;

/// <summary>
/// Holds thread-safe real-time audio metrics for UI meters.
/// </summary>
public sealed class AudioMetrics
{
    private long _inputPeakBits;
    private long _outputPeakBits;
    private long _gainReductionBits;
    private int _isGateOpen;

    public float InputPeakDb
    {
        get => BitConverter.Int64BitsToDouble(Interlocked.Read(ref _inputPeakBits)) is double d ? (float)d : -60f;
        set => Interlocked.Exchange(ref _inputPeakBits, BitConverter.DoubleToInt64Bits(value));
    }

    public float OutputPeakDb
    {
        get => BitConverter.Int64BitsToDouble(Interlocked.Read(ref _outputPeakBits)) is double d ? (float)d : -60f;
        set => Interlocked.Exchange(ref _outputPeakBits, BitConverter.DoubleToInt64Bits(value));
    }

    public float GainReductionDb
    {
        get => BitConverter.Int64BitsToDouble(Interlocked.Read(ref _gainReductionBits)) is double d ? (float)d : 0f;
        set => Interlocked.Exchange(ref _gainReductionBits, BitConverter.DoubleToInt64Bits(value));
    }

    public bool IsGateOpen
    {
        get => Volatile.Read(ref _isGateOpen) == 1;
        set => Volatile.Write(ref _isGateOpen, value ? 1 : 0);
    }

    public void Reset()
    {
        InputPeakDb = -60f;
        OutputPeakDb = -60f;
        GainReductionDb = 0f;
        IsGateOpen = false;
    }
}
